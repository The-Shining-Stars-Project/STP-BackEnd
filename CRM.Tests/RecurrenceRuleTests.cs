using CRM.Application.Services;
using CRM.Domain.Enums;
using Xunit;
using Rule = CRM.Application.Services.RecurrenceRule.Rule;

namespace CRM.Tests;

/// <summary>
/// Repeat (Oct 2026): occurrences are computed from the rule on demand, never stored, so these
/// pin exactly which days each kind of rule lands on.
/// </summary>
public class RecurrenceRuleTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static List<string> Occ(DateOnly start, Rule rule, DateOnly from, DateOnly to) =>
        RecurrenceRule.Occurrences(start, rule, from, to).Select(d => d.ToString("MM-dd")).ToList();

    private static Rule R(RepeatFrequency f, int interval = 1, int mask = 0, DateOnly? until = null) => new(f, interval, mask, until);

    [Fact]
    public void A_one_off_is_just_its_own_day()
    {
        Assert.Equal(new[] { "10-26" }, Occ(D(2026, 10, 26), R(RepeatFrequency.None), D(2026, 10, 1), D(2026, 10, 31)));
        Assert.Empty(Occ(D(2026, 10, 26), R(RepeatFrequency.None), D(2026, 11, 1), D(2026, 11, 30)));
    }

    [Fact]
    public void Daily_every_other_day_from_a_start_long_before_the_window()
    {
        // Started Jan 1 2024, every 2 days: computed by jumping, not by walking two years of days.
        var days = Occ(D(2024, 1, 1), R(RepeatFrequency.Daily, 2), D(2026, 10, 1), D(2026, 10, 9));
        Assert.Equal(new[] { "10-01", "10-03", "10-05", "10-07", "10-09" }, days); // 2024-01-01 + 1004 days is Oct 1 2026
    }

    [Fact]
    public void Weekly_on_the_starts_weekday_when_none_are_picked()
    {
        // Mon Oct 5 2026, weekly.
        Assert.Equal(new[] { "10-05", "10-12", "10-19", "10-26" }, Occ(D(2026, 10, 5), R(RepeatFrequency.Weekly), D(2026, 10, 1), D(2026, 10, 31)));
    }

    [Fact]
    public void Weekly_on_picked_weekdays_every_two_weeks_never_before_the_start()
    {
        // Starts Wed Oct 7; Mon + Wed every 2 weeks. Mon Oct 5 is in the start's week but before it.
        var mask = RecurrenceRule.MaskOf(new[] { 1, 3 });
        Assert.Equal(new[] { "10-07", "10-19", "10-21" }, Occ(D(2026, 10, 7), R(RepeatFrequency.Weekly, 2, mask), D(2026, 10, 1), D(2026, 10, 25)));
    }

    [Fact]
    public void Monthly_on_the_31st_lands_on_each_months_last_day_without_drifting()
    {
        var months = RecurrenceRule.Occurrences(D(2026, 1, 31), R(RepeatFrequency.Monthly), D(2026, 1, 1), D(2026, 4, 30)).ToList();
        Assert.Equal(new[] { D(2026, 1, 31), D(2026, 2, 28), D(2026, 3, 31), D(2026, 4, 30) }, months);
    }

    [Fact]
    public void Monthly_every_three_months_and_a_window_far_from_the_start()
    {
        Assert.Equal(new[] { "01-15", "04-15", "07-15", "10-15" }, Occ(D(2020, 1, 15), R(RepeatFrequency.Monthly, 3), D(2026, 1, 1), D(2026, 12, 31)));
    }

    [Fact]
    public void Yearly_on_Feb_29_uses_Feb_28_in_ordinary_years()
    {
        var years = RecurrenceRule.Occurrences(D(2028, 2, 29), R(RepeatFrequency.Yearly), D(2028, 1, 1), D(2032, 12, 31)).ToList();
        Assert.Equal(new[] { D(2028, 2, 29), D(2029, 2, 28), D(2030, 2, 28), D(2031, 2, 28), D(2032, 2, 29) }, years);
    }

    [Fact]
    public void Until_is_inclusive_and_cuts_the_series()
    {
        var rule = R(RepeatFrequency.Weekly, until: D(2026, 10, 19));
        Assert.Equal(new[] { "10-05", "10-12", "10-19" }, Occ(D(2026, 10, 5), rule, D(2026, 10, 1), D(2026, 10, 31)));
        Assert.Empty(Occ(D(2026, 10, 5), rule, D(2026, 11, 1), D(2026, 11, 30)));
    }

    [Fact]
    public void A_window_before_the_start_is_empty_and_the_cap_holds()
    {
        Assert.Empty(Occ(D(2026, 10, 5), R(RepeatFrequency.Daily), D(2026, 9, 1), D(2026, 9, 30)));
        var many = RecurrenceRule.Occurrences(D(2026, 1, 1), R(RepeatFrequency.Daily), D(2026, 1, 1), D(2030, 12, 31)).Count();
        Assert.Equal(RecurrenceRule.MaxPerWindow, many);
    }

    [Fact]
    public void Next_after_skips_to_the_following_occurrence_and_stops_at_until()
    {
        var weekly = R(RepeatFrequency.Weekly, mask: RecurrenceRule.MaskOf(new[] { 1, 3 })); // Mon + Wed
        Assert.Equal(D(2026, 10, 7), RecurrenceRule.NextAfter(D(2026, 10, 5), weekly, D(2026, 10, 5)));
        Assert.Equal(D(2026, 10, 12), RecurrenceRule.NextAfter(D(2026, 10, 5), weekly, D(2026, 10, 7)));

        Assert.Null(RecurrenceRule.NextAfter(D(2026, 10, 5), R(RepeatFrequency.Weekly, until: D(2026, 10, 12)), D(2026, 10, 12)));
        Assert.Null(RecurrenceRule.NextAfter(D(2026, 10, 5), R(RepeatFrequency.None), D(2026, 10, 5)));
        Assert.Equal(D(2076, 3, 1), RecurrenceRule.NextAfter(D(2026, 3, 1), R(RepeatFrequency.Yearly, 50), D(2026, 3, 1)));
    }

    [Fact]
    public void Validate_normalises_the_rule_and_refuses_nonsense()
    {
        var start = D(2026, 10, 5);
        Assert.Equal(0, RecurrenceRule.Validate(RepeatFrequency.Monthly, 1, new[] { 1, 3 }, null, start).WeekdayMask); // weekdays: Weekly only
        Assert.False(RecurrenceRule.Validate(RepeatFrequency.None, 7, new[] { 1 }, D(2027, 1, 1), start).Repeats);

        Assert.Throws<ArgumentException>(() => RecurrenceRule.Validate(RepeatFrequency.Weekly, 0, null, null, start));
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Validate(RepeatFrequency.Weekly, 100, null, null, start));
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Validate(RepeatFrequency.Weekly, 1, new[] { 7 }, null, start));
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Validate(RepeatFrequency.Weekly, 1, null, D(2026, 10, 4), start));
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Validate((RepeatFrequency)42, 1, null, null, start));
    }
}
