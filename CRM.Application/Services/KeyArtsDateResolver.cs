using System.Globalization;
using System.Text.RegularExpressions;

namespace CRM.Application.Services;

/// <summary>
/// Turns a key arts date's free-text <c>DateText</c> into real days of a given year so the
/// calendar can place it (Oct 2026). The source sheet writes dates every which way:
/// "Mar 27", "3/27", "9/15-10/15", "12/26-1/1", "3rd Mon", "Last Fri", "Full month", "Varies".
/// Anything it can't read resolves to null and is shown in the month banner only — never
/// guessed onto a day.
/// </summary>
public static partial class KeyArtsDateResolver
{
    public readonly record struct Resolved(DateOnly? Start, DateOnly? End)
    {
        public static readonly Resolved None = new(null, null);
        public bool IsResolved => Start is not null;
        /// <summary>Spans the whole month (or more): banner material, not a day chip.</summary>
        public bool IsMonthLong => Start is { } s && End is { } e && e.DayNumber - s.DayNumber >= 27;
    }

    private static readonly string[] MonthNames =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    [GeneratedRegex(@"^(?<a>\d{1,2})/(?<b>\d{1,2})(?:\s*[-–—]\s*(?<c>\d{1,2})/(?<d>\d{1,2}))?$")]
    private static partial Regex Numeric();

    [GeneratedRegex(@"^(?<mon>[A-Za-z]{3,9})\.?\s+(?<a>\d{1,2})(?:\s*[-–—]\s*(?:(?<mon2>[A-Za-z]{3,9})\.?\s+)?(?<b>\d{1,2}))?$")]
    private static partial Regex Named();

    [GeneratedRegex(@"^(?<n>1st|2nd|3rd|4th|5th|first|second|third|fourth|fifth|last)\s+(?<dow>[A-Za-z]{3,9})\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex Ordinal();

    [GeneratedRegex(@"^(?<a>\d{1,2})(?:\s*[-–—]\s*(?<b>\d{1,2}))?$")]
    private static partial Regex BareDays();

    public static Resolved Resolve(string? dateText, int month, int year)
    {
        if (month is < 1 or > 12) return Resolved.None;
        var text = (dateText ?? string.Empty).Trim();
        if (text.Length == 0) return Resolved.None;
        var lower = text.ToLowerInvariant();

        if (lower is "full month" or "all month" or "whole month" or "month" or "entire month")
            return new Resolved(new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

        if (Numeric().Match(text) is { Success: true } num)
        {
            var start = Day(year, Int(num, "a"), Int(num, "b"));
            if (start is null) return Resolved.None;
            if (!num.Groups["c"].Success) return new Resolved(start, start);
            var endMonth = Int(num, "c");
            // "12/26-1/1": a range that wraps into the next year.
            var endYear = endMonth < start.Value.Month ? year + 1 : year;
            var end = Day(endYear, endMonth, Int(num, "d"));
            return end is null ? Resolved.None : new Resolved(start, end);
        }

        if (Named().Match(text) is { Success: true } named)
        {
            var m1 = MonthIndex(named.Groups["mon"].Value);
            if (m1 is null) return Resolved.None;
            var start = Day(year, m1.Value, Int(named, "a"));
            if (start is null) return Resolved.None;
            if (!named.Groups["b"].Success) return new Resolved(start, start);
            var m2 = named.Groups["mon2"].Success ? MonthIndex(named.Groups["mon2"].Value) : m1;
            if (m2 is null) return Resolved.None;
            var end = Day(m2 < m1 ? year + 1 : year, m2.Value, Int(named, "b"));
            return end is null ? Resolved.None : new Resolved(start, end);
        }

        if (Ordinal().Match(text) is { Success: true } ord)
        {
            var dow = DayOfWeekIndex(ord.Groups["dow"].Value);
            if (dow is null) return Resolved.None;
            var day = NthWeekday(year, month, dow.Value, ord.Groups["n"].Value.ToLowerInvariant());
            return day is null ? Resolved.None : new Resolved(day, day);
        }

        if (BareDays().Match(text) is { Success: true } bare)
        {
            var start = Day(year, month, Int(bare, "a"));
            if (start is null) return Resolved.None;
            if (!bare.Groups["b"].Success) return new Resolved(start, start);
            var end = Day(year, month, Int(bare, "b"));
            return end is null || end < start ? Resolved.None : new Resolved(start, end);
        }

        return Resolved.None; // "Varies", "TBD", "Spring", prose…
    }

    private static int Int(Match m, string group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    /// <summary>A real date, with the day clamped to the month (Feb 29 in a non-leap year → Feb 28).</summary>
    private static DateOnly? Day(int year, int month, int day)
    {
        if (month is < 1 or > 12 || day < 1) return null;
        return new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    private static int? MonthIndex(string name)
    {
        var key = name.ToLowerInvariant();
        for (var i = 0; i < 12; i++)
            if (key.StartsWith(MonthNames[i], StringComparison.Ordinal)) return i + 1;
        return null;
    }

    private static DayOfWeek? DayOfWeekIndex(string name)
    {
        var key = name.ToLowerInvariant();
        foreach (var dow in Enum.GetValues<DayOfWeek>())
            if (key.StartsWith(dow.ToString()[..3].ToLowerInvariant(), StringComparison.Ordinal)) return dow;
        return null;
    }

    private static DateOnly? NthWeekday(int year, int month, DayOfWeek dow, string ordinal)
    {
        var days = DateTime.DaysInMonth(year, month);
        if (ordinal == "last")
        {
            for (var d = days; d >= 1; d--)
                if (new DateOnly(year, month, d).DayOfWeek == dow) return new DateOnly(year, month, d);
            return null;
        }
        var n = ordinal switch
        {
            "1st" or "first" => 1, "2nd" or "second" => 2, "3rd" or "third" => 3,
            "4th" or "fourth" => 4, "5th" or "fifth" => 5, _ => 0,
        };
        if (n == 0) return null;
        var count = 0;
        for (var d = 1; d <= days; d++)
        {
            if (new DateOnly(year, month, d).DayOfWeek != dow) continue;
            if (++count == n) return new DateOnly(year, month, d);
        }
        return null; // a 5th Monday that month doesn't have
    }
}
