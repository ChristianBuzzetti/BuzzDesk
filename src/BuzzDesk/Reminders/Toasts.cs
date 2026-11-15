using BuzzDesk.Data;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BuzzDesk;

/// <summary>
/// Windows toasts for reminders, with Snooze 10 min / 1 h / Tomorrow and Done buttons. Works for the zip build too:
/// the toolkit registers BuzzDesk as a notification source (name and icon from the .exe) the first time. Clicking a
/// toast opens the task or topic; a toast clicked while BuzzDesk is closed starts it.
/// </summary>
public static class Toasts
{
    public static bool Available { get; private set; } = true;

    /// <summary>Hooks button clicks; call once at startup.</summary>
    public static void Init(Action<string, long> onAction)
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += e =>
            {
                var args = ToastArguments.Parse(e.Argument);
                string action = args.TryGetValue("action", out string? a) ? a : "open";
                long id = args.TryGetValue("id", out string? s) && long.TryParse(s, out long x) ? x : 0;
                // Toasts call back on a background thread.
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => onAction(action, id));
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { Available = false; }
    }

    public static bool Show(Reminder r)
    {
        if (!Available) return false;
        try
        {
            string kind = r.Kind switch { "task" => L.T("Pendiente"), "topic" => L.T("Tema"), _ => L.T("Recordatorio") };
            var local = Clock.ToLocal(r.DueUtc);
            var builder = new ToastContentBuilder()
                .AddArgument("action", "open").AddArgument("id", r.Id)
                .AddText(r.Title.Length > 0 ? r.Title : L.T("Recordatorio"))
                .AddText(kind + " · " + Fmt.Time(TimeOnly.FromDateTime(local)) + (r.TaskDue is DateOnly d ? " · " + L.T("vence") + " " + Fmt.Day(d, Clock.Today).ToLower(L.Culture) : ""))
                .AddButton(new ToastButton().SetContent(L.T("10 min")).AddArgument("action", "snooze10").AddArgument("id", r.Id).SetBackgroundActivation())
                .AddButton(new ToastButton().SetContent(L.T("1 hora")).AddArgument("action", "snooze60").AddArgument("id", r.Id).SetBackgroundActivation())
                .AddButton(new ToastButton().SetContent(L.T("Mañana")).AddArgument("action", "tomorrow").AddArgument("id", r.Id).SetBackgroundActivation())
                .AddButton(new ToastButton().SetContent(L.T("Listo")).AddArgument("action", "done").AddArgument("id", r.Id).SetBackgroundActivation())
                .SetToastScenario(ToastScenario.Reminder);   // stays on screen until answered
            builder.Show(t => { t.Tag = "r" + r.Id; t.Group = "reminders"; });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        {
            Available = false;
            return false;
        }
    }

    /// <summary>One toast for several reminders missed while the PC was off or BuzzDesk closed.</summary>
    public static bool ShowMissed(IReadOnlyList<Reminder> missed)
    {
        if (!Available) return false;
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "today").AddArgument("id", 0)
                .AddText(L.F("{0} recordatorios mientras no estabas", missed.Count))
                .AddText(string.Join(" · ", missed.Take(3).Select(r => r.Title)) + (missed.Count > 3 ? " …" : ""))
                .AddButton(new ToastButton().SetContent(L.T("Ver en Hoy")).AddArgument("action", "today").AddArgument("id", 0))
                .Show(t => { t.Tag = "missed"; t.Group = "reminders"; });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        {
            Available = false;
            return false;
        }
    }

    /// <summary>Removes a reminder's toast from the Notification Center (answered in the app).</summary>
    public static void Remove(long reminderId)
    {
        if (!Available) return;
        try { ToastNotificationManagerCompat.History.Remove("r" + reminderId, "reminders"); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException) { }
    }
}
