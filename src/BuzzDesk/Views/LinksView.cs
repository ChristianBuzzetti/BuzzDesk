using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>Useful links: search as you type, filter by tag, pinned first; paste a URL to save it.</summary>
public sealed class LinksView : DeskView
{
    public override string Id => "links";
    readonly TextBlock subheading = Ui.Text("", "Muted", 12, mono: true);
    readonly TextBox filter = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly WrapPanel tagBar = new() { Margin = new Thickness(0, 8, 0, 4) };
    readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock emptyText = Ui.Text("", "Muted");
    string? tag;

    public LinksView()
    {
        var root = new DockPanel { Margin = new Thickness(8, 4, 8, 0) };
        var head = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(Ui.Title(L.T("Enlaces")));
        subheading.Margin = new Thickness(12, 6, 0, 0);
        titleRow.Children.Add(subheading);
        head.Children.Add(titleRow);
        filter.Margin = new Thickness(0, 10, 0, 0);
        filter.TextChanged += (_, _) => Refresh();
        filter.KeyDown += FilterKeyDown;
        filter.ToolTip = L.T("Filtra por título, dirección, descripción o etiqueta. Pegá una dirección y Enter para guardarla.");
        head.Children.Add(TaskEditor.Placeholder(filter, L.T("Buscar o pegar una dirección…  (Enter abre el primero)")));
        head.Children.Add(tagBar);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        emptyText.Margin = new Thickness(4, 24, 0, 0);
        DockPanel.SetDock(emptyText, Dock.Top);
        root.Children.Add(emptyText);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.KeyDown += (_, e) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: Link l }) return;
            if (e.Key == Key.Enter) { LinkUi.Open(l); e.Handled = true; }
            else if (e.Key == Key.Delete) { LinkUi.Delete(l); e.Handled = true; }
            else if (e.Key == Key.F2) { Edit(l); e.Handled = true; }
        };
        root.Children.Add(list);
        Children.Add(root);
    }

    public override void BuildToolbar(Panel bar)
    {
        bar.Children.Add(Ui.ToolButton("", L.T("Nuevo enlace"), (_, _) => New(), "Ctrl+N"));
        bar.Children.Add(Ui.ToolButton("", L.T("Lanzador"), (_, _) => LauncherWindow.Toggle(), Store.Settings.HotkeyLauncher + " · Ctrl+L"));
        var favicons = new CheckBox { Content = L.T("Íconos de los sitios"), IsChecked = Store.Settings.FetchFavicons, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = L.T("Al guardar un enlace, pedirle al propio sitio su título e ícono. Apagado, BuzzDesk no se conecta a ningún lado.") };
        favicons.Click += (_, _) => { Store.Settings.FetchFavicons = favicons.IsChecked == true; Store.SaveSettings(); };
        bar.Children.Add(favicons);
    }

    public override void New() => Edit(null);

    void Edit(Link? l)
    {
        if (LinkWindow.Edit(Window.GetWindow(this), l) is long id) { Refresh(); Select(id); }
    }

    public void Select(long id)
    {
        var row = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is Link l && l.Id == id);
        if (row == null && (filter.Text.Length > 0 || tag != null)) { filter.Text = ""; tag = null; Refresh(); row = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is Link l && l.Id == id); }
        if (row != null) { list.SelectedItem = row; row.BringIntoView(); }
    }

    public override void Refresh()
    {
        long? tagId = tag == null ? null : Tags.All().FirstOrDefault(t => t.Name.Equals(tag, StringComparison.OrdinalIgnoreCase))?.Id;
        if (tag != null && tagId == null) tag = null;
        var items = Links.List(filter.Text, tagId);
        var all = filter.Text.Length == 0 && tagId == null ? items : Links.List();
        subheading.Text = L.Count(all.Count, "enlace", "enlaces");
        long? keep = (list.SelectedItem as ListBoxItem)?.Tag is Link sel ? sel.Id : null;
        list.Items.Clear();
        bool pinnedHeader = false, restHeader = false;
        foreach (var l in items)
        {
            if (l.Pinned && !pinnedHeader) { list.Items.Add(TaskRows.Header(L.T("Fijados"), items.Count(x => x.Pinned), "Accent")); pinnedHeader = true; }
            if (!l.Pinned && pinnedHeader && !restHeader) { list.Items.Add(TaskRows.Header(L.T("Otros"), items.Count(x => !x.Pinned))); restHeader = true; }
            list.Items.Add(new ListBoxItem { Content = LinkUi.Row(l, Edit, t => { tag = t; Refresh(); }), Tag = l, Padding = new Thickness(0) });
        }
        if (keep is long id) Select(id);
        BuildTags(all);
        emptyText.Text = items.Count > 0 ? "" : all.Count == 0
            ? L.T("Guardá acá tus accesos de siempre: consolas, dashboards, documentación. Pegá una dirección arriba y Enter.")
            : Links.Normalize(filter.Text) != null && filter.Text.Contains('.') ? L.T("Enter para guardar esta dirección.") : L.T("Nada coincide.");
        emptyText.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void BuildTags(List<Link> all)
    {
        tagBar.Children.Clear();
        var counts = all.SelectMany(l => l.Tags).GroupBy(t => t, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(20).ToList();
        if (counts.Count == 0) return;
        void Chip(string label, string? value)
        {
            var chip = new RadioButton { Content = label, GroupName = "LinkTags", IsChecked = string.Equals(tag, value, StringComparison.OrdinalIgnoreCase) || tag == null && value == null, Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6) };
            chip.SetResourceReference(StyleProperty, "SegmentChip");
            chip.Checked += (_, _) => { if (tag != value) { tag = value; Refresh(); } };
            tagBar.Children.Add(chip);
        }
        Chip(L.T("Todas"), null);
        foreach (var g in counts) Chip("#" + g.Key + "  " + g.Count(), g.Key);
    }

    void FilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { filter.Text = ""; e.Handled = true; return; }
        if (e.Key == Key.Down) { list.Focus(); if (list.SelectedIndex < 0) list.SelectedIndex = list.Items.OfType<ListBoxItem>().ToList().FindIndex(i => i.Tag is Link); e.Handled = true; return; }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var first = list.Items.OfType<ListBoxItem>().Select(i => i.Tag).OfType<Link>().FirstOrDefault();
        if (first != null && !(Links.Normalize(filter.Text) is string u && filter.Text.Contains('.') && first.Url != u && Links.FindByUrl(u) == null)) { LinkUi.Open(first); return; }
        if (Links.Normalize(filter.Text) is string url && filter.Text.Contains('.'))
        {
            long id = LinkUi.Save(url);
            filter.Text = "";
            Refresh();
            Select(id);
        }
    }
}
