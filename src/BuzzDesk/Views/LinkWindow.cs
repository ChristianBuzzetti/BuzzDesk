using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>Add or edit a link: URL, title (fetched from the page if left empty), description, tags, pinned.</summary>
public sealed class LinkWindow : Window
{
    readonly TextBox url = new(), title = new(), description = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 }, tags = new();
    readonly CheckBox pinned = new() { Content = L.T("Fijado (aparece en Hoy)") };
    readonly TextBlock error = Ui.Text("", "ErrorInk", 12);
    readonly Link? existing;
    public long? SavedId { get; private set; }

    LinkWindow(Link? link, string? presetUrl)
    {
        existing = link;
        SetResourceReference(StyleProperty, typeof(Window));
        Title = (link == null ? L.T("Nuevo enlace") : L.T("Editar enlace")) + " · BuzzDesk";
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        root.Children.Add(Ui.Title(link == null ? L.T("Nuevo enlace") : L.T("Editar enlace")));
        void Field(string label, UIElement control) { root.Children.Add(Ui.Label(label, new Thickness(0, 14, 0, 6))); root.Children.Add(control); }
        Field(L.T("Dirección"), url);
        Field(L.T("Título"), TaskEditor.Placeholder(title, L.T("Vacío: se toma de la página")));
        Field(L.T("Descripción"), description);
        Field(L.T("Etiquetas"), tags);
        pinned.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(pinned);
        error.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(error);
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        foot.Children.Add(new Button { Content = L.T("Cancelar"), IsCancel = true, MinWidth = 110 });
        var ok = new Button { Content = L.T("Guardar"), IsDefault = true, MinWidth = 110, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
        ok.SetResourceReference(BackgroundProperty, "Accent"); ok.SetResourceReference(ForegroundProperty, "OnAccent"); ok.SetResourceReference(BorderBrushProperty, "Accent");
        ok.Click += (_, _) => Save();
        foot.Children.Add(ok);
        root.Children.Add(foot);
        Content = root;

        url.Text = link?.Url ?? presetUrl ?? "";
        title.Text = link?.Title ?? "";
        description.Text = link?.Description ?? "";
        tags.Text = string.Join(", ", link?.Tags ?? []);
        pinned.IsChecked = link?.Pinned ?? false;
        tags.ToolTip = L.T("Separadas por coma o espacio: dba, prod, reunión");
        Loaded += (_, _) => { (url.Text.Length == 0 ? url : title).Focus(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    void Save()
    {
        string? normalized = Links.Normalize(url.Text);
        if (normalized == null) { error.Text = L.T("Esa dirección no parece válida (http, https, archivo o mailto)."); url.Focus(); return; }
        var tagList = tags.Text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (existing == null)
        {
            SavedId = LinkUi.Save(normalized, title.Text.Trim().Length > 0 ? title.Text.Trim() : null, tagList, pinned.IsChecked == true, description.Text);
        }
        else
        {
            if (normalized != existing.Url && Links.FindByUrl(normalized) is { } other && other.Id != existing.Id) { error.Text = L.F("Esa dirección ya está guardada como \"{0}\".", other.Title); return; }
            try { Links.Update(existing with { Url = normalized, Title = title.Text.Trim().Length > 0 ? title.Text.Trim() : normalized, Description = description.Text, Pinned = pinned.IsChecked == true }, tagList); }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19) { error.Text = L.T("Esa dirección ya está guardada."); return; }
            if (normalized != existing.Url) LinkUi.FetchInBackground(existing.Id, false);
            SavedId = existing.Id;
        }
        DialogResult = true;
    }

    public static long? Edit(Window? owner, Link? link, string? presetUrl = null)
    {
        var w = new LinkWindow(link, presetUrl);
        if (owner is { IsVisible: true }) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w.SavedId : null;
    }
}
