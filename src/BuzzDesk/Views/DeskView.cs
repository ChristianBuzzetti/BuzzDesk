using System.Windows;
using System.Windows.Controls;

namespace BuzzDesk;

/// <summary>A page of the main window (Today, Tasks, Topics, Links, Search). Each fills the subbar with its own
/// actions and reloads from the database when data changes.</summary>
public abstract class DeskView : Grid
{
    public abstract string Key { get; }
    /// <summary>Adds this view's actions to the main window's subbar.</summary>
    public virtual void BuildToolbar(Panel bar) { }
    /// <summary>Reloads from the database.</summary>
    public virtual void Refresh() { }
    /// <summary>"New" (Ctrl+N) in this view.</summary>
    public virtual void New() { }
    /// <summary>Called when the view is shown again.</summary>
    public virtual void Shown() => Refresh();
    /// <summary>Releases WebViews and timers when the window closes.</summary>
    public virtual void Release() { }

    protected MainWindow Host => (MainWindow)Window.GetWindow(this);
}
