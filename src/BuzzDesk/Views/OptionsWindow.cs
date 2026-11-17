using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace BuzzDesk;

/// <summary>View → Options: global shortcuts (recorded by pressing them), start with Windows, local backups,
/// default currency, site icons, and links to time zone, language, cloud backup and the data folder.</summary>
public static class OptionsUi
{
    public static void Show(Window owner)
    {
        var st = Store.Settings;
        var form = new FormWindow(L.T("Opciones"));

        form.AddRaw(Ui.Label(L.T("Atajos globales"), new Thickness(0, 16, 0, 0)));
        var capture = form.Add(L.T("Captura rápida"), HotkeyBox(st.HotkeyCapture), L.T("Hacé clic y apretá la combinación (por ejemplo Ctrl+Shift+Espacio). Evitá Ctrl+Alt+Espacio: la usa Claude."));
        var launcher = form.Add(L.T("Lanzador de enlaces"), HotkeyBox(st.HotkeyLauncher));

        form.AddRaw(Ui.Label(L.T("Inicio"), new Thickness(0, 20, 0, 0)));
        var startup = new CheckBox { Content = L.T("Iniciar con Windows (para que los recordatorios suenen siempre)"), IsChecked = App.StartupEnabled };
        form.AddRaw(startup);

        var test = Ui.ToolButton("\uEA8F", L.T("Probar una notificación"), (_, _) =>
        {
            bool ok = Toasts.ShowNotice(L.T("Así se ven los avisos de BuzzDesk"), L.T("Si no aparece, revisá Configuración → Sistema → Notificaciones → BuzzDesk, y el modo No molestar."), "today");
            if (!ok) App.Current.Toast(L.T("Así se ven los avisos de BuzzDesk"), L.T("Las notificaciones de Windows no están disponibles: BuzzDesk usa los globos de la bandeja."));
            Log.Info("test notification: " + (ok ? "toast" : "balloon"));
        }, L.T("Muestra un aviso de prueba ahora"));
        test.Margin = new Thickness(0, 10, 0, 0); test.HorizontalAlignment = HorizontalAlignment.Left;
        form.AddRaw(test);

        form.AddRaw(Ui.Label(L.T("Copias de seguridad locales"), new Thickness(0, 20, 0, 0)));
        var keepRow = new StackPanel { Orientation = Orientation.Horizontal };
        var daily = new TextBox { Width = 60, Text = st.BackupKeepDaily.ToString() };
        var weekly = new TextBox { Width = 60, Text = st.BackupKeepWeekly.ToString(), Margin = new Thickness(8, 0, 0, 0) };
        keepRow.Children.Add(Ui.Text(L.T("Diarias"), "Muted")); keepRow.Children.Add(new Border { Width = 8 }); keepRow.Children.Add(daily);
        keepRow.Children.Add(new Border { Width = 18 });
        keepRow.Children.Add(Ui.Text(L.T("Semanales"), "Muted")); keepRow.Children.Add(weekly);
        form.AddRaw(keepRow);
        var cloud = Ui.ToolButton("", L.T("Copia en la nube…") + "  ·  " + Cloud.Status(), (_, _) => CloudUi.Configure(owner));
        cloud.Margin = new Thickness(0, 10, 0, 0); cloud.HorizontalAlignment = HorizontalAlignment.Left;
        form.AddRaw(cloud);

        form.AddRaw(Ui.Label(L.T("Pagos y enlaces"), new Thickness(0, 20, 0, 0)));
        var currency = new ComboBox { Width = 100, HorizontalAlignment = HorizontalAlignment.Left, IsEditable = true, Text = Fmt.DefaultCurrency };
        foreach (var c in Fmt.Currencies) currency.Items.Add(c);
        form.Add(L.T("Moneda por defecto"), currency);
        var favicons = new CheckBox { Content = L.T("Pedir el título y el ícono a cada sitio que guardes"), IsChecked = st.FetchFavicons, Margin = new Thickness(0, 10, 0, 0) };
        form.AddRaw(favicons);

        var more = Ui.Text(L.T("Idioma y zona horaria: menú Vista. Apariencia y Boberto: Personalizar."), "Muted", 11.5);
        more.Margin = new Thickness(0, 18, 0, 0);
        form.AddRaw(more);

        if (!form.Ask(owner, () =>
        {
            if ((string)capture.Tag == "" || (string)launcher.Tag == "") return L.T("Cada atajo necesita una combinación.");
            if ((string)capture.Tag == (string)launcher.Tag) return L.T("Los dos atajos no pueden ser iguales.");
            if (!int.TryParse(daily.Text, out int d) || d is < 1 or > 365 || !int.TryParse(weekly.Text, out int w) || w is < 0 or > 520) return L.T("Copias: diarias de 1 a 365, semanales de 0 a 520.");
            string cur = currency.Text.Trim().ToUpperInvariant();
            if (cur.Length != 3 || !cur.All(char.IsAsciiLetterUpper)) return L.T("La moneda va en código de 3 letras: ARS, USD, EUR…");
            return null;
        })) return;
        st.HotkeyCapture = (string)capture.Tag;
        st.HotkeyLauncher = (string)launcher.Tag;
        st.BackupKeepDaily = int.Parse(daily.Text);
        st.BackupKeepWeekly = int.Parse(weekly.Text);
        st.DefaultCurrency = currency.Text.Trim().ToUpperInvariant();
        st.FetchFavicons = favicons.IsChecked == true;
        Store.SaveSettings();
        if (startup.IsChecked != App.StartupEnabled) App.StartupEnabled = startup.IsChecked == true;
        App.Current.ReRegisterHotkeys();
        (owner as MainWindow)?.SyncStartupMenu();
    }

    /// <summary>A box that records a key combination ("Ctrl+Alt+Space", in HotkeyWindow.Parse's names).</summary>
    static TextBox HotkeyBox(string current)
    {
        var box = new TextBox { Text = Pretty(current), Tag = current, IsReadOnly = true, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
        box.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            e.Handled = true;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (key is Key.Tab) { box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
            if (key is Key.Escape) { box.Text = Pretty((string)box.Tag); return; }
            var mods = Keyboard.Modifiers;
            bool function = key is >= Key.F1 and <= Key.F24;
            if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0 && !function) { box.Text = L.T("Usá Ctrl, Alt o Win"); return; }
            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(((WinForms.Keys)KeyInterop.VirtualKeyFromKey(key)).ToString());
            string spec = string.Join("+", parts);
            if (!HotkeyWindow.Parse(spec, out uint _, out WinForms.Keys _)) { box.Text = L.T("Esa tecla no sirve para un atajo"); return; }
            box.Tag = spec;
            box.Text = Pretty(spec);
        };
        return box;
    }

    static string Pretty(string spec) => spec.Replace("+", " + ").Replace("Space", L.Es ? "Espacio" : "Space");
}
