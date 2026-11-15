using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>
/// Quick link launcher (Ctrl+Alt+L anywhere, Ctrl+L in BuzzDesk): a small box next to the cursor; type part of the
/// name, arrows to choose, Enter to open. Most used and pinned links come first. Optionally picks a link for a
/// topic instead of opening it.
/// </summary>
public sealed class LauncherWindow : Window
{
    readonly TextBox box = new() { FontSize = 15, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0) };
    readonly ListBox list = new() { BorderThickness = new Thickness(0), MaxHeight = 380, Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock hint = Ui.Text("", "Muted", 11.5);
    readonly Action<Link>? pick;
    static LauncherWindow? open;
    bool closing;

    LauncherWindow(Action<Link>? pick, string title)
    {
        this.pick = pick;
        SetResourceReference(StyleProperty, typeof(Window));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        Width = 520; SizeToContent = SizeToContent.Height;
        var frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(10) };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        frame.SetResourceReference(Border.BorderBrushProperty, "AccentRing");
        var root = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 6) };
        var mark = new BobertoMark { Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) };
        mark.SetResourceReference(BobertoMark.TileProperty, "Accent");
        head.Children.Add(mark);
        head.Children.Add(Ui.Label(title, new Thickness(0)));
        root.Children.Add(head);
        var boxFrame = new Border { BorderThickness = new Thickness(1), Child = box };
        boxFrame.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
        boxFrame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
        boxFrame.SetResourceReference(Border.BackgroundProperty, "Surface");
        root.Children.Add(boxFrame);
        list.Margin = new Thickness(0, 6, 0, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        root.Children.Add(list);
        hint.Margin = new Thickness(4, 6, 0, 0);
        hint.Text = pick == null ? L.T("Enter abre · ↑↓ elegir · Esc cerrar · Ctrl+Enter abre y deja la ventana") : L.T("Enter elige · ↑↓ · Esc cerrar");
        root.Children.Add(hint);
        frame.Child = root;
        Content = frame;
        box.TextChanged += (_, _) => Fill();
        box.PreviewKeyDown += Keys;
        list.MouseUp += (_, e) => { if (list.SelectedItem != null) Go(false); };
        Deactivated += (_, _) => SafeClose();
        Loaded += (_, _) => { box.Focus(); Fill(); };
    }

    void Fill()
    {
        list.Items.Clear();
        var found = Links.Launch(box.Text, 9);
        foreach (var l in found)
        {
            var row = new DockPanel { Margin = new Thickness(4, 2, 4, 2) };
            var icon = LinkUi.Icon(l, 18); icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(icon);
            var host = Ui.Text(l.Host, "Muted", 11.5, mono: true); host.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(host, Dock.Right);
            row.Children.Add(host);
            var t = Ui.Text(l.Title, "Ink"); t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(t);
            list.Items.Add(new ListBoxItem { Content = row, Tag = l });
        }
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        if (found.Count == 0)
            list.Items.Add(new ListBoxItem { Content = Ui.Text(Links.Normalize(box.Text) != null && box.Text.Contains('.') ? L.T("Enter guarda esta dirección como enlace nuevo") : L.T("Sin resultados"), "Muted"), IsEnabled = false });
    }

    void Keys(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: SafeClose(); e.Handled = true; break;
            case Key.Down: if (list.SelectedIndex < list.Items.Count - 1) list.SelectedIndex++; e.Handled = true; break;
            case Key.Up: if (list.SelectedIndex > 0) list.SelectedIndex--; e.Handled = true; break;
            case Key.Enter: Go((Keyboard.Modifiers & ModifierKeys.Control) != 0); e.Handled = true; break;
        }
    }

    void Go(bool stay)
    {
        if (list.SelectedItem is ListBoxItem { Tag: Link l })
        {
            if (pick != null) { SafeClose(); pick(l); return; }
            LinkUi.Open(l);
            if (!stay) SafeClose();
            return;
        }
        // Nothing matched: a typed URL becomes a new link.
        if (Links.Normalize(box.Text) is string url && box.Text.Contains('.'))
        {
            SafeClose();
            long? id = LinkWindow.Edit(null, null, url);
            if (id is long saved && pick != null && Links.Get(saved) is { } link) pick(link);
        }
    }

    void SafeClose() { if (closing) return; closing = true; open = null; Close(); }

    /// <summary>Opens (or closes, if open) the launcher next to the mouse.</summary>
    public static void Toggle(Action<Link>? pick = null, string? title = null)
    {
        if (open != null) { open.SafeClose(); return; }
        var w = new LauncherWindow(pick, title ?? L.T("Abrir enlace"));
        open = w;
        PlaceNearCursor(w);
        w.Show();
        w.Activate();
    }

    /// <summary>Puts a small window next to the mouse once it has its size, inside that screen's work area (same
    /// as BuzzPrint's quick menu: device pixels to DIPs with the window's own transform).</summary>
    public static void PlaceNearCursor(Window w)
    {
        var cur = System.Windows.Forms.Cursor.Position;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -10000; w.Top = -10000;   // off screen until measured
        w.Loaded += (_, _) =>
        {
            var src = PresentationSource.FromVisual(w);
            double sx = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1, sy = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1;
            var wa = System.Windows.Forms.Screen.FromPoint(cur).WorkingArea;
            double x = cur.X * sx - w.ActualWidth / 2, y = cur.Y * sy - 30;
            w.Left = Math.Max(wa.Left * sx, Math.Min(x, wa.Right * sx - w.ActualWidth));
            w.Top = Math.Max(wa.Top * sy, Math.Min(y, wa.Bottom * sy - w.ActualHeight));
        };
    }
}
