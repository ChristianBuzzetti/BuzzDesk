using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BuzzDesk.Data;
using BuzzDesk.Domain;
using D = System.Drawing;

namespace BuzzDesk;

/// <summary>
/// BuzzDesk.exe --shot DIR [--lang en|es] : the README screenshots, on a throwaway demo database (DIR\demo, never
/// the person's data; settings are not saved). Fills sample tasks, topics, reminders and links, opens each view and
/// the quick capture card, and saves them with PrintWindow (WebView2/Boberto included). Writes DIR\shots.log.
/// </summary>
static class Shots
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    public static async Task RunAsync(MainWindow main)
    {
        string dir = App.ShotDir!;
        Directory.CreateDirectory(dir);
        var log = new List<string>();
        try
        {
            Seed(log);
            main.Width = 1440; main.Height = 900;
            main.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            main.Show();
            main.Navigate("today");
            await Task.Delay(9000);   // Boberto's first paint
            Save(main, Path.Combine(dir, "today.png"), log);

            main.Navigate("tasks");
            var first = Tasks.List(new TaskFilter(TaskScope.Open), Clock.Today).FirstOrDefault(t => t.Recurrence != null) ?? Tasks.List(new TaskFilter(TaskScope.Open), Clock.Today).First();
            main.OpenTask(first.Id);
            await Task.Delay(1500);
            Save(main, Path.Combine(dir, "tasks.png"), log);

            var topic = Topics.List(TopicScope.Open, Clock.Today).First(t => t.Status == TopicState.Waiting);
            main.OpenTopic(topic.Id);
            await Task.Delay(1500);
            Save(main, Path.Combine(dir, "topics.png"), log);

            main.Navigate("links");
            await Task.Delay(1200);
            Save(main, Path.Combine(dir, "links.png"), log);

            main.ShotSearch(L.Es ? "indices" : "index");
            await Task.Delay(1200);
            Save(main, Path.Combine(dir, "search.png"), log);
            main.ShotSearch("");

            main.Navigate("today");
            var quick = QuickCaptureWindow.ForShot(L.Es ? "Revisar backups de PROD !3 mañana 9:30 #dba" : "Check PROD backups !3 tomorrow 9:30 #dba", main);
            await Task.Delay(1200);
            Save(quick, Path.Combine(dir, "quick-capture.png"), log);
            quick.Close();

            using (var c = Db.Open())
            {
                log.Add("schema v" + Migrator.CurrentVersion(c) + ", integrity " + Db.Scalar<string>(c, "PRAGMA integrity_check;"));
                Db.Exec(c, "INSERT INTO search_fts(search_fts) VALUES ('integrity-check');");
                log.Add("fts integrity ok");
            }
            log.Add("OK");
        }
        catch (Exception ex) { log.Add("FAIL " + ex); }
        File.WriteAllLines(Path.Combine(dir, "shots.log"), log);
        App.Current.ExitApp();
    }

    /// <summary>Sample data that shows every feature (English or Spanish, following --lang).</summary>
    static void Seed(List<string> log)
    {
        var today = Clock.Today;
        bool es = L.Es;
        string S(string spanish, string english) => es ? spanish : english;
        var areas = Sections.All();
        long work = areas[0].Id, personal = areas[1].Id;
        long migration = Sections.Add(S("Migración SQL 2025", "SQL 2025 migration"), work);
        long monitoring = Sections.Add(S("Monitoreo", "Monitoring"), work);

        long t1 = Tasks.Add(new Tasks.Draft(S("Revisar fragmentación de índices en PROD", "Review index fragmentation in PROD"), work, 3, today.AddDays(-1), Tags: ["dba", "prod"], Notes: S("Foco en las tablas de facturación.", "Focus on the billing tables.")), today);
        Tasks.Add(new Tasks.Draft(S("Rebuild online de IX_Factura_Fecha", "Online rebuild of IX_Invoice_Date"), ParentId: t1), today);
        long sub = Tasks.Add(new Tasks.Draft(S("Actualizar estadísticas", "Update statistics"), ParentId: t1), today);
        Tasks.Complete(sub, today);
        Tasks.Add(new Tasks.Draft(S("Verificar backups nocturnos", "Check nightly backups"), work, 2, today, new TimeOnly(9, 0), Tags: ["backup"], Recurrence: new RecurrenceRule(Freq.Weekly, 1, 0b0011111)), today);
        long close = Tasks.Add(new Tasks.Draft(S("Informe mensual de capacidad", "Monthly capacity report"), work, 2, Recurrence: new RecurrenceRule(Freq.LastBusinessDay), Tags: ["informe"]), today);
        Tasks.Add(new Tasks.Draft(S("Probar restore en QA", "Test restore on QA"), migration, 2, today, Tags: ["migracion"]), today);
        Tasks.Add(new Tasks.Draft(S("Pedir ventana de mantenimiento", "Request a maintenance window"), migration, 1, today.AddDays(2)), today);
        long p = Tasks.Add(new Tasks.Draft(S("Ajustar alertas de espacio en disco", "Tune disk space alerts"), monitoring, 1, today.AddDays(5), Tags: ["mon"]), today);
        Tasks.Postpone(p, today.AddDays(6)); Tasks.Postpone(p, today.AddDays(7));
        Tasks.Add(new Tasks.Draft(S("Turno con el dentista", "Dentist appointment"), personal, 0, today.AddDays(3), new TimeOnly(18, 30)), today);
        Tasks.Add(new Tasks.Draft(S("Renovar el seguro del auto", "Renew car insurance"), personal, 1), today);
        Tasks.Add(new Tasks.Draft(S("Leer notas de versión de SQL 2025", "Read the SQL 2025 release notes"), migration), today);

        long topic = Topics.Add(S("Migración del cluster de facturación", "Billing cluster migration"), work, today, ["migracion"]);
        Topics.AddNote(topic, S("Kickoff con infraestructura. Ventana tentativa: sábado 22 hs.", "Kickoff with infrastructure. Tentative window: Saturday 10pm."));
        Topics.AddNote(topic, S("Infra confirmó el ambiente de QA con la misma versión de storage.", "Infra confirmed the QA environment with the same storage version."));
        Topics.SetStatus(topic, TopicState.Waiting, "Mariela (Infra)", S("Esperando a Mariela (Infra)", "Waiting on Mariela (Infra)"));
        Tasks.Add(new Tasks.Draft(S("Validar compatibilidad de jobs del Agent", "Validate Agent job compatibility"), migration, 2, today.AddDays(1), TopicId: topic), today);
        long t2 = Topics.Add(S("Lentitud en reportes de ventas", "Slow sales reports"), work, today.AddDays(2), ["perf"]);
        Topics.AddNote(t2, S("El plan cambió después del último deploy: parameter sniffing.", "The plan changed after the last deploy: parameter sniffing."));
        long t3 = Topics.Add(S("Licencias para el ambiente de DR", "Licenses for the DR environment"), work, today.AddDays(-1));
        Topics.SetStatus(t3, TopicState.Waiting, "Compras", S("Esperando a Compras", "Waiting on Purchasing"));
        Topics.Add(S("Mudanza de oficina", "Office move"), personal, today.AddDays(10));

        Reminders.Add(Clock.ToUtc(today, new TimeOnly(Math.Min(22, Clock.Now.Hour + 1), 30)), title: S("Daily del equipo", "Team daily"));
        Reminders.Add(Clock.ToUtc(today.AddDays(1), new TimeOnly(9, 0)), taskId: close);
        Topics.AddNote(null, S("Preguntar a Diego por el certificado del linked server", "Ask Diego about the linked server certificate"));

        var (grafana, _) = Links.Add("https://grafana.example.com/d/sql", "Grafana · SQL Server", S("Dashboards de rendimiento", "Performance dashboards"), ["mon"], pinned: true);
        var (runbook, _) = Links.Add("https://wiki.example.com/dba/runbooks", S("Runbooks del equipo DBA", "DBA team runbooks"), tags: ["docs"], pinned: true);
        Links.Add("https://learn.microsoft.com/sql/relational-databases/indexes/reorganize-and-rebuild-indexes", S("Reorganizar y reconstruir índices", "Reorganize and rebuild indexes"), tags: ["docs", "indices"]);
        Links.Add("https://jira.example.com/browse/DBA", "Jira · DBA", tags: ["tickets"], pinned: true);
        Links.Add("https://status.example.com", S("Estado de servicios", "Service status"), tags: ["mon"]);
        Links.Opened(grafana); Links.Opened(grafana); Links.Opened(runbook);
        Links.Attach(topic, runbook);
        log.Add("seeded " + Tasks.Counts(today) + " tasks, " + Topics.Counts(today) + " topics, " + Links.Count() + " links");
    }

    static void Save(Window w, string path, List<string> log)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.UpdateLayout();
        var h = new WindowInteropHelper(w).Handle;
        Native.GetWindowRect(h, out var r);
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        using var bmp = new D.Bitmap(width, height);
        using (var g = D.Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            PrintWindow(h, hdc, 2);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(path, D.Imaging.ImageFormat.Png);
        log.Add("saved " + path + " " + width + "x" + height);
    }
}
