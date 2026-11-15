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
