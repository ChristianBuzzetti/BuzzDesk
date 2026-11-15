using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>Results of the global search, grouped by kind, with the matches highlighted.</summary>
public sealed class SearchView : DeskView
{
    public override string Id => "search";
    readonly TextBlock heading = Ui.Title("");
    readonly TextBlock subheading = Ui.Text("", "Muted", 12, mono: true);
    readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock emptyText = Ui.Text("", "Muted");
    string query = "";
    List<SearchHit> hits = [];

    public SearchView()
    {
        var root = new DockPanel { Margin = new Thickness(8, 4, 8, 0) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(heading);
        subheading.Margin = new Thickness(0, 4, 0, 0);
        head.Children.Add(subheading);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        emptyText.Margin = new Thickness(4, 16, 0, 0);
        DockPanel.SetDock(emptyText, Dock.Top);
        root.Children.Add(emptyText);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter && list.SelectedItem is ListBoxItem { Tag: SearchHit h }) { Open(h); e.Handled = true; } };
        list.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Left && list.SelectedItem is ListBoxItem { Tag: SearchHit h }) Open(h); };
        root.Children.Add(list);
        Children.Add(root);
    }

    public void SetQuery(string text) { query = text.Trim(); Refresh(); }

    public override void Refresh()
    {
        heading.Text = L.F("Resultados para «{0}»", query);
        hits = query.Length == 0 ? [] : Search.Run(query);
        subheading.Text = L.Count(hits.Count, "resultado", "resultados");
        list.Items.Clear();
        foreach (var (kind, label) in new[] { ("task", L.T("Pendientes")), ("topic", L.T("Temas")), ("note", L.T("Notas")), ("link", L.T("Enlaces")) })
        {
            var group = hits.Where(h => h.Kind == kind).ToList();
            if (group.Count == 0) continue;
            list.Items.Add(TaskRows.Header(label, group.Count));
            foreach (var h in group) list.Items.Add(new ListBoxItem { Content = Row(h), Tag = h, Padding = new Thickness(0) });
        }
        emptyText.Text = hits.Count > 0 ? "" : L.T("Nada encontrado. Probá con menos palabras o el comienzo de una palabra; las tildes no importan.");
        emptyText.Visibility = hits.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    static FrameworkElement Row(SearchHit h)
    {
        var grid = new Grid { MinHeight = 44, Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var glyph = Ui.Glyph(h.Kind switch { "task" => "", "topic" => "", "note" => "", _ => "" }, 13);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(glyph);
        var text = new StackPanel { Margin = new Thickness(0, 6, 8, 6), VerticalAlignment = VerticalAlignment.Center };
        string title = h.Title.Length > 0 ? h.Title : h.Kind == "note" ? L.T("Nota suelta") : "";
        string plain = h.Snippet.Replace(Search.MarkStart.ToString(), "").Replace(Search.MarkEnd.ToString(), "");
        if (plain == title)
        {
            // The match is in the title itself: show the title once, highlighted.
            var t = Highlighted(h.Snippet);
            t.FontSize = 13.5; t.FontWeight = FontWeights.SemiBold; t.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            text.Children.Add(t);
        }
        else
        {
            if (title.Length > 0)
            {
                var t = Ui.Text(title, "Ink", weight: FontWeights.SemiBold);
                t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(t);
            }
            text.Children.Add(Highlighted(h.Snippet));
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>The snippet with the matched words in the accent color.</summary>
    public static TextBlock Highlighted(string snippet)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), FontWeight = FontWeights.Normal };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var parts = snippet.Replace('\n', ' ').Split(Search.MarkStart);
        tb.Inlines.Add(new Run(parts[0]));
        foreach (var part in parts.Skip(1))
        {
            int end = part.IndexOf(Search.MarkEnd);
            string hit = end < 0 ? part : part[..end], rest = end < 0 ? "" : part[(end + 1)..];
            var run = new Run(hit) { FontWeight = FontWeights.SemiBold };
            run.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            tb.Inlines.Add(run);
            tb.Inlines.Add(new Run(rest));
        }
        return tb;
    }

    /// <summary>Enter in the search box: the best hit.</summary>
    public void OpenFirst() { if (hits.Count > 0) Open(hits[0]); }
    public void FocusList() { list.Focus(); if (list.SelectedIndex < 0) list.SelectedIndex = list.Items.OfType<ListBoxItem>().ToList().FindIndex(i => i.Tag is SearchHit); }

    void Open(SearchHit h)
    {
        var main = Host;
        switch (h.Kind)
        {
            case "task": main.OpenTask(h.RefId); break;
            case "topic": main.OpenTopic(h.RefId); break;
            case "note": if (h.TopicId is long topic) main.OpenTopic(topic); else main.Navigate("today"); break;
            case "link": main.OpenLink(h.RefId); break;
        }
    }
}
