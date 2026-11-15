using System.Windows;
using System.Windows.Controls;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>Loose notes from quick capture, shown in Today until they are filed in a topic or deleted.</summary>
public static class InboxUi
{
    public static FrameworkElement? TodayBlock(DateOnly today)
    {
        var notes = Topics.Inbox();
        if (notes.Count == 0) return null;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Label(L.T("Notas sueltas") + "  ·  " + notes.Count, new Thickness(4, 14, 0, 6)));
        foreach (var n in notes.Take(10))
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var g = Ui.Glyph("", 12); g.HorizontalAlignment = HorizontalAlignment.Center; g.VerticalAlignment = VerticalAlignment.Top; g.Margin = new Thickness(0, 10, 0, 0);
            grid.Children.Add(g);
            var text = new StackPanel { Margin = new Thickness(0, 7, 8, 7) };
            text.Children.Add(Ui.Text(n.Body, "Ink"));
            text.Children.Add(Ui.Text(Fmt.Ago(n.CreatedUtc), "Muted", 11, mono: true));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) };
            var note = n;
            buttons.Children.Add(Ui.IconButton("", (s, _) => FileMenu(note, (FrameworkElement)s!), L.T("Archivar en un tema")));
            buttons.Children.Add(Ui.IconButton("", (_, _) =>
            {
                Topics.DeleteNote(note.Id);
                App.Current.Desk?.SetStatus(L.T("Nota eliminada"), L.T("Deshacer"), () => Topics.RestoreNote(note.Id));
            }, L.T("Eliminar nota")));
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            var frame = new Border { Child = grid, BorderThickness = new Thickness(1) };
            frame.SetResourceReference(Border.BackgroundProperty, "Surface");
            frame.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
            frame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
            stack.Children.Add(frame);
        }
        return stack;
    }

    static void FileMenu(Note n, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var create = new MenuItem { Header = L.T("Nuevo tema con esta nota…") };
        create.Click += (_, _) =>
        {
            string first = n.Body.Split('\n')[0].Trim();
            if (PromptWindow.Ask(App.Current.Desk, L.T("Nuevo tema"), L.T("Nombre:"), first.Length > 80 ? first[..80] : first) is { } name && name.Trim().Length > 0)
            {
                long topic = Topics.Add(name);
                Topics.FileNote(n.Id, topic);
                App.Current.Desk?.OpenTopic(topic);
            }
        };
        menu.Items.Add(create);
        var topics = Topics.List(TopicScope.Open, Clock.Today);
        if (topics.Count > 0) menu.Items.Add(new Separator());
        foreach (var t in topics.Take(25))
        {
            var m = new MenuItem { Header = t.Title };
            long id = t.Id;
            m.Click += (_, _) => { Topics.FileNote(n.Id, id); App.Current.Desk?.SetStatus(L.F("Nota archivada en {0}", t.Title)); };
            menu.Items.Add(m);
        }
        menu.IsOpen = true;
    }
}
