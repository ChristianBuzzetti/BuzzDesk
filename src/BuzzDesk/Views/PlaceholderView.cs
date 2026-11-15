using System.Windows;
using System.Windows.Controls;

namespace BuzzDesk;

/// <summary>Stand-in for a view that is not built yet.</summary>
public sealed class PlaceholderView : DeskView
{
    readonly string key;
    public override string Key => key;
    public PlaceholderView(string key, string title)
    {
        this.key = key;
        var stack = new StackPanel { Margin = new Thickness(8, 4, 8, 0) };
        stack.Children.Add(Ui.Title(title));
        stack.Children.Add(Ui.Text(L.T("Próximamente."), "Muted"));
        Children.Add(stack);
    }
}
