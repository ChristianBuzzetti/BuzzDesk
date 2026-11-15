using System.IO;
using System.Windows;
using System.Windows.Controls;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>File → Cloud backup: where (a detected OneDrive / Google Drive / Dropbox / iCloud folder or any
/// other), encryption with a passphrase, and "Back up now". Plus the restore flow.</summary>
public static class CloudUi
{
    public static void Configure(Window? owner)
    {
        var st = Store.Settings;
        var form = new FormWindow(L.T("Copia en la nube"));
        var intro = Ui.Text(L.T("BuzzDesk guarda una copia verificada de tus datos en una carpeta que sincroniza tu servicio de nube. Elegí cuál; podés restaurar desde ahí si cambiás de PC o algo se rompe."), "Muted");
        intro.Margin = new Thickness(0, 8, 0, 0);
        form.AddRaw(intro);

        var places = Cloud.Detect();
        var where = form.Add(L.T("Dónde"), new ComboBox { MinWidth = 380 });
        where.Items.Add(new ComboBoxItem { Content = L.T("Apagada (solo copias locales)"), Tag = "" });
        foreach (var p in places) where.Items.Add(new ComboBoxItem { Content = p.Service + "  —  " + Cloud.Target(p.Root), Tag = Cloud.Target(p.Root) });
        if (st.CloudFolder.Length > 0 && places.All(p => !Cloud.Target(p.Root).Equals(st.CloudFolder, StringComparison.OrdinalIgnoreCase)))
            where.Items.Add(new ComboBoxItem { Content = st.CloudFolder, Tag = st.CloudFolder });
        var other = new ComboBoxItem { Content = L.T("Otra carpeta…"), Tag = "?" };
        where.Items.Add(other);
        where.SelectedItem = where.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, st.CloudFolder, StringComparison.OrdinalIgnoreCase)) ?? where.Items[places.Count > 0 && st.CloudFolder.Length == 0 ? 1 : 0];
        where.SelectionChanged += (_, _) =>
        {
            if (where.SelectedItem != other) return;
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = L.T("Elegí la carpeta para las copias"), UseDescriptionForTitle = true };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var item = new ComboBoxItem { Content = dlg.SelectedPath, Tag = dlg.SelectedPath };
                where.Items.Insert(where.Items.Count - 1, item);
                where.SelectedItem = item;
            }
            else where.SelectedIndex = 0;
        };
        if (places.Count == 0) form.AddRaw(Ui.Text(L.T("No encontré OneDrive, Google Drive, Dropbox ni iCloud en esta PC: elegí otra carpeta (por ejemplo un disco de red)."), "Muted", 11.5));

        var encrypt = new CheckBox { Content = L.T("Cifrar la copia con una frase (recomendado)"), IsChecked = st.CloudEncrypt, Margin = new Thickness(0, 16, 0, 0) };
        form.AddRaw(encrypt);
        bool hasPass = Cloud.HasPassphrase;
        var pass1 = new PasswordBox { Padding = new Thickness(8, 6, 8, 6) };
        var pass2 = new PasswordBox { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 6, 0, 0) };
        foreach (var pb in new[] { pass1, pass2 })
        {
            pb.SetResourceReference(Control.BackgroundProperty, "Surface");
            pb.SetResourceReference(Control.ForegroundProperty, "Ink");
            pb.SetResourceReference(Control.BorderBrushProperty, "FaintBorder");
        }
        var passPanel = new StackPanel();
        passPanel.Children.Add(Ui.Label(hasPass ? L.T("Cambiar la frase (vacío: se mantiene)") : L.T("Frase de cifrado"), new Thickness(0, 12, 0, 6)));
        passPanel.Children.Add(pass1);
        passPanel.Children.Add(pass2);
        var warn = Ui.Text(L.T("Guardala en un lugar seguro: sin la frase, las copias cifradas no se pueden abrir. BuzzDesk la recuerda en esta PC para las copias automáticas."), "Accent", 11.5);
        warn.Margin = new Thickness(0, 6, 0, 0);
        passPanel.Children.Add(warn);
        form.AddRaw(passPanel);
        void SyncPass() => passPanel.Visibility = encrypt.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        encrypt.Click += (_, _) => SyncPass();
        SyncPass();

        var status = Ui.Text(L.T("Última copia:") + " " + (st.CloudLastUtc.Length > 0 ? Fmt.When(Db.ParseUtc(st.CloudLastUtc)) : L.T("nunca")) +
                             (st.CloudLastError.Length > 0 ? "\n" + L.T("Último error:") + " " + st.CloudLastError : ""), st.CloudLastError.Length > 0 ? "ErrorInk" : "Muted", 12);
        status.Margin = new Thickness(0, 16, 0, 0);
        form.AddRaw(status);
        var info = Ui.Text(L.F("Se sube una vez por día y al salir si hubo cambios. Se conservan {0} copias diarias y {1} mensuales.", st.CloudKeepDaily, st.CloudKeepMonthly), "Muted", 11.5);
        info.Margin = new Thickness(0, 4, 0, 0);
        form.AddRaw(info);

        if (!form.Ask(owner, () =>
        {
            string folder = (string)((ComboBoxItem)where.SelectedItem).Tag;
            if (folder == "?") return L.T("Elegí una carpeta.");
            if (folder.Length > 0 && encrypt.IsChecked == true)
            {
                if (pass1.Password.Length == 0 && !hasPass) return L.T("Escribí una frase de cifrado (o desactivá el cifrado).");
                if (pass1.Password.Length > 0 && pass1.Password.Length < 8) return L.T("La frase necesita al menos 8 caracteres.");
                if (pass1.Password != pass2.Password) return L.T("Las dos frases no coinciden.");
            }
            if (folder.Length > 0 && folder.StartsWith(DataFolder.Root, StringComparison.OrdinalIgnoreCase)) return L.T("Elegí una carpeta fuera de la de datos de BuzzDesk.");
            return null;
        })) return;
        st.CloudFolder = (string)((ComboBoxItem)where.SelectedItem).Tag;
        st.CloudEncrypt = encrypt.IsChecked == true;
        if (st.CloudEncrypt && pass1.Password.Length > 0) Cloud.SavePassphrase(pass1.Password);
        st.CloudLastError = "";
        Store.SaveSettings();
        if (Cloud.Enabled) UploadNow();
        App.Current.Desk?.ShowCloud();
    }

    /// <summary>"Back up now", with the result in the footer (or a dialog if it failed).</summary>
    public static void UploadNow()
    {
        var main = App.Current.Desk;
        if (!Cloud.Enabled) { Configure(main); return; }
        main?.SetStatus(L.T("Subiendo copia…"));
        Task.Run(Cloud.Upload).ContinueWith(t => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var (ok, message) = t.Result;
            main?.SetStatus(message);
            main?.ShowCloud();
            if (!ok) PromptWindow.Info(main, L.T("No se pudo subir la copia"), message);
        }));
    }

    /// <summary>File → Restore from a backup: pick a copy (cloud or local), check it, confirm, replace, restart.</summary>
    public static void Restore(Window? owner)
    {
        var st = Store.Settings;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("Restaurar desde una copia"),
            Filter = L.T("Copias de BuzzDesk") + " (*.bzdk;*.db)|*.bzdk;*.db",
            InitialDirectory = Directory.Exists(st.CloudFolder) ? st.CloudFolder : Db.BackupDir,
        };
        if (dlg.ShowDialog(owner) != true) return;
        string? pass = null;
        if (CloudBackup.IsEncrypted(dlg.FileName))
        {
            pass = Cloud.LoadPassphrase();
            try { if (pass != null) CloudBackup.Decrypt(File.ReadAllBytes(dlg.FileName), pass); }
            catch (System.Security.Cryptography.CryptographicException) { pass = null; }
            if (pass == null)
            {
                var ask = new FormWindow(L.T("Copia cifrada"));
                var box = ask.Add(L.T("Frase de cifrado"), new PasswordBox { Padding = new Thickness(8, 6, 8, 6) });
                if (!ask.Ask(owner, () => box.Password.Length == 0 ? L.T("Escribí la frase.") : null)) return;
                pass = box.Password;
            }
        }
        CloudBackup.Inspection copy;
        try { copy = CloudBackup.Inspect(dlg.FileName, pass); }
        catch (System.Security.Cryptography.CryptographicException) { PromptWindow.Info(owner, L.T("No se pudo abrir la copia"), L.T("La frase no es correcta, o el archivo está dañado.")); return; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException) { PromptWindow.Info(owner, L.T("No se pudo abrir la copia"), ex.Message); return; }
        string details = L.F("Copia: {0}", Path.GetFileName(dlg.FileName)) + "\n" +
                         (copy.CreatedLocal is DateTime when ? L.F("Fecha: {0}", when.ToString("g", L.Culture)) + "\n" : "") +
                         L.F("Contenido: {0} pendientes, {1} temas, {2} enlaces, {3} hábitos, {4} vencimientos", copy.Tasks, copy.Topics, copy.Links, copy.Habits, copy.Bills);
        if (!PromptWindow.Confirm(owner, L.T("Restaurar esta copia"),
                L.T("Tus datos actuales se reemplazan por los de la copia. Antes se guarda una copia de seguridad de lo actual, por las dudas. BuzzDesk se reinicia al terminar.") + "\n\n" + details,
                L.T("Restaurar"), L.T("Cancelar")))
        {
            try { File.Delete(copy.TempDb); } catch (IOException) { }
            return;
        }
        try { CloudBackup.Apply(copy, Db.BackupDir); }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException or InvalidOperationException)
        {
            PromptWindow.Info(owner, L.T("No se pudo restaurar"), ex.Message + "\n\n" + L.T("Tus datos anteriores están en la carpeta de copias (…-pre-restore.db)."));
            return;
        }
        App.Current.Relaunch();
    }
}
