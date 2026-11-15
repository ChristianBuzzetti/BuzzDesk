using System.Windows;
using System.Windows.Controls;

namespace BuzzDesk;

/// <summary>Home: what is due today or late, today's reminders, topics to review and pinned links, with Boberto.</summary>
public sealed class TodayView : DeskView
{
    public override string Key => "today";
    readonly StackPanel body = new() { Margin = new Thickness(8, 4, 8, 16) };
    readonly ContentControl stageHost = new() { Width = 300, Height = 200, Focusable = false, ToolTip = "Boberto" };
    readonly TextBlock heading = Ui.Title("");
    readonly TextBlock dateLine = Ui.Text("", "Muted", mono: true);
    BobertoStageControl? stage;

    public TodayView()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var top = new StackPanel { Margin = new Thickness(8, 4, 8, 0) };
        top.Children.Add(heading);
        top.Children.Add(dateLine);
        var main = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        main.Children.Add(top);
        main.Children.Add(Ui.Scroll(body));
        Children.Add(main);
        var side = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        side.Children.Add(stageHost);
        SetColumn(side, 1);
        Children.Add(side);
        App.Current.AppearanceChanged += SyncStage;
        Loaded += (_, _) => SyncStage(App.Current.Appearance);
    }

    void SyncStage(AppearanceSettings settings)
    {
        if (settings.BobertoVisible && stage == null) { stage = new BobertoStageControl(settings); stageHost.Content = stage; }
        else if (!settings.BobertoVisible && stage != null) { stageHost.Content = null; stage.Dispose(); stage = null; }
        stageHost.Visibility = settings.BobertoVisible ? Visibility.Visible : Visibility.Collapsed;
        if (stage != null) _ = stage.ApplyThemeAsync(settings);
    }

    public override void Shown() { base.Shown(); stage?.Wave(); }

    public override void Refresh()
    {
        var now = Clock.Now;
        heading.Text = L.T(now.Hour < 12 ? "Buen día" : now.Hour < 20 ? "Buenas tardes" : "Buenas noches");
        dateLine.Text = now.ToString(L.Es ? "dddd d 'de' MMMM · HH:mm" : "dddd, MMMM d · HH:mm", L.Culture);
        body.Children.Clear();
        body.Children.Add(Ui.Label(L.T("Para hoy")));
        body.Children.Add(Ui.Text(L.T("Nada por ahora."), "Muted"));
    }

    public override void Release() { App.Current.AppearanceChanged -= SyncStage; stage?.Dispose(); stage = null; }
}
