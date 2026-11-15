using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>Link helpers shared by Links, the launcher, Today and topics: icon, open, save with icon fetch.</summary>
public static class LinkUi
{
    static readonly Dictionary<long, ImageSource?> icons = [];

    /// <summary>The site icon at about 18 px, or a globe / document / mail glyph.</summary>
    public static FrameworkElement Icon(Link l, double size = 18)
    {
        if (l.Favicon != null && Decode(l) is ImageSource img)
            return new Image { Source = img, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        string glyph = l.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? "" : l.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? "" : "";
        var g = Ui.Glyph(glyph, size * 0.8, "Muted");
        g.Width = size; g.TextAlignment = TextAlignment.Center;
        return g;
    }

    static ImageSource? Decode(Link l)
    {
        long key = l.Id ^ (l.FaviconUtc?.Ticks ?? 0);
        if (icons.TryGetValue(key, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(l.Favicon!), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            // ICO files hold several sizes: the smallest one of at least 32 px, else the largest.
            var frame = decoder.Frames.Where(f => f.PixelWidth >= 32).OrderBy(f => f.PixelWidth).FirstOrDefault() ?? decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            result = frame;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or OverflowException) { }
        icons[key] = result;
        return result;
    }

    public static void Open(Link l)
    {
        try
        {
            Process.Start(new ProcessStartInfo(l.Url) { UseShellExecute = true });
            Links.Opened(l.Id);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            PromptWindow.Info(App.Current.Desk, L.T("No se pudo abrir el enlace"), ex.Message + "\n\n" + "URL: " + l.Url);
        }
    }

    /// <summary>Saves a link (or finds the one with the same URL) and fetches its title and icon in the background.</summary>
    public static long Save(string url, string? title = null, IReadOnlyList<string>? tags = null, bool pinned = false, string? description = null)
    {
        var (id, existed) = Links.Add(url, title ?? "", description, tags, pinned);
        if (existed) App.Current.Desk?.SetStatus(L.T("Ese enlace ya estaba guardado."));
        else FetchInBackground(id, string.IsNullOrWhiteSpace(title));
        return id;
    }

    public static void FetchInBackground(long id, bool alsoTitle)
    {
        if (!Store.Settings.FetchFavicons) return;
        var link = Links.Get(id);
        if (link == null || !link.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        var dispatcher = Application.Current.Dispatcher;
        _ = Task.Run(async () =>
        {
            var r = await Favicons.FetchAsync(link.Url);
            await dispatcher.BeginInvoke(() =>
            {
                var now = Links.Get(id);
                if (now == null) return;
                if (alsoTitle && r.Title is { Length: > 0 } t && now.Title == now.Url) Links.Update(now with { Title = t.Length > 200 ? t[..200] : t });
                Links.SetFavicon(id, r.Icon);
            });
        });
    }

    /// <summary>A link as a row: icon, title, host and tags, pin and open buttons.</summary>
    public static FrameworkElement Row(Link l, Action<Link>? edit, Action<string>? tagClick = null, Action? detach = null)
    {
        var grid = new Grid { MinHeight = 42, Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = l };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icon(l);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 8, 5) };
        var title = Ui.Text(l.Title, "Ink");
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        var meta = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var host = Ui.Text(l.Host, "Muted", 11.5, mono: true);
        host.Margin = new Thickness(0, 0, 10, 0); host.TextWrapping = TextWrapping.NoWrap; host.TextTrimming = TextTrimming.CharacterEllipsis; host.MaxWidth = 340;
        host.ToolTip = l.Url;
        meta.Children.Add(host);
        foreach (var tag in l.Tags)
        {
            var chip = Ui.Chip("#" + tag, "AlternateRow", "Muted");
            if (tagClick != null) { string t = tag; chip.Cursor = Cursors.Hand; chip.MouseLeftButtonUp += (_, e) => { tagClick(t); e.Handled = true; }; }
            meta.Children.Add(chip);
        }
        text.Children.Add(meta);
        if (!string.IsNullOrWhiteSpace(l.Description)) grid.ToolTip = l.Description;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
        var pin = Ui.IconButton(l.Pinned ? "" : "", (_, _) => Links.SetPinned(l.Id, !l.Pinned), l.Pinned ? L.T("Soltar (sale de Hoy)") : L.T("Fijar (aparece en Hoy)"));
        if (l.Pinned) pin.SetResourceReference(Control.ForegroundProperty, "Accent");
        buttons.Children.Add(pin);
        if (edit != null) buttons.Children.Add(Ui.IconButton("", (_, _) => edit(l), L.T("Editar")));
        if (detach != null) buttons.Children.Add(Ui.IconButton("", (_, _) => detach(), L.T("Quitar de este tema")));
        buttons.Children.Add(Ui.IconButton("", (_, _) => Open(l), L.T("Abrir")));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        grid.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Button && FindButton(e.OriginalSource as DependencyObject) == null) Open(l); };
        var menu = new ContextMenu();
        void Add(string h, Action a) { var m = new MenuItem { Header = h }; m.Click += (_, _) => a(); menu.Items.Add(m); }
        Add(L.T("Abrir"), () => Open(l));
        Add(L.T("Copiar dirección"), () => { try { Clipboard.SetText(l.Url); } catch (System.Runtime.InteropServices.COMException) { } });
        if (edit != null) Add(L.T("Editar…"), () => edit(l));
        Add(l.Pinned ? L.T("Soltar") : L.T("Fijar"), () => Links.SetPinned(l.Id, !l.Pinned));
        menu.Items.Add(new Separator());
        Add(L.T("Eliminar"), () => Delete(l));
        grid.ContextMenu = menu;
        return grid;
    }

    static Button? FindButton(DependencyObject? d)
    {
        while (d != null && d is not Button) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as Button;
    }

    public static void Delete(Link l)
    {
        Links.Delete(l.Id);
        App.Current.Desk?.SetStatus(L.F("Enlace eliminado: {0}", l.Title), L.T("Deshacer"), () =>
        {
            if (!Links.Restore(l.Id)) App.Current.Desk?.SetStatus(L.T("No se pudo restaurar: esa dirección se volvió a guardar."));
        });
    }

    /// <summary>Today's block: pinned links as compact tiles.</summary>
    public static FrameworkElement? TodayBlock(DateOnly today)
    {
        var pinned = Links.List(pinnedOnly: true, limit: 24);
        if (pinned.Count == 0) return null;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Label(L.T("Enlaces fijados"), new Thickness(4, 14, 0, 6)));
        var wrap = new WrapPanel();
        foreach (var l in pinned)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(l, 16); icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(icon);
            var t = Ui.Text(l.Title, "Ink"); t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 180;
            row.Children.Add(t);
            var tile = new Button { Content = row, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 0, 12, 0), Height = 36, ToolTip = l.Url };
            tile.Click += (_, _) => Open(l);
            wrap.Children.Add(tile);
        }
        stack.Children.Add(wrap);
        return stack;
    }
}
