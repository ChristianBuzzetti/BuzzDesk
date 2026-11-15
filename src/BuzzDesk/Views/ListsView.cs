using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>Lists to tick off (groceries, packing): pick a list on the left, tick items on the right; "Use again"
/// unticks everything.</summary>
public sealed class ListsView : DeskView
{
    public override string Id => "lists";
    readonly ListBox lists = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TextBox newList = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly TextBlock title = Ui.Title("");
    readonly TextBlock progress = Ui.Text("", "Muted", 12, mono: true);
    readonly TextBox newItem = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly StackPanel items = new();
    readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    readonly Border right;
    readonly TextBlock empty = Ui.Text("", "Muted");
    long? current;

    public ListsView()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel { Margin = new Thickness(8, 4, 16, 0) };
        var head = new StackPanel();
        head.Children.Add(Ui.Title(L.T("Listas")));
        newList.Margin = new Thickness(0, 12, 0, 8);
        newList.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || newList.Text.Trim().Length == 0) return;
            try { current = Checklists.Add(newList.Text); newList.Text = ""; Refresh(); newItem.Focus(); }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19) { Host.SetStatus(L.T("Ya hay una lista con ese nombre.")); }
            e.Handled = true;
        };
        head.Children.Add(TaskEditor.Placeholder(newList, L.T("Nueva lista…  (Enter)")));
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        lists.SelectionChanged += (_, _) => { if (lists.SelectedItem is ListBoxItem { Tag: Checklist l } && l.Id != current) { current = l.Id; ShowItems(); } };
        left.Children.Add(lists);
        Children.Add(left);

        var body = new DockPanel();
        var top = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(title);
        progress.Margin = new Thickness(12, 6, 0, 0);
        titleRow.Children.Add(progress);
        top.Children.Add(titleRow);
        newItem.Margin = new Thickness(0, 12, 0, 8);
        newItem.ToolTip = L.T("Varias a la vez separadas por coma: leche, pan, café");
        newItem.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || current is not long id || newItem.Text.Trim().Length == 0) return;
            Checklists.AddItems(id, Split(newItem.Text));
            newItem.Text = "";
            e.Handled = true;
        };
        top.Children.Add(TaskEditor.Placeholder(newItem, L.T("Agregar…  (varias separadas por coma)")));
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);
        DockPanel.SetDock(actions, Dock.Bottom);
        actions.Children.Add(Ui.ToolButton("", L.T("Usar de nuevo"), (_, _) => { if (current is long id) Checklists.Reset(id); }, L.T("Destilda todo, para volver a usar la lista")));
        actions.Children.Add(Ui.ToolButton("", L.T("Quitar tildados"), (_, _) => { if (current is long id) Checklists.ClearDone(id); }));
        actions.Children.Add(Ui.ToolButton("", L.T("Renombrar"), (_, _) => Rename()));
        actions.Children.Add(Ui.ToolButton("", L.T("Eliminar lista"), (_, _) => Delete()));
        body.Children.Add(actions);
        body.Children.Add(Ui.Scroll(items));
        empty.Margin = new Thickness(0, 20, 0, 0);
        empty.TextAlignment = TextAlignment.Center;
        right = Ui.Panel(new Grid { Children = { body, empty } });
        SetColumn(right, 1);
        Children.Add(right);
    }

    public static IEnumerable<string> Split(string text) => text.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override void BuildToolbar(Panel bar) => bar.Children.Add(Ui.ToolButton("", L.T("Nueva lista"), (_, _) => New(), "Ctrl+N"));
    public override void New() => newList.Focus();

    public void Select(long id) { current = id; Refresh(); }

    public override void Refresh()
    {
        var all = Checklists.List();
        if (current is long c && all.All(l => l.Id != c)) current = null;
        current ??= all.FirstOrDefault()?.Id;
        lists.Items.Clear();
        foreach (var l in all)
        {
            var row = new DockPanel { Margin = new Thickness(6, 4, 6, 4) };
            var count = Ui.Text(l.Items == 0 ? "" : (l.Items - l.Done) + "/" + l.Items, l.Done == l.Items && l.Items > 0 ? "Ok" : "Muted", 11.5, mono: true);
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            var g = Ui.Glyph("", 13); g.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(g);
            row.Children.Add(Ui.Text(l.Name, "Ink"));
            var item = new ListBoxItem { Content = row, Tag = l };
            lists.Items.Add(item);
            if (l.Id == current) lists.SelectedItem = item;
        }
        ShowItems();
    }

    void ShowItems()
    {
        items.Children.Clear();
        var list = current is long id ? Checklists.Get(id) : null;
        empty.Text = list == null ? L.T("Creá una lista a la izquierda: súper, valija, regalos…") : "";
        empty.Visibility = list == null ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement e in ((DockPanel)((Grid)right.Child).Children[0]).Children) e.Visibility = list == null ? Visibility.Collapsed : Visibility.Visible;
        if (list == null) return;
        title.Text = list.Name;
        progress.Text = list.Items == 0 ? "" : L.F("{0} de {1} listos", list.Done, list.Items);
        foreach (var it in Checklists.Items(list.Id))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = System.Windows.Media.Brushes.Transparent, MinHeight = 32 };
            var check = new CheckBox { IsChecked = it.Done, Margin = new Thickness(0, 0, 10, 0) };
            check.SetResourceReference(StyleProperty, "TaskCheck");
            var item = it;
            check.Click += (_, _) => Checklists.Toggle(item.Id);
            DockPanel.SetDock(check, Dock.Left);
            row.Children.Add(check);
            var del = Ui.IconButton("", (_, _) => Checklists.DeleteItem(item.Id), L.T("Quitar"));
            del.Opacity = 0;
            row.MouseEnter += (_, _) => del.Opacity = 1;
            row.MouseLeave += (_, _) => del.Opacity = 0;
            DockPanel.SetDock(del, Dock.Right);
            row.Children.Add(del);
            var text = Ui.Text(it.Text, it.Done ? "Muted" : "Ink");
            if (it.Done) text.TextDecorations = TextDecorations.Strikethrough;
            text.Cursor = Cursors.Hand;
            text.MouseLeftButtonUp += (_, _) => Checklists.Toggle(item.Id);
            row.Children.Add(text);
            items.Children.Add(row);
        }
    }

    void Rename()
    {
        if (current is not long id || Checklists.Get(id) is not { } l) return;
        if (PromptWindow.Ask(Window.GetWindow(this), L.T("Renombrar"), L.T("Nuevo nombre:"), l.Name) is { } name && name.Trim().Length > 0)
        {
            try { Checklists.Rename(id, name); }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19) { Host.SetStatus(L.T("Ya hay una lista con ese nombre.")); }
        }
    }

    void Delete()
    {
        if (current is not long id || Checklists.Get(id) is not { } l) return;
        Checklists.Delete(id);
        current = null;
        Host.SetStatus(L.F("Lista eliminada: {0}", l.Name), L.T("Deshacer"), () => { if (Checklists.Restore(id)) Select(id); });
    }
}
