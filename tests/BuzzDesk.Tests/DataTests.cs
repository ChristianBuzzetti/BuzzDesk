using BuzzDesk.Data;
using BuzzDesk.Domain;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Tests;

[Collection("db")]
public class DataTests : IDisposable
{
    readonly TempDb db = new();
    static readonly DateOnly Today = new(2026, 11, 15);
    public void Dispose() => db.Dispose();

    [Fact]
    public void Schema_is_versioned_with_wal_and_application_id()
    {
        using var c = Db.Open();
        Assert.Equal("wal", Db.Scalar<string>(c, "PRAGMA journal_mode;"));
        Assert.Equal(Db.ApplicationId, Db.Scalar<long>(c, "PRAGMA application_id;"));
        Assert.Equal(1L, Db.Scalar<long>(c, "PRAGMA foreign_keys;"));
        Assert.Equal(Migrator.All.Count, Migrator.CurrentVersion(c));
        Assert.Empty(Migrator.Pending(c));
        Assert.Equal("ok", Db.Scalar<string>(c, "PRAGMA integrity_check;"));
        Assert.Equal(2, Sections.All().Count);
    }

    [Fact]
    public void Initialize_twice_is_a_no_op()
    {
        Db.Initialize(c => Sections.SeedDefaults(c, "Work", "Personal"));
        Assert.Equal(2, Sections.All().Count);
    }

    [Fact]
    public void Changed_migration_is_refused()
    {
        using var c = Db.Open();
        Db.Exec(c, "UPDATE schema_version SET checksum = $x WHERE version = 1", ("$x", new string('0', 64)));
        Assert.Throws<InvalidOperationException>(() => Migrator.Pending(c));
    }

    [Fact]
    public void Constraints_hold()
    {
        using var c = Db.Open();
        string now = Db.NowUtc();
        // bad date format
        Assert.Throws<SqliteException>(() => Db.Exec(c, "INSERT INTO task(title, due_date, created_utc, updated_utc) VALUES ('x', '15/11/2026', $n, $n)", ("$n", now)));
        // done without completion time
        Assert.Throws<SqliteException>(() => Db.Exec(c, "INSERT INTO task(title, status, created_utc, updated_utc) VALUES ('x', 'done', $n, $n)", ("$n", now)));
        // unknown section
        Assert.Throws<SqliteException>(() => Db.Exec(c, "INSERT INTO task(title, section_id, created_utc, updated_utc) VALUES ('x', 999, $n, $n)", ("$n", now)));
        // waiting without a person
        Assert.Throws<SqliteException>(() => Db.Exec(c, "INSERT INTO topic(title, status, created_utc, updated_utc) VALUES ('x', 'waiting', $n, $n)", ("$n", now)));
        // reminder on both a task and a topic
        long t = Db.Insert(c, "INSERT INTO task(title, created_utc, updated_utc) VALUES ('t', $n, $n)", ("$n", now));
        long tp = Db.Insert(c, "INSERT INTO topic(title, created_utc, updated_utc) VALUES ('tp', $n, $n)", ("$n", now));
        Assert.Throws<SqliteException>(() => Db.Exec(c, "INSERT INTO reminder(task_id, topic_id, remind_at_utc, created_utc) VALUES ($t, $tp, $n, $n)", ("$t", t), ("$tp", tp), ("$n", now)));
        // STRICT: text in an integer column
        Assert.Throws<SqliteException>(() => Db.Exec(c, "INSERT INTO task(title, priority, created_utc, updated_utc) VALUES ('x', 'high', $n, $n)", ("$n", now)));
    }

    [Fact]
    public void Add_list_and_counts()
    {
        long work = Sections.All()[0].Id;
        Tasks.Add(new Tasks.Draft("Late", work, 2, Today.AddDays(-2)), Today);
        Tasks.Add(new Tasks.Draft("Due today", work, 3, Today, new TimeOnly(9, 0), Tags: ["dba", "#prod"]), Today);
        Tasks.Add(new Tasks.Draft("Later", null, 0, Today.AddDays(5)), Today);
        Tasks.Add(new Tasks.Draft("Someday"), Today);

        var today = Tasks.List(new TaskFilter(TaskScope.Today), Today);
        Assert.Equal(["Late", "Due today"], today.Select(t => t.Title));
        Assert.Equal(["dba", "prod"], today[1].Tags);
        Assert.Equal((1, 1, 4), Tasks.Counts(Today));
        Assert.Equal(2, Tasks.List(new TaskFilter(TaskScope.Open, SectionId: work), Today).Count);
        Assert.Single(Tasks.List(new TaskFilter(TaskScope.NoDate), Today));
        Assert.Single(Tasks.List(new TaskFilter(TaskScope.Open, Text: "toda"), Today));
    }

    [Fact]
    public void Completing_a_recurring_task_creates_the_next_with_subtasks_tags_and_reminders()
    {
        var rule = new RecurrenceRule(Freq.LastBusinessDay);
        long id = Tasks.Add(new Tasks.Draft("Cierre de mes", Recurrence: rule, Tags: ["cierre"]), Today);
        var first = Tasks.Get(id)!;
        Assert.Equal(new DateOnly(2026, 11, 30), first.DueDate);
        Tasks.Add(new Tasks.Draft("Backup", ParentId: id), Today);
        using (var c = Db.Open())
            Db.Exec(c, "INSERT INTO reminder(task_id, remind_at_utc, created_utc) VALUES ($t, '2026-11-30T12:00:00Z', $n)", ("$t", id), ("$n", Db.NowUtc()));

        long? next = Tasks.Complete(id, new DateOnly(2026, 11, 30));
        Assert.NotNull(next);
        var n = Tasks.Get(next!.Value)!;
        Assert.Equal(new DateOnly(2026, 12, 31), n.DueDate);
        Assert.Equal(first.RecurrenceId, n.RecurrenceId);
        Assert.Equal(["cierre"], n.Tags);
        Assert.Equal(1, n.SubtaskCount);
        Assert.Equal(0, n.SubtaskDone);
        Assert.Equal(TaskState.Done, Tasks.Get(id)!.Status);
        using (var c = Db.Open())
        {
            Assert.Equal("2026-12-31T12:00:00Z", Db.Scalar<string>(c, "SELECT remind_at_utc FROM reminder WHERE task_id = $t", ("$t", next.Value)));
            Assert.Equal(1L, Db.Scalar<long>(c, "SELECT count(*) FROM reminder WHERE task_id = $t AND dismissed_utc IS NOT NULL", ("$t", id)));
        }
    }

    [Fact]
    public void Late_completion_skips_past_instances()
    {
        long id = Tasks.Add(new Tasks.Draft("Daily check", DueDate: Today.AddDays(-5), Recurrence: new RecurrenceRule(Freq.Daily)), Today);
        long next = Tasks.Complete(id, Today)!.Value;
        Assert.Equal(Today, Tasks.Get(next)!.DueDate);
    }

    [Fact]
    public void Postpone_moves_date_reminders_and_counts()
    {
        long id = Tasks.Add(new Tasks.Draft("Patear", DueDate: Today), Today);
        using (var c = Db.Open())
            Db.Exec(c, "INSERT INTO reminder(task_id, remind_at_utc, created_utc) VALUES ($t, '2026-11-15T12:00:00Z', $n)", ("$t", id), ("$n", Db.NowUtc()));
        Tasks.Postpone(id, Today.AddDays(1));
        Tasks.Postpone(id, Today.AddDays(3));
        var t = Tasks.Get(id)!;
        Assert.Equal(Today.AddDays(3), t.DueDate);
        Assert.Equal(2, t.PostponeCount);
        using var c2 = Db.Open();
        Assert.Equal("2026-11-18T12:00:00Z", Db.Scalar<string>(c2, "SELECT remind_at_utc FROM reminder WHERE task_id = $t", ("$t", id)));
    }

    [Fact]
    public void Delete_and_restore()
    {
        long id = Tasks.Add(new Tasks.Draft("Borrar", DueDate: Today), Today);
        Tasks.Add(new Tasks.Draft("Sub", ParentId: id), Today);
        Tasks.Delete(id);
        Assert.Empty(Tasks.List(new TaskFilter(TaskScope.All), Today));
        Tasks.Restore(id);
        Assert.Equal(1, Tasks.Get(id)!.SubtaskCount);
    }

    [Fact]
    public void Update_changes_rule_and_tags_and_prunes()
    {
        long id = Tasks.Add(new Tasks.Draft("Semanal", DueDate: Today, Recurrence: new RecurrenceRule(Freq.Weekly), Tags: ["a"]), Today);
        var t = Tasks.Get(id)!;
        Tasks.Update(t with { Recurrence = new RecurrenceRule(Freq.Monthly, 1, MonthDay: 15), Title = "Mensual" }, ["b"]);
        var u = Tasks.Get(id)!;
        Assert.Equal(Freq.Monthly, u.Recurrence!.Freq);
        Assert.Equal(["b"], u.Tags);
        Assert.DoesNotContain(Tags.All(), x => x.Name == "a");
        using var c = Db.Open();
        Assert.Equal(1L, Db.Scalar<long>(c, "SELECT count(*) FROM recurrence"));
    }

    [Fact]
    public void Backup_is_verified_and_pruned()
    {
        var day = new DateTime(2026, 11, 15, 10, 0, 0);
        for (int i = 0; i < 40; i++) Assert.NotNull(Backup.DailyIfDue(db.BackupDir, 14, 8, day.AddDays(-i)));
        Assert.Null(Backup.DailyIfDue(db.BackupDir, 14, 8, day));   // already done today
        var files = Directory.GetFiles(db.BackupDir, "*.db").Select(Path.GetFileName).ToList();
        Assert.Contains("buzzdesk-20261115.db", files);
        Assert.Contains("buzzdesk-20261102.db", files);              // 14th newest
        Assert.DoesNotContain("buzzdesk-20261101.db", files);        // Sunday, not the oldest of its week
        Assert.Contains("buzzdesk-20261026.db", files);              // Monday: oldest of its week
        Assert.True(files.Count is > 14 and < 14 + 8);
        using var b = new SqliteConnection("Data Source=" + Path.Combine(db.BackupDir, "buzzdesk-20261115.db") + ";Pooling=False");
        b.Open();
        Assert.Equal("delete", Db.Scalar<string>(b, "PRAGMA journal_mode;"));
        Assert.Equal(Db.ApplicationId, Db.Scalar<long>(b, "PRAGMA application_id;"));
    }
}

[Collection("db")]
public class ReminderTests : IDisposable
{
    readonly TempDb db = new();
    static readonly DateOnly Today = new(2026, 11, 15);
    static readonly DateTime Noon = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => db.Dispose();

    [Fact]
    public void Ring_snooze_dismiss()
    {
        long task = Tasks.Add(new Tasks.Draft("Llamar a soporte", DueDate: Today), Today);
        long r1 = Reminders.Add(Noon.AddMinutes(-5), taskId: task);
        long r2 = Reminders.Add(Noon.AddMinutes(30), title: "Reunión");
        Assert.Equal([r1], Reminders.DueNow(Noon).Select(r => r.Id));
        Assert.Equal("Llamar a soporte", Reminders.DueNow(Noon)[0].Title);
        Assert.Equal(r2, Reminders.Next(Noon)!.Id);

        Reminders.MarkFired([r1], Noon);
        Assert.Empty(Reminders.DueNow(Noon));
        Reminders.Snooze(r1, Noon.AddMinutes(10));
        Assert.Empty(Reminders.DueNow(Noon.AddMinutes(9)));
        Assert.Single(Reminders.DueNow(Noon.AddMinutes(10)));
        Reminders.Dismiss(r1);
        Assert.DoesNotContain(Reminders.DueNow(Noon.AddHours(1)), r => r.Id == r1);
        Assert.Equal([r2], Reminders.Between(Noon.AddHours(-12), Noon.AddHours(12)).Select(r => r.Id));   // r1 dismissed
    }

    [Fact]
    public void Completed_or_deleted_task_does_not_ring()
    {
        long a = Tasks.Add(new Tasks.Draft("A"), Today);
        long b = Tasks.Add(new Tasks.Draft("B"), Today);
        Reminders.Add(Noon.AddMinutes(-1), taskId: a);
        Reminders.Add(Noon.AddMinutes(-1), taskId: b);
        Tasks.Complete(a, Today);
        Tasks.Delete(b);
        Assert.Empty(Reminders.DueNow(Noon));
    }

    [Fact]
    public void Loose_reminder_needs_a_title()
    {
        Assert.Throws<SqliteException>(() => Reminders.Add(Noon, title: "  "));
    }
}

[Collection("db")]
public class SchemaLintTests : IDisposable
{
    readonly TempDb db = new();
    public void Dispose() => db.Dispose();

    /// <summary>Every foreign key column has an index whose first column is that column.</summary>
    [Fact]
    public void Foreign_keys_are_indexed()
    {
        using var c = Db.Open();
        var tables = Db.Query(c, "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND sql NOT LIKE 'CREATE VIRTUAL%'", r => r.GetString(0));
        var missing = new List<string>();
        foreach (var t in tables)
        {
            var fks = Db.Query(c, $"SELECT \"from\" FROM pragma_foreign_key_list('{t}')", r => r.GetString(0));
            var firstCols = Db.Query(c, $"SELECT il.name FROM pragma_index_list('{t}') il", r => r.GetString(0))
                .Select(ix => Db.Query(c, $"SELECT name FROM pragma_index_info('{ix}') WHERE seqno = 0", r => r.IsDBNull(0) ? "" : r.GetString(0)).FirstOrDefault() ?? "")
                .ToHashSet();
            // WITHOUT ROWID tables: the primary key's first column counts as indexed.
            var pkFirst = Db.Query(c, $"SELECT name FROM pragma_table_info('{t}') WHERE pk = 1", r => r.GetString(0));
            foreach (var fk in fks)
                if (!firstCols.Contains(fk) && !pkFirst.Contains(fk)) missing.Add(t + "." + fk);
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void All_tables_are_strict()
    {
        using var c = Db.Open();
        var loose = Db.Query(c, "SELECT name FROM pragma_table_list WHERE schema = 'main' AND type = 'table' AND strict = 0 AND name NOT LIKE 'sqlite_%'", r => r.GetString(0));
        Assert.Empty(loose);
    }
}

[Collection("db")]
public class TopicTests : IDisposable
{
    readonly TempDb db = new();
    static readonly DateOnly Today = new(2026, 11, 15);
    public void Dispose() => db.Dispose();

    [Fact]
    public void Status_waiting_review_and_timeline()
    {
        long id = Topics.Add("Migración a SQL 2025", nextReview: Today, tags: ["migracion"]);
        Topics.AddNote(id, "Pedido el ambiente a infraestructura");
        Topics.SetStatus(id, TopicState.Waiting, "Juan Pérez", "Esperando a Juan Pérez");
        var t = Topics.Get(id)!;
        Assert.Equal(TopicState.Waiting, t.Status);
        Assert.Equal("Juan Pérez", t.WaitingOn);
        Assert.NotNull(t.WaitingSinceUtc);
        Assert.True(t.ReviewDue(Today));
        Assert.Equal((1, 1, 1), Topics.Counts(Today));
        Assert.Single(Topics.List(TopicScope.Review, Today));

        // Same person again: nothing changes, no extra timeline line.
        Topics.SetStatus(id, TopicState.Waiting, "juan pérez", "dup");
        Assert.Equal(2, Topics.Timeline(id).Count);

        Topics.Reviewed(id, Today.AddDays(7), "Revisado");
        Assert.False(Topics.Get(id)!.ReviewDue(Today));
        Topics.SetStatus(id, TopicState.Closed, null, "Cerrado");
        t = Topics.Get(id)!;
        Assert.Equal(TopicState.Closed, t.Status);
        Assert.Null(t.WaitingOnId);
        Assert.NotNull(t.ClosedUtc);
        Assert.Equal(["status", "system", "status", "note"], Topics.Timeline(id).Select(n => n.Kind));
        Assert.Empty(Topics.List(TopicScope.Open, Today));
        Assert.Single(Topics.List(TopicScope.Closed, Today));
    }

    [Fact]
    public void Waiting_requires_a_person() =>
        Assert.Throws<ArgumentException>(() => Topics.SetStatus(Topics.Add("X"), TopicState.Waiting, " ", "x"));

    [Fact]
    public void Tasks_belong_to_a_topic_and_loose_notes_can_be_filed()
    {
        long id = Topics.Add("Incidente backups");
        Tasks.Add(new Tasks.Draft("Revisar job", TopicId: id), Today);
        Assert.Equal(1, Topics.Get(id)!.OpenTasks);
        Assert.Single(Tasks.List(new TaskFilter(TaskScope.Open, TopicId: id), Today));
        long note = Topics.AddNote(null, "Ver logs del agente");
        Assert.Single(Topics.Inbox());
        Topics.FileNote(note, id);
        Assert.Empty(Topics.Inbox());
        Assert.Equal(1, Topics.Get(id)!.Notes);
    }
}

[Collection("db")]
public class LinkTests : IDisposable
{
    readonly TempDb db = new();
    public void Dispose() => db.Dispose();

    [Theory]
    [InlineData("grafana.local/d/abc", "https://grafana.local/d/abc")]
    [InlineData("http://intranet", "http://intranet")]
    [InlineData("mailto:dba@empresa.com", "mailto:dba@empresa.com")]
    [InlineData(@"C:\docs\runbook.pdf", "file:///C:/docs/runbook.pdf")]
    [InlineData("ftp://x", null)]
    [InlineData("  ", null)]
    public void Normalize(string input, string? expected) => Assert.Equal(expected, Links.Normalize(input));

    [Fact]
    public void Add_search_launch_pin_and_topics()
    {
        var (grafana, _) = Links.Add("https://grafana.local", "Grafana", "Dashboards de monitoreo", ["mon"]);
        var (wiki, _) = Links.Add("https://wiki.local/dba", "Wiki DBA", tags: ["docs"]);
        var (again, existed) = Links.Add("https://grafana.local", "Otro título");
        Assert.True(existed);
        Assert.Equal(grafana, again);

        Assert.Equal(["Grafana"], Links.List("monitoreo").Select(l => l.Title));
        Assert.Equal(["Wiki DBA"], Links.List("docs").Select(l => l.Title));        // by tag
        Assert.Equal(["Wiki DBA"], Links.List("wiki dba").Select(l => l.Title));    // every word

        Links.Opened(wiki); Links.Opened(wiki);
        Assert.Equal(wiki, Links.Launch("")[0].Id);                                  // most used first
        Assert.Equal(grafana, Links.Launch("gra")[0].Id);                            // prefix wins

        Links.SetPinned(grafana, true);
        Assert.Equal([grafana], Links.List(pinnedOnly: true).Select(l => l.Id));

        long topic = Topics.Add("Monitoreo");
        Links.Attach(topic, grafana); Links.Attach(topic, grafana);
        Assert.Single(Links.ForTopic(topic));
        Links.Detach(topic, grafana);
        Assert.Empty(Links.ForTopic(topic));
    }

    [Fact]
    public void Deleted_url_can_be_saved_again_and_restore_does_not_duplicate()
    {
        var (a, _) = Links.Add("https://x.local", "X");
        Links.Delete(a);
        var (b, existed) = Links.Add("https://x.local", "X again");
        Assert.False(existed);
        Assert.NotEqual(a, b);
        Assert.False(Links.Restore(a));   // the URL is live again: unique index ux_link_url
    }

    [Fact]
    public void Favicon_size_is_limited()
    {
        var (id, _) = Links.Add("https://big.local", "Big");
        Links.SetFavicon(id, new byte[1024]);
        Assert.Equal(1024, Links.Get(id)!.Favicon!.Length);
        Assert.Throws<SqliteException>(() => Links.SetFavicon(id, new byte[300_000]));
    }
}

[Collection("db")]
public class SearchTests : IDisposable
{
    readonly TempDb db = new();
    static readonly DateOnly Today = new(2026, 11, 15);
    public void Dispose() => db.Dispose();

    [Theory]
    [InlineData("migra prod", "\"migra\"* AND \"prod\"*")]
    [InlineData("\"plan de backup\" sql", "\"plan de backup\" AND \"sql\"*")]
    [InlineData("a OR b NOT c*", "\"a\"* AND \"OR\"* AND \"b\"* AND \"NOT\"* AND \"c\"*")]
    [InlineData("x\"y", "\"x\"* AND \"y\"*")]
    [InlineData("  ;; ", null)]
    public void Query_building(string text, string? expected) => Assert.Equal(expected, Search.BuildQuery(text));

    [Fact]
    public void Finds_everything_without_accents_and_follows_changes()
    {
        long task = Tasks.Add(new Tasks.Draft("Revisión de índices", Notes: "fragmentación en PROD", Tags: ["dba"]), Today);
        long sub = Tasks.Add(new Tasks.Draft("Rebuild online", ParentId: task), Today);
        long topic = Topics.Add("Migración a SQL 2025", tags: ["proyecto"]);
        Topics.SetStatus(topic, TopicState.Waiting, "Mariela", "Esperando a Mariela");
        long note = Topics.AddNote(topic, "Infra confirmó el ambiente de QA");
        var (link, _) = Links.Add("https://wiki.local/indices", "Guía de índices", tags: ["docs"]);

        Assert.Contains(Search.Run("revision"), h => h.Kind == "task" && h.RefId == task);          // no accent needed
        Assert.Contains(Search.Run("fragmenta"), h => h.Kind == "task" && h.RefId == task);         // prefix in notes
        Assert.Contains(Search.Run("rebuild"), h => h.Kind == "task" && h.RefId == task && h.Title.Contains("›"));   // subtask → parent
        Assert.Contains(Search.Run("mariela"), h => h.Kind == "topic" && h.RefId == topic);         // waiting on
        Assert.Contains(Search.Run("ambiente qa"), h => h.Kind == "note" && h.RefId == note && h.Title.StartsWith("Migración"));
        Assert.Contains(Search.Run("docs"), h => h.Kind == "link" && h.RefId == link);              // by tag
        Assert.Contains(Search.Run("\"de índices\""), h => h.Kind == "link");
        var first = Search.Run("indices");
        Assert.Contains(Search.MarkStart, first[0].Snippet);

        // edits, tags, deletes
        Tasks.Update(Tasks.Get(task)! with { Title = "Estadísticas" }, ["perf"]);
        Assert.DoesNotContain(Search.Run("revision"), h => h.RefId == task && h.Kind == "task");
        Assert.Contains(Search.Run("perf"), h => h.RefId == task);
        Assert.DoesNotContain(Search.Run("dba"), h => h.RefId == task);
        Tasks.Delete(task);
        Assert.DoesNotContain(Search.Run("estadisticas"), h => h.Kind == "task");
        Tasks.Restore(task);
        Assert.Contains(Search.Run("estadisticas"), h => h.Kind == "task");
        Topics.DeleteNote(note);
        Assert.Empty(Search.Run("ambiente"));

        using var c = Db.Open();
        Assert.Equal("ok", Db.Scalar<string>(c, "PRAGMA integrity_check;"));
        Db.Exec(c, "INSERT INTO search_fts(search_fts) VALUES ('integrity-check');");
    }

    [Fact]
    public void Status_notes_are_not_indexed()
    {
        long topic = Topics.Add("Compras");
        Topics.SetStatus(topic, TopicState.Paused, null, "Pausado por presupuesto");
        Assert.Empty(Search.Run("presupuesto"));
    }
}

[Collection("db")]
public class UpgradeTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "buzzdesk-upgrade-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(dir, true); } catch (IOException) { } }

    [Fact]
    public void Version_1_database_upgrades_with_backup_and_backfill()
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "buzzdesk.db"), backups = Path.Combine(dir, "backups");
        // A database left by a build that only knew migration 1, with data in it.
        using (var c = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            c.Open();
            Db.Exec(c, $"PRAGMA application_id = {Db.ApplicationId}; PRAGMA journal_mode = WAL;");
            var m1 = Migrator.All[0];
            Db.Exec(c, "CREATE TABLE schema_version (version INTEGER PRIMARY KEY, description TEXT NOT NULL, checksum TEXT NOT NULL CHECK (length(checksum) = 64), applied_utc TEXT NOT NULL) STRICT;");
            Db.Exec(c, m1.Sql);
            Db.Exec(c, "INSERT INTO schema_version VALUES (1, $d, $c, '2026-11-01T00:00:00Z')", ("$d", m1.Description), ("$c", m1.Checksum));
            Db.Exec(c, "INSERT INTO task(title, created_utc, updated_utc) VALUES ('Tarea vieja de auditoría', '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z')");
        }
        Db.Configure(path, backups);
        int version = Db.Initialize();
        Assert.Equal(Migrator.All.Count, version);
        Assert.Single(Directory.GetFiles(backups, "*-pre-migration.db"));
        Assert.Contains(Search.Run("auditoria"), h => h.Kind == "task");
    }
}

[Collection("db")]
public class PersonalTests : IDisposable
{
    readonly TempDb db = new();
    static readonly DateOnly Today = new(2026, 11, 15);   // Sunday
    public void Dispose() => db.Dispose();

    [Fact]
    public void Habit_streaks_respect_the_days_it_applies_and_the_target()
    {
        long water = Habits.Add("Agua", target: 2);
        long gym = Habits.Add("Gimnasio", weekdaysMask: RecurrenceRule.Bit(DayOfWeek.Monday) | RecurrenceRule.Bit(DayOfWeek.Wednesday) | RecurrenceRule.Bit(DayOfWeek.Friday));
        // Water: 2 a day for the last 4 days, but only 1 five days ago.
        for (int i = 1; i <= 4; i++) { Habits.Mark(water, Today.AddDays(-i)); Habits.Mark(water, Today.AddDays(-i)); }
        Habits.Mark(water, Today.AddDays(-5));
        // Gym: Mon 2, Wed 4, Fri 6, Mon 9, Wed 11, Fri 13 — and Mon 16/11 is tomorrow.
        foreach (int d in new[] { 2, 4, 6, 9, 11, 13 }) Habits.Mark(gym, new DateOnly(2026, 11, d));
        var list = Habits.List(Today);
        var w = list.Single(h => h.Id == water);
        Assert.Equal(4, w.Streak);                 // today not done yet: still alive
        Assert.Equal(0, w.TodayCount);
        var g = list.Single(h => h.Id == gym);
        Assert.Equal(6, g.Streak);                 // weekends don't break it
        Assert.Equal(6, g.BestStreak);

        Habits.Mark(water, Today); Habits.Mark(water, Today);
        Assert.Equal(5, Habits.List(Today).Single(h => h.Id == water).Streak);
        Assert.Equal(1, Habits.Mark(water, Today, -1));
        Assert.Equal(0, Habits.Mark(water, Today, -1));
        Assert.Empty(Habits.History(water, Today, Today));
        // A missed applicable day breaks it.
        Assert.Equal(0, Habits.List(Today.AddDays(2)).Single(h => h.Id == water).Streak);
    }

    [Fact]
    public void Checklists_reuse_and_dedupe()
    {
        long super = Checklists.FindOrCreate("Súper");
        Assert.Equal(super, Checklists.FindOrCreate("súper"));
        Assert.Equal(3, Checklists.AddItems(super, ["leche", "pan", " café ", ""]));
        var items = Checklists.Items(super);
        Checklists.Toggle(items[0].Id);
        Assert.Equal(0, Checklists.AddItems(super, ["Leche"]));   // already there: unticked instead of duplicated
        Assert.All(Checklists.Items(super), i => Assert.False(i.Done));
        Checklists.Toggle(items[1].Id);
        Assert.Equal((3, 1), (Checklists.Get(super)!.Items, Checklists.Get(super)!.Done));
        Checklists.Reset(super);
        Assert.Equal(0, Checklists.Get(super)!.Done);
        Checklists.Toggle(items[2].Id);
        Checklists.ClearDone(super);
        Assert.Equal(2, Checklists.Items(super).Count);
        Assert.Contains(Search.Run("leche"), h => h.Kind == "checklist" && h.RefId == super);
    }

    [Fact]
    public void Bills_pay_advance_undo_and_totals()
    {
        long light = Bills.Add(new Bill { Name = "Luz", AmountCents = 2_500_000, Currency = "ARS", NextDue = new DateOnly(2026, 11, 20), Recurrence = new RecurrenceRule(Freq.Monthly, 1, MonthDay: 20) });
        long dni = Bills.Add(new Bill { Name = "DNI", Kind = "renewal", Currency = "ARS", NextDue = new DateOnly(2027, 3, 1), Recurrence = Bills.Yearly(new DateOnly(2027, 3, 1)) });
        long netflix = Bills.Add(new Bill { Name = "Streaming", AmountCents = 999, Currency = "usd", NextDue = new DateOnly(2026, 11, 28), Recurrence = new RecurrenceRule(Freq.Monthly, 1, MonthDay: 28) });
        Assert.Equal("USD", Bills.Get(netflix)!.Currency);
        Assert.True(Bills.IsYearly(Bills.Get(dni)!.Recurrence));

        long pay = Bills.Pay(light, 2_600_000, Today);
        Assert.Equal(new DateOnly(2026, 12, 20), Bills.Get(light)!.NextDue);
        Assert.Equal(new DateOnly(2027, 3, 1), Bills.Get(dni)!.NextDue);
        var totals = Bills.MonthTotals(2026, 11);
        Assert.Contains(("ARS", 2_600_000L, 0L), totals);
        Assert.Contains(("USD", 0L, 999L), totals);
        Bills.Unpay(pay);
        Assert.Equal(new DateOnly(2026, 11, 20), Bills.Get(light)!.NextDue);
        Assert.Empty(Bills.Payments(2026, 11));
        Bills.Pay(dni, null, Today);
        Assert.Equal(new DateOnly(2028, 3, 1), Bills.Get(dni)!.NextDue);

        // A task edit must not delete the bills' rules (shared recurrence table).
        long t = Tasks.Add(new Tasks.Draft("x", DueDate: Today, Recurrence: new RecurrenceRule(Freq.Daily)), Today);
        Tasks.Update(Tasks.Get(t)! with { Recurrence = null });
        Assert.NotNull(Bills.Get(light)!.Recurrence);
        Assert.Throws<SqliteException>(() => Bills.Add(new Bill { Name = "Bad", Currency = "PESOS" }));
    }

    [Fact]
    public void Important_dates_and_notices()
    {
        Dates.Add("Cumple de Ana", "birthday", 11, 18, 1990, "Ana");
        Dates.Add("Leap", "birthday", 2, 29, 2000, remindDaysBefore: 0);
        Assert.Throws<SqliteException>(() => Dates.Add("Bad", "birthday", 4, 31));
        var up = Dates.Upcoming(Today, 7);
        Assert.Single(up);
        Assert.Equal(new DateOnly(2026, 11, 18), up[0].on);
        Assert.Equal(36, up[0].date.YearsOn(up[0].on));
        Assert.Equal(new DateOnly(2027, 2, 28), Dates.All().Single(d => d.Label == "Leap").Next(Today));   // 2027 is not a leap year
        Assert.Contains(Search.Run("ana"), h => h.Kind == "date");

        Bills.Add(new Bill { Name = "Gas", Currency = "ARS", NextDue = Today.AddDays(2), RemindDaysBefore = 3 });
        var due = Notices.Due(Today);
        Assert.Equal(2, due.Count);                // the bill (2 days) and Ana's birthday (3 days)
        Notices.MarkFired(due);
        Assert.Empty(Notices.Due(Today));
    }

    [Fact]
    public void Day_plan_keeps_three_in_order()
    {
        var ids = Enumerable.Range(1, 4).Select(i => Tasks.Add(new Tasks.Draft("T" + i), Today)).ToList();
        DayPlan.Set(Today, [ids[2], ids[0], ids[2], ids[1], ids[3]]);
        Assert.Equal(["T3", "T1", "T2"], DayPlan.Get(Today).Select(t => t.Title));
        Tasks.Delete(ids[0]);
        Assert.Equal(["T3", "T2"], DayPlan.Get(Today).Select(t => t.Title));
        Assert.Empty(DayPlan.Get(Today.AddDays(1)));
    }
}

[Collection("db")]
public class CloudBackupTests : IDisposable
{
    readonly TempDb db = new();
    string Cloud => Path.Combine(db.Dir, "OneDrive", "BuzzDesk Backups");
    public void Dispose() => db.Dispose();

    [Fact]
    public void Encrypted_roundtrip_wrong_passphrase_and_restore()
    {
        var today = new DateOnly(2026, 11, 15);
        Tasks.Add(new Tasks.Draft("Antes del backup"), today);
        Habits.Add("Leer");
        CloudBackup.Result r;
        using (var c = Db.Open()) r = CloudBackup.Run(c, Cloud, "correct horse battery", new DateTime(2026, 11, 15, 22, 0, 0));
        Assert.True(r.Encrypted);
        Assert.EndsWith(".bzdk", r.Path);
        var raw = File.ReadAllBytes(r.Path);
        Assert.DoesNotContain("SQLite format 3", System.Text.Encoding.ASCII.GetString(raw));        // nothing readable
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => CloudBackup.Inspect(r.Path, "wrong"));
        Assert.Empty(Directory.GetFiles(Cloud, ".*partial"));

        // Change data, then restore the copy.
        Tasks.Add(new Tasks.Draft("Después del backup"), today);
        var copy = CloudBackup.Inspect(r.Path, "correct horse battery");
        Assert.Equal(Migrator.All.Count, copy.SchemaVersion);
        Assert.Equal((1, 1), (copy.Tasks, copy.Habits));
        CloudBackup.Apply(copy, db.BackupDir);
        Assert.Equal(["Antes del backup"], Tasks.List(new TaskFilter(TaskScope.All), today).Select(t => t.Title));
        Assert.Single(Directory.GetFiles(db.BackupDir, "*-pre-restore.db"));
        using var c2 = Db.Open();
        Assert.Equal("ok", Db.Scalar<string>(c2, "PRAGMA integrity_check;"));
        Assert.Contains(Search.Run("antes"), h => h.Kind == "task");
    }

    [Fact]
    public void Plain_copy_and_a_foreign_file_is_refused()
    {
        CloudBackup.Result r;
        using (var c = Db.Open()) r = CloudBackup.Run(c, Cloud, null, new DateTime(2026, 11, 15, 8, 0, 0));
        Assert.EndsWith(".db", r.Path);
        Assert.Equal(Migrator.All.Count, CloudBackup.Inspect(r.Path, null).SchemaVersion);
        string other = Path.Combine(db.Dir, "other.db");
        using (var o = new SqliteConnection("Data Source=" + other + ";Pooling=False")) { o.Open(); Db.Exec(o, "CREATE TABLE x(a)"); }
        Assert.Throws<InvalidDataException>(() => CloudBackup.Inspect(other, null));
    }

    [Fact]
    public void Retention_keeps_daily_and_monthly()
    {
        Directory.CreateDirectory(Cloud);
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        for (int i = 0; i < 320; i++)
            foreach (int h in new[] { 9, 21 })
                File.WriteAllBytes(Path.Combine(Cloud, CloudBackup.Prefix + start.AddDays(i).Date.AddHours(h).ToString("yyyyMMdd-HHmmss") + ".bzdk"), [1]);
        CloudBackup.Prune(Cloud, 30, 12);
        var left = CloudBackup.List(Cloud);
        var recent = left.Where(x => x.when >= start.AddDays(290)).ToList();
        Assert.Equal(30, recent.Select(x => x.when.Date).Distinct().Count());                // last 30 days
        Assert.All(recent.GroupBy(x => x.when.Date), g => Assert.Contains(g, x => x.when.Hour == 21));   // the newest of each day
        Assert.All(recent.Where(x => x.when.Hour == 9), x => Assert.Equal(1, x.when.Day));   // a 9:00 copy only as a month's first
        Assert.Contains(left, x => x.when == new DateTime(2026, 3, 1, 9, 0, 0));             // oldest of March
        Assert.True(left.Count <= 30 + 12);
    }
}

[Collection("db")]
public class ExportTests : IDisposable
{
    readonly TempDb db = new();
    public void Dispose() => db.Dispose();

    [Theory]
    [InlineData("simple", ',', "simple")]
    [InlineData("a,b", ',', "\"a,b\"")]
    [InlineData("a;b", ',', "a;b")]
    [InlineData("say \"hi\"", ';', "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", ',', "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("-12.5", ',', "-12.5")]
    public void Quoting(string text, char sep, string expected) => Assert.Equal(expected, Export.Quote(text, sep));

    [Fact]
    public void Writes_every_file_for_excel()
    {
        var today = new DateOnly(2026, 11, 15);
        long t = Tasks.Add(new Tasks.Draft("Pagar; luz", DueDate: today, Tags: ["casa"], Notes: "línea 1\nlínea 2"), today);
        Tasks.Add(new Tasks.Draft("Sub", ParentId: t), today);
        long b = Bills.Add(new Bill { Name = "Luz", AmountCents = 1_234_550, Currency = "ARS", NextDue = today });
        Bills.Pay(b, null, today);
        string dir = Path.Combine(db.Dir, "export");
        var files = Export.All(dir, new Export.Options(';', utc => utc.AddHours(-3), ","));
        Assert.Equal(9, files.Count);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "tasks.csv"));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);                  // BOM: Excel reads UTF-8
        string tasks = File.ReadAllText(Path.Combine(dir, "tasks.csv"));
        Assert.StartsWith("id;parent;title;", tasks);
        Assert.Contains("\"Pagar; luz\"", tasks);
        Assert.Contains("\"línea 1\nlínea 2\"", tasks);
        Assert.Contains(";Pagar; luz;Sub;", tasks.Replace("\"", ""));                 // the subtask names its parent
        string payments = File.ReadAllText(Path.Combine(dir, "payments.csv"));
        Assert.Contains("12345,5;ARS", payments);                                    // decimal comma for Spanish Excel
    }
}

[Collection("db")]
public class UndoTests : IDisposable
{
    readonly TempDb db = new();
    public void Dispose() => db.Dispose();

    [Fact]
    public void Undo_complete_removes_the_created_instance()
    {
        var today = new DateOnly(2026, 11, 15);
        long id = Tasks.Add(new Tasks.Draft("Semanal", DueDate: today, Recurrence: new RecurrenceRule(Freq.Weekly)), today);
        Tasks.Add(new Tasks.Draft("paso", ParentId: id), today);
        long? next = Tasks.Complete(id, today);
        Assert.NotNull(next);
        Tasks.UndoComplete(id, next);
        var open = Tasks.List(new TaskFilter(TaskScope.Open), today);
        Assert.Equal([id], open.Select(t => t.Id));
        Assert.Null(Tasks.Get(next!.Value));
        Assert.Equal(1, Tasks.Get(id)!.SubtaskCount);
    }
}
