using CRM.Domain.Enums;

namespace CRM.Application.Services;

/// <summary>
/// Pure date maths for Repeat (Oct 2026), shared by calendar events and tasks. Occurrences are
/// computed on demand from the rule — there is no background job (Always On is off) and no
/// stored copies — so this is the single place that decides which days a series lands on.
/// </summary>
public static class RecurrenceRule
{
    public const int MaxInterval = 99;

    /// <summary>Hard ceiling on occurrences returned for one window; a month never comes close.</summary>
    public const int MaxPerWindow = 500;

    public readonly record struct Rule(RepeatFrequency Frequency, int Interval, int WeekdayMask, DateOnly? Until)
    {
        public bool Repeats => Frequency != RepeatFrequency.None;
        public int SafeInterval => Math.Clamp(Interval, 1, MaxInterval);
    }

    /// <summary>Sun=1, Mon=2, Tue=4 … Sat=64.</summary>
    public static int MaskOf(DayOfWeek d) => 1 << (int)d;

    public static int MaskOf(IEnumerable<int> weekdays) =>
        weekdays.Where(d => d is >= 0 and <= 6).Aggregate(0, (m, d) => m | (1 << d));

    public static List<int> WeekdaysOf(int mask) =>
        Enumerable.Range(0, 7).Where(d => (mask & (1 << d)) != 0).ToList();

    /// <summary>
    /// The occurrence dates of a series starting on <paramref name="start"/> that fall within
    /// [<paramref name="from"/>, <paramref name="to"/>], in order. The start itself is always the
    /// first occurrence. Monthly on the 29th–31st and yearly on Feb 29 land on the month's last
    /// day when the month is shorter. A non-repeating rule yields just the start (if in range).
    /// </summary>
    public static IEnumerable<DateOnly> Occurrences(DateOnly start, Rule rule, DateOnly from, DateOnly to)
    {
        if (to < from) yield break;
        var end = rule.Until is { } u && u < to ? u : to;
        if (end < start) yield break;

        if (!rule.Repeats)
        {
            if (start >= from && start <= end) yield return start;
            yield break;
        }

        var n = rule.SafeInterval;
        var emitted = 0;
        foreach (var d in Candidates(start, rule.Frequency, n, rule.WeekdayMask, from))
        {
            if (d > end) yield break;
            if (d < from || d < start) continue;
            yield return d;
            if (++emitted >= MaxPerWindow) yield break;
        }
    }

    /// <summary>The first occurrence strictly after <paramref name="after"/>, or null when the series has ended.</summary>
    public static DateOnly? NextAfter(DateOnly start, Rule rule, DateOnly after)
    {
        if (!rule.Repeats) return null;
        // Far enough ahead for the sparsest rule of each kind: every 99 months is ~8¼ years and
        // every 99 weeks under 2, so 10 years covers all but Yearly, which needs interval + 1.
        var years = rule.Frequency == RepeatFrequency.Yearly ? rule.SafeInterval + 1 : 10;
        if (after.Year + years > 9999) years = 9999 - after.Year;
        if (years <= 0) return null;
        foreach (var d in Occurrences(start, rule, after.AddDays(1), after.AddYears(years))) return d;
        return null;
    }

    // Candidate dates in increasing order, starting near `from` rather than at `start`, so a daily
    // series that began years ago doesn't walk every day since.
    private static IEnumerable<DateOnly> Candidates(DateOnly start, RepeatFrequency f, int n, int mask, DateOnly from)
    {
        switch (f)
        {
            case RepeatFrequency.Daily:
            {
                var k = from > start ? (from.DayNumber - start.DayNumber) / n : 0;
                for (; ; k++)
                {
                    var dayNumber = (long)start.DayNumber + (long)k * n;
                    if (dayNumber > DateOnly.MaxValue.DayNumber) yield break;
                    yield return DateOnly.FromDayNumber((int)dayNumber);
                }
            }
            case RepeatFrequency.Weekly:
            {
                if (mask == 0) mask = MaskOf(start.DayOfWeek);
                var weekStart = start.AddDays(-(int)start.DayOfWeek); // the Sunday of start's week
                var w = from > weekStart ? (from.DayNumber - weekStart.DayNumber) / 7 / n * n : 0;
                for (; ; w += n)
                {
                    var sunday = weekStart.DayNumber + (long)w * 7;
                    if (sunday + 6 > DateOnly.MaxValue.DayNumber) yield break;
                    for (var d = 0; d < 7; d++)
                        if ((mask & (1 << d)) != 0) yield return DateOnly.FromDayNumber((int)sunday + d);
                }
            }
            case RepeatFrequency.Monthly:
            {
                var monthsToFrom = (from.Year - start.Year) * 12 + from.Month - start.Month;
                var k = monthsToFrom > 0 ? Math.Max(0, monthsToFrom / n - 1) : 0;
                for (; ; k++)
                {
                    var total = start.Year * 12 + (start.Month - 1) + k * n;
                    var y = total / 12;
                    if (y > 9999) yield break;
                    yield return Clamp(y, total % 12 + 1, start.Day);
                }
            }
            case RepeatFrequency.Yearly:
            {
                var k = from.Year > start.Year ? Math.Max(0, (from.Year - start.Year) / n - 1) : 0;
                for (; ; k++)
                {
                    var y = start.Year + k * n;
                    if (y > 9999) yield break;
                    yield return Clamp(y, start.Month, start.Day);
                }
            }
            default:
                yield break;
        }
    }

    private static DateOnly Clamp(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>
    /// Validates a rule coming from a form (ArgumentException → 400). Returns the normalised rule:
    /// weekdays only matter for Weekly, interval clamped, an end before the start refused.
    /// </summary>
    public static Rule Validate(RepeatFrequency frequency, int? interval, IReadOnlyCollection<int>? weekdays, DateOnly? until, DateOnly start)
    {
        if (!Enum.IsDefined(frequency)) throw new ArgumentException("Unknown repeat option.");
        if (frequency == RepeatFrequency.None) return new Rule(RepeatFrequency.None, 1, 0, null);

        var n = interval ?? 1;
        if (n is < 1 or > MaxInterval) throw new ArgumentException($"Repeat every 1–{MaxInterval}.");
        if (until is { } u && u < start) throw new ArgumentException("The repeat can't end before it starts.");

        var mask = 0;
        if (frequency == RepeatFrequency.Weekly && weekdays is { Count: > 0 })
        {
            if (weekdays.Any(d => d is < 0 or > 6)) throw new ArgumentException("Weekdays run 0 (Sunday) to 6 (Saturday).");
            mask = MaskOf(weekdays);
        }
        return new Rule(frequency, n, mask, until);
    }
}
