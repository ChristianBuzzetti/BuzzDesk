using System.Windows.Threading;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>
/// Rings reminders while BuzzDesk runs (the tray is enough). Checks every 20 seconds and right after any change to
/// reminders; what was missed while BuzzDesk was closed rings at startup (several at once become one toast).
/// </summary>
public sealed class ReminderScheduler
{
    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
    bool startup = true;
    public event Action? Rang;

    public ReminderScheduler()
    {
        timer.Tick += (_, _) => Check();
        Db.Changed += what => { if (what is "reminder" or "task") System.Windows.Application.Current?.Dispatcher.BeginInvoke(Check, DispatcherPriority.Background); };
    }

    public void Start() { timer.Start(); Check(); }
    public void Stop() => timer.Stop();

    public void Check()
    {
        List<Reminder> due;
        try { due = Reminders.DueNow(DateTime.UtcNow); }
        catch (Microsoft.Data.Sqlite.SqliteException) { return; }   // busy for a moment: next tick
        bool wasStartup = startup;
        startup = false;
        if (due.Count == 0) return;
        Reminders.MarkFired(due.Select(r => r.Id), DateTime.UtcNow);
        // Missed while closed: more than 3 that are over 10 minutes late become one toast.
        var late = due.Where(r => DateTime.UtcNow - r.DueUtc > TimeSpan.FromMinutes(10)).ToList();
        var ring = due;
        if (wasStartup && late.Count > 3)
        {
            if (!Toasts.ShowMissed(late)) App.Current.Toast(L.F("{0} recordatorios mientras no estabas", late.Count), string.Join(" · ", late.Take(3).Select(r => r.Title)), () => App.Current.Navigate("today"));
            ring = due.Except(late).ToList();
        }
        foreach (var r in ring)
            if (!Toasts.Show(r)) App.Current.Toast(r.Title, L.T("Recordatorio") + " · " + Fmt.Time(TimeOnly.FromDateTime(Clock.ToLocal(r.DueUtc))), () => App.Current.OpenReminder(r.Id));
        Rang?.Invoke();
    }

    /// <summary>The toast buttons (and the app's own buttons).</summary>
    public static void Handle(string action, long id)
    {
        if (action == "today") { App.Current.Navigate("today"); return; }
        var r = Reminders.Get(id);
        if (r == null) return;
        switch (action)
        {
            case "snooze10": Reminders.Snooze(id, DateTime.UtcNow.AddMinutes(10)); break;
            case "snooze60": Reminders.Snooze(id, DateTime.UtcNow.AddHours(1)); break;
            case "tomorrow": Reminders.Snooze(id, TomorrowMorning()); break;
            case "done":
                Reminders.Dismiss(id);
                if (r.TaskId is long task && r.TaskOpen) Tasks.Complete(task, Clock.Today);
                break;
            default: App.Current.OpenReminder(id); return;
        }
        Toasts.Remove(id);
    }

    /// <summary>Tomorrow at 9:00 in the app's time zone.</summary>
    public static DateTime TomorrowMorning() => Clock.ToUtc(Clock.Today.AddDays(1), new TimeOnly(9, 0));
}
