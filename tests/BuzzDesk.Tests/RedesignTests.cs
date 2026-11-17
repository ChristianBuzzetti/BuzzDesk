using BuzzDesk.Data;

namespace BuzzDesk.Tests;

/// <summary>The redesign's data: topics become tasks once, the sidebar counts, the calendar range and moving a task
/// to another day.</summary>
[Collection("db")]
public class RedesignTests : IDisposable
{
    readonly TempDb db = new();
    static readonly DateOnly Today = new(2026, 11, 16);
    public void Dispose() => db.Dispose();

    static int Convert() => Repo.Read(c => Redesign.ConvertTopics(c, s => s));

    [Fact]
    public void Open_topics_become_tasks_with_notes_tags_and_reminders()
    {
        long work = Sections.All().First().Id;
        long id = Topics.Add("Migración a SQL 2025", work, Today.AddDays(3), ["migracion"]);
        Topics.AddNote(id, "Pedido el ambiente a infraestructura");
        Topics.SetStatus(id, TopicState.Waiting, "Juan Pérez", "Esperando a Juan Pérez");
        long reminder = Reminders.Add(DateTime.UtcNow.AddDays(1), topicId: id);
        long closed = Topics.Add("Viejo");
        Topics.SetStatus(closed, TopicState.Closed, null, "Cerrado");

        Assert.Equal(1, Convert());
        Assert.Equal(0, Convert());   // only once

        var task = Assert.Single(Tasks.List(new TaskFilter(TaskScope.Open), Today));
        Assert.Equal("Migración a SQL 2025", task.Title);
        Assert.Equal(work, task.SectionId);
        Assert.Equal(Today.AddDays(3), task.DueDate);
        Assert.Contains("Esperando a Juan Pérez", task.Notes);
        Assert.Contains("Pedido el ambiente a infraestructura", task.Notes);
        Assert.Equal(["esperando", "migracion"], task.Tags);
        Assert.Contains(Reminders.ForTask(task.Id), r => r.Id == reminder);
        Assert.Equal(TopicState.Closed, Topics.Get(id)!.Status);
        Assert.Empty(Topics.List(TopicScope.Open, Today));
    }

    [Fact]
    public void Nav_counts_inbox_week_and_someday()
    {
        long work = Sections.All().First().Id;
        Tasks.Add(new Tasks.Draft("inbox, sin fecha"), Today);
        Tasks.Add(new Tasks.Draft("inbox, mañana", DueDate: Today.AddDays(1)), Today);
        Tasks.Add(new Tasks.Draft("trabajo, atrasado", work, DueDate: Today.AddDays(-2)), Today);
        Tasks.Add(new Tasks.Draft("trabajo, en 10 días", work, DueDate: Today.AddDays(10)), Today);
        Assert.Equal((2, 2, 1), Tasks.NavCounts(Today));
        Assert.Equal(2, Tasks.List(new TaskFilter(TaskScope.Inbox), Today).Count);
        Assert.Equal(2, Tasks.List(new TaskFilter(TaskScope.Week), Today).Count);
    }

    [Fact]
    public void Calendar_range_and_reschedule_move_reminders()
    {
        long a = Tasks.Add(new Tasks.Draft("A", DueDate: Today), Today);
        long b = Tasks.Add(new Tasks.Draft("B", DueDate: Today.AddDays(40)), Today);
        long done = Tasks.Add(new Tasks.Draft("hecho", DueDate: Today.AddDays(1)), Today);
        Tasks.Complete(done, Today);
        Assert.Equal(["A", "hecho"], Tasks.Between(Today, Today.AddDays(7)).Select(t => t.Title));
        Assert.Equal(["A"], Tasks.Between(Today, Today.AddDays(7), withDone: false).Select(t => t.Title));

        var at = DateTime.UtcNow.AddDays(1);
        Reminders.Add(at, taskId: a);
        Tasks.Reschedule(a, Today.AddDays(2));
        var moved = Tasks.Get(a)!;
        Assert.Equal(Today.AddDays(2), moved.DueDate);
        Assert.Equal(1, moved.PostponeCount);
        Assert.Equal(at.AddDays(2).ToString("yyyy-MM-dd HH:mm"), Reminders.ForTask(a).Single().DueUtc.ToString("yyyy-MM-dd HH:mm"));

        Tasks.Reschedule(b, Today.AddDays(5));   // earlier: not a postponement
        Assert.Equal(0, Tasks.Get(b)!.PostponeCount);
    }
}
