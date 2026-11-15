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
