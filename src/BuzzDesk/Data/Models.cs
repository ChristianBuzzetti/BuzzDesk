using BuzzDesk.Domain;

namespace BuzzDesk.Data;

public sealed record Section(long Id, long? ParentId, string Name, string Kind, string? Color, int SortOrder, bool Archived, int OpenTasks)
{
    public bool IsArea => Kind == "area";
}

public sealed record Tag(long Id, string Name, string? Color);

public sealed record Person(long Id, string Name, string? Email);

public enum TaskState { Open, Done, Cancelled }

/// <summary>A task row (or a subtask when ParentId is set), with the bits lists need joined in.</summary>
public sealed record TaskItem
{
    public long Id { get; init; }
    public long? SectionId { get; init; }
    public long? ParentId { get; init; }
    public long? TopicId { get; init; }
    public long? RecurrenceId { get; init; }
    public string Title { get; init; } = "";
    public string? Notes { get; init; }
    public int Priority { get; init; }
    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }
    public TaskState Status { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public int PostponeCount { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public DateTime? DeletedUtc { get; init; }
    // joined
    public string? SectionName { get; init; }
    public string? TopicTitle { get; init; }
    public RecurrenceRule? Recurrence { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int SubtaskCount { get; init; }
    public int SubtaskDone { get; init; }

    public bool IsOpen => Status == TaskState.Open;
    public bool IsOverdue(DateOnly today) => IsOpen && DueDate is DateOnly d && d < today;

    public static string ToDb(TaskState s) => s switch { TaskState.Open => "open", TaskState.Done => "done", _ => "cancelled" };
    public static TaskState FromDb(string s) => s switch { "open" => TaskState.Open, "done" => TaskState.Done, _ => TaskState.Cancelled };
}

/// <summary>Which tasks a list shows.</summary>
public enum TaskScope { Open, Today, Upcoming, NoDate, Done, All, Inbox, Week }

public sealed record TaskFilter(TaskScope Scope = TaskScope.Open, long? SectionId = null, long? TagId = null, long? TopicId = null, string? Text = null, int Limit = 500);
