using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BuzzDesk;

/// <summary>A small edit dialog in the app's style: a title, labelled fields and Save / Cancel. The caller adds
/// controls, then <see cref="Ask"/> runs a validation that returns an error text or null to accept.</summary>
public sealed class FormWindow : Window
{
    readonly StackPanel fields = new();
    readonly TextBlock error = Ui.Text("", "ErrorInk", 12);
    Func<string?>? validate;

    public FormWindow(string title)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = title + " · BuzzDesk";
        Width = 500; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        root.Children.Add(Ui.Title(title));
        root.Children.Add(fields);
        error.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(error);
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        foot.Children.Add(new Button { Content = L.T("Cancelar"), IsCancel = true, MinWidth = 110 });
        var ok = new Button { Content = L.T("Guardar"), IsDefault = true, MinWidth = 110, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
        ok.SetResourceReference(BackgroundProperty, "Accent"); ok.SetResourceReference(ForegroundProperty, "OnAccent"); ok.SetResourceReference(BorderBrushProperty, "Accent");
        ok.Click += (_, _) => { if (validate?.Invoke() is string e) { error.Text = e; return; } DialogResult = true; };
        foot.Children.Add(ok);
        root.Children.Add(foot);
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => fields.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    public T Add<T>(string label, T control, string? hint = null) where T : UIElement
    {
        fields.Children.Add(Ui.Label(label, new Thickness(0, 14, 0, 6)));
        fields.Children.Add(control);
        if (hint != null) { var h = Ui.Text(hint, "Muted", 11.5); h.Margin = new Thickness(0, 4, 0, 0); fields.Children.Add(h); }
        return control;
    }

    public void AddRaw(UIElement e) => fields.Children.Add(e);

    public bool Ask(Window? owner, Func<string?> validation)
    {
        validate = validation;
        if (owner is { IsVisible: true }) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return ShowDialog() == true;
    }

    /// <summary>Seven weekday chips (Monday first) bound to a bit mask.</summary>
    public static WrapPanel WeekdayPicker(int mask, Action<int> changed)
    {
        var panel = new WrapPanel();
        int current = mask;
        for (int i = 0; i < 7; i++)
        {
            int bit = 1 << i;
            var chip = new DayChip(L.Culture.DateTimeFormat.AbbreviatedDayNames[(i + 1) % 7], bit) { IsOn = (mask & bit) != 0 };
            chip.Changed += () => { current = chip.IsOn ? current | bit : current & ~bit; changed(current); };
            panel.Children.Add(chip);
        }
        return panel;
    }
}
