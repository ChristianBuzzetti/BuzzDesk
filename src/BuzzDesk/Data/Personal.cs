using BuzzDesk.Domain;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

// ====================================================================== habits
public sealed record Habit(long Id, string Name, string? Icon, string? Color, int Target, int WeekdaysMask, int SortOrder, bool Archived,
    int TodayCount, int Streak, int BestStreak)
{
    public bool AppliesOn(DateOnly d) => (WeekdaysMask & RecurrenceRule.Bit(d.DayOfWeek)) != 0;
    public bool DoneToday => TodayCount >= Target;
}

/// <summary>Habits: a check per day, a daily target, the weekdays it applies. Streaks are computed in SQL
/// (gaps and islands over a generated calendar of the days the habit applies), never stored.</summary>
public static class Habits
{
    public static List<Habit> List(DateOnly today, bool includeArchived = false) => Repo.Read(c =>
    {
        var rows = Db.Query(c, """
            SELECT h.id, h.name, h.icon, h.color, h.target_per_day, h.weekdays_mask, h.sort_order, h.archived_utc,
                   coalesce((SELECT k.count FROM habit_check k WHERE k.habit_id = h.id AND k.day = $today), 0)
              FROM habit h
            """ + (includeArchived ? "" : " WHERE h.archived_utc IS NULL") + " ORDER BY h.archived_utc IS NOT NULL, h.sort_order, h.id",
            r => (id: r.GetInt64(0), name: r.GetString(1), icon: Db.Str(r, "icon"), color: Db.Str(r, "color"), target: (int)r.GetInt64(4),
                  mask: (int)r.GetInt64(5), order: (int)r.GetInt64(6), archived: !r.IsDBNull(7), today: (int)r.GetInt64(8)),
            ("$today", Db.Date(today)));
        return rows.Select(h =>
        {
            var (streak, best) = Streaks(c, h.id, h.target, h.mask, today);
            return new Habit(h.id, h.name, h.icon, h.color, h.target, h.mask, h.order, h.archived, h.today, streak, best);
        }).ToList();
    });

    /// <summary>Current and best streak over the last 400 days, counting only the days the habit applies. Today
    /// does not break a streak until it is over (a streak of 5 stays 5 until you check today in).</summary>
    public static (int current, int best) Streaks(SqliteConnection c, long habitId, int target, int mask, DateOnly today)
    {
        var islands = Db.Query(c, """
            WITH RECURSIVE cal(d) AS (
                SELECT date($today)
                UNION ALL SELECT date(d, '-1 day') FROM cal WHERE d > date($today, '-400 days')),
            applies AS (                                        -- days the habit applies (bit0 = Monday)
                SELECT d FROM cal WHERE ($mask >> ((CAST(strftime('%w', d) AS INTEGER) + 6) % 7)) & 1),
            marked AS (
                SELECT a.d, coalesce(k.count, 0) >= $target AS met, row_number() OVER (ORDER BY a.d) AS rn
                  FROM applies a LEFT JOIN habit_check k ON k.habit_id = $h AND k.day = a.d),
            islands AS (                                        -- consecutive met days share rn - row_number()
                SELECT d, rn - row_number() OVER (ORDER BY d) AS grp FROM marked WHERE met)
            SELECT count(*) AS len, max(d) AS last FROM islands GROUP BY grp ORDER BY last DESC
            """, r => (len: (int)r.GetInt64(0), last: Db.ParseDate(r.GetString(1))),
            ("$today", Db.Date(today)), ("$mask", mask), ("$target", target), ("$h", habitId));
        int best = islands.Count == 0 ? 0 : islands.Max(i => i.len);
        if (islands.Count == 0) return (0, 0);
        // The streak is alive if its island ends on the latest applicable day up to today, or on the one before
        // today when today applies but is not done yet.
        var latest = LastApplicable(mask, today, inclusive: true);
        var previous = LastApplicable(mask, today, inclusive: false);
        var top = islands[0];
        bool todayApplies = latest == today;
        int current = top.last == latest || (todayApplies && top.last == previous) ? top.len : 0;
        return (current, best);
    }

    static DateOnly LastApplicable(int mask, DateOnly today, bool inclusive)
    {
        var d = inclusive ? today : today.AddDays(-1);
        for (int i = 0; i < 8 && (mask & RecurrenceRule.Bit(d.DayOfWeek)) == 0; i++) d = d.AddDays(-1);
        return d;
    }

    public static long Add(string name, int target = 1, int weekdaysMask = 127, string? icon = null, string? color = null) => Repo.Write("habit", (c, _) =>
    {
        int order = (int)(Db.Scalar<long?>(c, "SELECT max(sort_order) FROM habit") ?? 0) + 1;
        return Db.Insert(c, "INSERT INTO habit(name, icon, color, target_per_day, weekdays_mask, sort_order, created_utc) VALUES ($n, $i, $c, $t, $m, $o, $now)",
            ("$n", name.Trim()), ("$i", icon), ("$c", color), ("$t", Math.Clamp(target, 1, 50)), ("$m", Math.Clamp(weekdaysMask, 1, 127)), ("$o", order), ("$now", Db.NowUtc()));
    });

    public static void Update(long id, string name, int target, int weekdaysMask, string? icon, string? color) => Repo.Write("habit", (c, _) =>
        Db.Exec(c, "UPDATE habit SET name = $n, target_per_day = $t, weekdays_mask = $m, icon = $i, color = $c WHERE id = $id",
            ("$n", name.Trim()), ("$t", Math.Clamp(target, 1, 50)), ("$m", Math.Clamp(weekdaysMask, 1, 127)), ("$i", icon), ("$c", color), ("$id", id)));

    public static void Archive(long id, bool archived) => Repo.Write("habit", (c, _) =>
        Db.Exec(c, "UPDATE habit SET archived_utc = $t WHERE id = $id", ("$t", archived ? Db.NowUtc() : null), ("$id", id)));

    /// <summary>Hard delete with its history (asked for explicitly; archiving keeps it).</summary>
    public static void Delete(long id) => Repo.Write("habit", (c, _) => Db.Exec(c, "DELETE FROM habit WHERE id = $id", ("$id", id)));

    /// <summary>One more (or one less) for a day; the count never exceeds 50 and 0 removes the day.</summary>
    public static int Mark(long habitId, DateOnly day, int delta = 1) => Repo.Write("habit", (c, _) =>
    {
        if (delta > 0)
            Db.Exec(c, "INSERT INTO habit_check(habit_id, day, count) VALUES ($h, $d, min($n, 50)) ON CONFLICT(habit_id, day) DO UPDATE SET count = min(count + $n, 50)",
                ("$h", habitId), ("$d", Db.Date(day)), ("$n", delta));
        else
        {
            Db.Exec(c, "UPDATE habit_check SET count = count + $n WHERE habit_id = $h AND day = $d AND count + $n >= 1", ("$h", habitId), ("$d", Db.Date(day)), ("$n", delta));
            Db.Exec(c, "DELETE FROM habit_check WHERE habit_id = $h AND day = $d AND changes() = 0", ("$h", habitId), ("$d", Db.Date(day)));
        }
        return (int)(Db.Scalar<long?>(c, "SELECT count FROM habit_check WHERE habit_id = $h AND day = $d", ("$h", habitId), ("$d", Db.Date(day))) ?? 0);
    });

    /// <summary>Counts per day for the heat map.</summary>
    public static Dictionary<DateOnly, int> History(long habitId, DateOnly from, DateOnly to) => Repo.Read(c =>
        Db.Query(c, "SELECT day, count FROM habit_check WHERE habit_id = $h AND day BETWEEN $f AND $t", r => (Db.ParseDate(r.GetString(0)), (int)r.GetInt64(1)),
            ("$h", habitId), ("$f", Db.Date(from)), ("$t", Db.Date(to))).ToDictionary(x => x.Item1, x => x.Item2));
}

// ====================================================================== checklists
public sealed record Checklist(long Id, string Name, string? Icon, int Items, int Done, DateTime UpdatedUtc);
public sealed record ChecklistItem(long Id, long ChecklistId, string Text, bool Done, int SortOrder);

/// <summary>Reusable lists (groceries, packing). Reusing a list unticks everything.</summary>
public static class Checklists
{
    public static List<Checklist> List() => Repo.Read(c => Db.Query(c, """
        SELECT l.id, l.name, l.icon,
               (SELECT count(*) FROM checklist_item i WHERE i.checklist_id = l.id),
               (SELECT count(*) FROM checklist_item i WHERE i.checklist_id = l.id AND i.done_utc IS NOT NULL),
               l.updated_utc
          FROM checklist l WHERE l.deleted_utc IS NULL ORDER BY l.sort_order, l.name COLLATE NOCASE
        """, r => new Checklist(r.GetInt64(0), r.GetString(1), Db.Str(r, "icon"), (int)r.GetInt64(3), (int)r.GetInt64(4), Db.ParseUtc(r.GetString(5)))));

    public static Checklist? Get(long id) => List().FirstOrDefault(l => l.Id == id);

    /// <summary>Open items first, in their order; ticked ones after.</summary>
    public static List<ChecklistItem> Items(long listId) => Repo.Read(c => Db.Query(c,
        "SELECT id, checklist_id, text, done_utc, sort_order FROM checklist_item WHERE checklist_id = $l ORDER BY done_utc IS NOT NULL, sort_order, id",
        r => new ChecklistItem(r.GetInt64(0), r.GetInt64(1), r.GetString(2), !r.IsDBNull(3), (int)r.GetInt64(4)), ("$l", listId)));

    public static long Add(string name) => Repo.Write("checklist", (c, _) => Create(c, name));

    static long Create(SqliteConnection c, string name)
    {
        string now = Db.NowUtc();
        int order = (int)(Db.Scalar<long?>(c, "SELECT max(sort_order) FROM checklist") ?? 0) + 1;
        return Db.Insert(c, "INSERT INTO checklist(name, sort_order, created_utc, updated_utc) VALUES ($n, $o, $now, $now)", ("$n", name.Trim()), ("$o", order), ("$now", now));
    }

    /// <summary>The live list with that name (case-insensitive), created when missing. For "#super milk, bread".</summary>
    public static long FindOrCreate(string name) => Repo.Write("checklist", (c, _) =>
        Db.Scalar<long?>(c, "SELECT id FROM checklist WHERE name = $n COLLATE NOCASE AND deleted_utc IS NULL", ("$n", name.Trim())) ?? Create(c, name));

    /// <summary>Adds items (one per text, blanks skipped). An item already on the list, ticked, is unticked instead.</summary>
    public static int AddItems(long listId, IEnumerable<string> texts) => Repo.Write("checklist", (c, _) =>
    {
        int added = 0;
        string now = Db.NowUtc();
        foreach (var raw in texts)
        {
            string text = raw.Trim();
            if (text.Length == 0) continue;
            if (Db.Exec(c, "UPDATE checklist_item SET done_utc = NULL WHERE checklist_id = $l AND text = $t COLLATE NOCASE", ("$l", listId), ("$t", text)) > 0) continue;
            int order = (int)(Db.Scalar<long?>(c, "SELECT max(sort_order) FROM checklist_item WHERE checklist_id = $l", ("$l", listId)) ?? 0) + 1;
            Db.Exec(c, "INSERT INTO checklist_item(checklist_id, text, sort_order, created_utc) VALUES ($l, $t, $o, $now)", ("$l", listId), ("$t", text), ("$o", order), ("$now", now));
            added++;
        }
        Touch(c, listId);
        return added;
    });

    public static void Toggle(long itemId) => Repo.Write("checklist", (c, _) =>
    {
        Db.Exec(c, "UPDATE checklist_item SET done_utc = CASE WHEN done_utc IS NULL THEN $now END WHERE id = $id", ("$now", Db.NowUtc()), ("$id", itemId));
        Touch(c, Db.Scalar<long>(c, "SELECT checklist_id FROM checklist_item WHERE id = $id", ("$id", itemId)));
    });

    public static void RenameItem(long itemId, string text) => Repo.Write("checklist", (c, _) => Db.Exec(c, "UPDATE checklist_item SET text = $t WHERE id = $id", ("$t", text.Trim()), ("$id", itemId)));
    public static void DeleteItem(long itemId) => Repo.Write("checklist", (c, _) => Db.Exec(c, "DELETE FROM checklist_item WHERE id = $id", ("$id", itemId)));

    /// <summary>Unticks every item (use the list again).</summary>
    public static void Reset(long listId) => Repo.Write("checklist", (c, _) => { Db.Exec(c, "UPDATE checklist_item SET done_utc = NULL WHERE checklist_id = $l", ("$l", listId)); Touch(c, listId); });
    /// <summary>Removes the ticked items (a one-off list).</summary>
    public static void ClearDone(long listId) => Repo.Write("checklist", (c, _) => { Db.Exec(c, "DELETE FROM checklist_item WHERE checklist_id = $l AND done_utc IS NOT NULL", ("$l", listId)); Touch(c, listId); });

    public static void Rename(long listId, string name) => Repo.Write("checklist", (c, _) => Db.Exec(c, "UPDATE checklist SET name = $n, updated_utc = $now WHERE id = $id", ("$n", name.Trim()), ("$now", Db.NowUtc()), ("$id", listId)));
    public static void Delete(long listId) => Repo.Write("checklist", (c, _) => Db.Exec(c, "UPDATE checklist SET deleted_utc = $now WHERE id = $id", ("$now", Db.NowUtc()), ("$id", listId)));
    public static bool Restore(long listId) => Repo.Write("checklist", (c, _) =>
    {
        try { return Db.Exec(c, "UPDATE checklist SET deleted_utc = NULL WHERE id = $id", ("$id", listId)) == 1; }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return false; }
    });

    static void Touch(SqliteConnection c, long listId) => Db.Exec(c, "UPDATE checklist SET updated_utc = $now WHERE id = $id", ("$now", Db.NowUtc()), ("$id", listId));
}

// ====================================================================== bills
public sealed record Bill
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Category { get; init; }
    public string Kind { get; init; } = "payment";   // payment | renewal
    public long? AmountCents { get; init; }
    public string Currency { get; init; } = "ARS";
    public RecurrenceRule? Recurrence { get; init; }
    public DateOnly? NextDue { get; init; }
    public int RemindDaysBefore { get; init; } = 3;
    public string? Notes { get; init; }
    public bool Archived { get; init; }
    public DateTime? LastPaidUtc { get; init; }
    public bool IsRenewal => Kind == "renewal";
    public bool Overdue(DateOnly today) => NextDue is DateOnly d && d < today;
    public bool DueSoon(DateOnly today) => NextDue is DateOnly d && d.DayNumber - today.DayNumber <= RemindDaysBefore;
}

public sealed record BillPayment(long Id, long BillId, string BillName, DateOnly DueDate, DateTime PaidUtc, long? AmountCents, string Currency);

/// <summary>Bills and renewals: the next due date, "Paid" (records the payment and moves to the next due date of
/// its rule) and what was paid in a month.</summary>
public static class Bills
{
    /// <summary>Yearly = monthly every 12 months on the same day (no change to the recurrence table).</summary>
    public static RecurrenceRule Yearly(DateOnly anchor) => new(Freq.Monthly, 12, MonthDay: anchor.Day);
    public static bool IsYearly(RecurrenceRule? r) => r is { Freq: Freq.Monthly, Interval: 12 };

    const string Select = """
        SELECT b.id, b.name, b.category, b.kind, b.amount_cents, b.currency, b.next_due_date, b.remind_days_before, b.notes, b.archived_utc,
               r.freq, r.interval, r.weekdays_mask, r.month_day, r.until_date,
               (SELECT max(p.paid_utc) FROM bill_payment p WHERE p.bill_id = b.id) AS last_paid
          FROM bill b LEFT JOIN recurrence r ON r.id = b.recurrence_id
        """;

    static Bill Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), Name = r.GetString(1), Category = Db.Str(r, "category"), Kind = r.GetString(3), AmountCents = Db.Long(r, "amount_cents"),
        Currency = r.GetString(5), NextDue = Db.DateOrNull(r, "next_due_date"), RemindDaysBefore = (int)r.GetInt64(7), Notes = Db.Str(r, "notes"),
        Archived = !r.IsDBNull(9),
        Recurrence = Db.Str(r, "freq") is string f
            ? new RecurrenceRule(RecurrenceRule.FromDb(f), (int)r.GetInt64(11), r.IsDBNull(12) ? null : (int)r.GetInt64(12), r.IsDBNull(13) ? null : (int)r.GetInt64(13), Db.DateOrNull(r, "until_date"))
            : null,
        LastPaidUtc = Db.UtcOrNull(r, "last_paid"),
    };

    /// <summary>Live bills, the next to come due first (no date last).</summary>
    public static List<Bill> List(bool includeArchived = false) => Repo.Read(c => Db.Query(c, Select +
        (includeArchived ? "" : " WHERE b.archived_utc IS NULL") + " ORDER BY b.next_due_date IS NULL, b.next_due_date, b.name COLLATE NOCASE", Map));

    public static Bill? Get(long id) => Repo.Read(c => Db.Query(c, Select + " WHERE b.id = $id", Map, ("$id", id)).FirstOrDefault());

    public static long Add(Bill b) => Repo.Write("bill", (c, _) =>
    {
        string now = Db.NowUtc();
        long? rule = b.Recurrence is RecurrenceRule r ? Tasks.InsertRule(c, r.Normalized(b.NextDue ?? DateOnly.FromDateTime(DateTime.Today))) : null;
        return Db.Insert(c, """
            INSERT INTO bill(name, category, kind, amount_cents, currency, recurrence_id, next_due_date, remind_days_before, notes, created_utc, updated_utc)
            VALUES ($n, $cat, $k, $a, $cur, $r, $due, $rem, $notes, $now, $now)
            """, ("$n", b.Name.Trim()), ("$cat", Blank(b.Category)), ("$k", b.Kind), ("$a", b.AmountCents), ("$cur", b.Currency.ToUpperInvariant()), ("$r", rule),
            ("$due", Db.Value(b.NextDue)), ("$rem", Math.Clamp(b.RemindDaysBefore, 0, 60)), ("$notes", Blank(b.Notes)), ("$now", now));
    });

    public static void Update(Bill b) => Repo.Write("bill", (c, _) =>
    {
        var current = Db.Query(c, Select + " WHERE b.id = $id", Map, ("$id", b.Id)).Single();
        long? rule = Db.Scalar<long?>(c, "SELECT recurrence_id FROM bill WHERE id = $id", ("$id", b.Id));
        if (b.Recurrence == null) rule = null;
        else if (b.Recurrence != current.Recurrence || rule == null) rule = Tasks.InsertRule(c, b.Recurrence.Normalized(b.NextDue ?? DateOnly.FromDateTime(DateTime.Today)));
        Db.Exec(c, """
            UPDATE bill SET name = $n, category = $cat, kind = $k, amount_cents = $a, currency = $cur, recurrence_id = $r, next_due_date = $due,
                            remind_days_before = $rem, notes = $notes, updated_utc = $now
             WHERE id = $id
            """, ("$n", b.Name.Trim()), ("$cat", Blank(b.Category)), ("$k", b.Kind), ("$a", b.AmountCents), ("$cur", b.Currency.ToUpperInvariant()), ("$r", rule),
            ("$due", Db.Value(b.NextDue)), ("$rem", Math.Clamp(b.RemindDaysBefore, 0, 60)), ("$notes", Blank(b.Notes)), ("$now", Db.NowUtc()), ("$id", b.Id));
        Recurrences.PruneUnused(c);
    });

    /// <summary>Records the payment of the current due date and moves the bill to its next one (or leaves it with
    /// no date when it doesn't repeat). Returns the payment id.</summary>
    public static long Pay(long billId, long? amountCents, DateOnly today) => Repo.Write("bill", (c, _) =>
    {
        var b = Db.Query(c, Select + " WHERE b.id = $id", Map, ("$id", billId)).Single();
        var due = b.NextDue ?? today;
        long id = Db.Insert(c, "INSERT INTO bill_payment(bill_id, due_date, paid_utc, amount_cents, currency) VALUES ($b, $d, $now, $a, $cur)",
            ("$b", billId), ("$d", Db.Date(due)), ("$now", Db.NowUtc()), ("$a", amountCents ?? b.AmountCents), ("$cur", b.Currency));
        var next = b.Recurrence?.Next(due);
        Db.Exec(c, "UPDATE bill SET next_due_date = $n, updated_utc = $now WHERE id = $id", ("$n", Db.Value(next)), ("$now", Db.NowUtc()), ("$id", billId));
        return id;
    });

    /// <summary>Undo a payment: removes it and puts its due date back.</summary>
    public static void Unpay(long paymentId) => Repo.Write("bill", (c, _) =>
    {
        var row = Db.Query(c, "SELECT bill_id, due_date FROM bill_payment WHERE id = $id", r => (r.GetInt64(0), r.GetString(1)), ("$id", paymentId)).SingleOrDefault();
        if (row == default) return;
        Db.Exec(c, "DELETE FROM bill_payment WHERE id = $id", ("$id", paymentId));
        Db.Exec(c, "UPDATE bill SET next_due_date = $d, updated_utc = $now WHERE id = $b", ("$d", row.Item2), ("$now", Db.NowUtc()), ("$b", row.Item1));
    });

    /// <summary>Payments whose due date falls in a month.</summary>
    public static List<BillPayment> Payments(int year, int month) => Repo.Read(c => Db.Query(c, """
        SELECT p.id, p.bill_id, b.name, p.due_date, p.paid_utc, p.amount_cents, p.currency
          FROM bill_payment p JOIN bill b ON b.id = p.bill_id
         WHERE p.due_date >= $from AND p.due_date < $to ORDER BY p.due_date, b.name
        """, r => new BillPayment(r.GetInt64(0), r.GetInt64(1), r.GetString(2), Db.ParseDate(r.GetString(3)), Db.ParseUtc(r.GetString(4)), Db.Long(r, "amount_cents"), r.GetString(6)),
        ("$from", Db.Date(new DateOnly(year, month, 1))), ("$to", Db.Date(new DateOnly(year, month, 1).AddMonths(1)))));

    public static List<BillPayment> History(long billId, int limit = 24) => Repo.Read(c => Db.Query(c, """
        SELECT p.id, p.bill_id, b.name, p.due_date, p.paid_utc, p.amount_cents, p.currency
          FROM bill_payment p JOIN bill b ON b.id = p.bill_id WHERE p.bill_id = $b ORDER BY p.due_date DESC LIMIT $n
        """, r => new BillPayment(r.GetInt64(0), r.GetInt64(1), r.GetString(2), Db.ParseDate(r.GetString(3)), Db.ParseUtc(r.GetString(4)), Db.Long(r, "amount_cents"), r.GetString(6)),
        ("$b", billId), ("$n", limit)));

    /// <summary>A month in numbers, per currency: paid, and still to pay (due in the month, live bills).</summary>
    public static List<(string currency, long paid, long pending)> MonthTotals(int year, int month) => Repo.Read(c => Db.Query(c, """
        WITH m AS (SELECT $from AS f, $to AS t),
        paid AS (SELECT p.currency, sum(coalesce(p.amount_cents, 0)) AS amt FROM bill_payment p, m WHERE p.due_date >= m.f AND p.due_date < m.t GROUP BY p.currency),
        pending AS (SELECT b.currency, sum(coalesce(b.amount_cents, 0)) AS amt FROM bill b, m
                     WHERE b.archived_utc IS NULL AND b.kind = 'payment' AND b.next_due_date >= m.f AND b.next_due_date < m.t GROUP BY b.currency)
        SELECT cur, coalesce((SELECT amt FROM paid WHERE paid.currency = cur), 0), coalesce((SELECT amt FROM pending WHERE pending.currency = cur), 0)
          FROM (SELECT currency AS cur FROM paid UNION SELECT currency FROM pending) ORDER BY cur
        """, r => (r.GetString(0), r.GetInt64(1), r.GetInt64(2)),
        ("$from", Db.Date(new DateOnly(year, month, 1))), ("$to", Db.Date(new DateOnly(year, month, 1).AddMonths(1)))));

    public static void Archive(long id, bool archived) => Repo.Write("bill", (c, _) =>
        Db.Exec(c, "UPDATE bill SET archived_utc = $t, updated_utc = $now WHERE id = $id", ("$t", archived ? Db.NowUtc() : null), ("$now", Db.NowUtc()), ("$id", id)));

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ====================================================================== important dates
public sealed record ImportantDate(long Id, long? PersonId, string? PersonName, string Label, string Kind, int Month, int Day, int? Year, int RemindDaysBefore)
{
    /// <summary>The next time it comes, today included (29/2 → 28/2 in common years).</summary>
    public DateOnly Next(DateOnly today)
    {
        var d = On(today.Year);
        return d >= today ? d : On(today.Year + 1);
    }
    public DateOnly On(int year) => new(year, Month, Math.Min(Day, DateTime.DaysInMonth(year, Month)));
    /// <summary>Years turned on the next occurrence (birthday age, anniversary number), if the year is known.</summary>
    public int? YearsOn(DateOnly when) => Year is int y ? when.Year - y : null;
}

public static class Dates
{
    public static List<ImportantDate> All() => Repo.Read(c => Db.Query(c, """
        SELECT x.id, x.person_id, p.name, x.label, x.kind, x.month, x.day, x.year, x.remind_days_before
          FROM important_date x LEFT JOIN person p ON p.id = x.person_id ORDER BY x.month, x.day, x.label
        """, r => new ImportantDate(r.GetInt64(0), Db.Long(r, "person_id"), Db.Str(r, "name"), r.GetString(3), r.GetString(4),
            (int)r.GetInt64(5), (int)r.GetInt64(6), r.IsDBNull(7) ? null : (int)r.GetInt64(7), (int)r.GetInt64(8))));

    /// <summary>Coming within <paramref name="days"/> days (today included), soonest first.</summary>
    public static List<(ImportantDate date, DateOnly on)> Upcoming(DateOnly today, int days) =>
        All().Select(d => (d, on: d.Next(today))).Where(x => x.on.DayNumber - today.DayNumber <= days).OrderBy(x => x.on).ThenBy(x => x.d.Label).ToList();

    public static long Add(string label, string kind, int month, int day, int? year = null, string? person = null, int remindDaysBefore = 3) => Repo.Write("date", (c, _) =>
    {
        long? personId = string.IsNullOrWhiteSpace(person) ? null : People.Ensure(c, person);
        return Db.Insert(c, "INSERT INTO important_date(person_id, label, kind, month, day, year, remind_days_before, created_utc) VALUES ($p, $l, $k, $m, $d, $y, $r, $now)",
            ("$p", personId), ("$l", label.Trim()), ("$k", kind), ("$m", month), ("$d", day), ("$y", year), ("$r", Math.Clamp(remindDaysBefore, 0, 60)), ("$now", Db.NowUtc()));
    });

    public static void Update(ImportantDate x, string? person) => Repo.Write("date", (c, _) =>
    {
        long? personId = string.IsNullOrWhiteSpace(person) ? null : People.Ensure(c, person);
        Db.Exec(c, "UPDATE important_date SET person_id = $p, label = $l, kind = $k, month = $m, day = $d, year = $y, remind_days_before = $r WHERE id = $id",
            ("$p", personId), ("$l", x.Label.Trim()), ("$k", x.Kind), ("$m", x.Month), ("$d", x.Day), ("$y", x.Year), ("$r", Math.Clamp(x.RemindDaysBefore, 0, 60)), ("$id", x.Id));
    });

    public static void Delete(long id) => Repo.Write("date", (c, _) => Db.Exec(c, "DELETE FROM important_date WHERE id = $id", ("$id", id)));
}

// ====================================================================== day plan
/// <summary>The three things that matter most today (chosen with the "Plan my day" button).</summary>
public static class DayPlan
{
    public static List<TaskItem> Get(DateOnly day)
    {
        var ids = Repo.Read(c => Db.Query(c, "SELECT task_id FROM day_focus WHERE day = $d ORDER BY position", r => r.GetInt64(0), ("$d", Db.Date(day))));
        return ids.Select(Tasks.Get).OfType<TaskItem>().Where(t => t.DeletedUtc == null).ToList();
    }

    /// <summary>Replaces the day's plan with up to three tasks, in order.</summary>
    public static void Set(DateOnly day, IReadOnlyList<long> taskIds) => Repo.Write("plan", (c, _) =>
    {
        Db.Exec(c, "DELETE FROM day_focus WHERE day = $d", ("$d", Db.Date(day)));
        int pos = 1;
        foreach (long id in taskIds.Distinct().Take(3))
            Db.Exec(c, "INSERT INTO day_focus(day, position, task_id) VALUES ($d, $p, $t)", ("$d", Db.Date(day)), ("$p", pos++), ("$t", id));
    });
}

// ====================================================================== one-a-day notices
/// <summary>Bills coming due and important dates coming up, each announced once per occurrence.</summary>
public static class Notices
{
    public sealed record Notice(string Kind, long RefId, DateOnly ForDate, string Title, int DaysLeft);

    public static List<Notice> Due(DateOnly today)
    {
        var list = new List<Notice>();
        foreach (var b in Bills.List())
            if (b.NextDue is DateOnly d && d.DayNumber - today.DayNumber <= b.RemindDaysBefore)
                list.Add(new Notice("bill", b.Id, d, b.Name, d.DayNumber - today.DayNumber));
        foreach (var (x, on) in Dates.Upcoming(today, 60))
            if (on.DayNumber - today.DayNumber <= x.RemindDaysBefore)
                list.Add(new Notice("date", x.Id, on, x.Label, on.DayNumber - today.DayNumber));
        var fired = Repo.Read(c => Db.Query(c, "SELECT kind, ref_id, for_date FROM notice_log WHERE for_date >= $f", r => (r.GetString(0), r.GetInt64(1), r.GetString(2)),
            ("$f", Db.Date(today.AddDays(-60))))).ToHashSet();
        return list.Where(n => !fired.Contains((n.Kind, n.RefId, Db.Date(n.ForDate)))).ToList();
    }

    public static void MarkFired(IEnumerable<Notice> notices) => Repo.Write("notice", (c, _) =>
    {
        foreach (var n in notices)
            Db.Exec(c, "INSERT INTO notice_log(kind, ref_id, for_date, fired_utc) VALUES ($k, $r, $d, $now) ON CONFLICT DO NOTHING",
                ("$k", n.Kind), ("$r", n.RefId), ("$d", Db.Date(n.ForDate)), ("$now", Db.NowUtc()));
    });
}
