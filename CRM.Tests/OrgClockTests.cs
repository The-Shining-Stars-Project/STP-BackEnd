using CRM.Infrastructure.Time;
using Microsoft.Extensions.Options;

namespace CRM.Tests;

/// <summary>
/// Regression cover for #7 — deriving a calendar date from <c>DateTime.UtcNow</c> put every
/// late-afternoon session on the wrong day.
/// </summary>
public class OrgClockTests
{
    private static OrgClock Clock(string timeZone = "America/Los_Angeles") =>
        new(Options.Create(new OrgTimeSettings { TimeZone = timeZone }));

    [Fact]
    public void A_late_afternoon_session_belongs_to_the_local_day_not_the_utc_one()
    {
        // 5:30pm Pacific on Thursday 15 January 2026 — an ordinary after-school slot.
        var utcInstant = new DateTime(2026, 1, 16, 1, 30, 0, DateTimeKind.Utc);
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcInstant, pacific);

        // What a teacher in the room would call it...
        Assert.Equal(new DateTime(2026, 1, 15), local.Date);
        Assert.Equal(DayOfWeek.Thursday, local.DayOfWeek);

        // ...versus what the pre-fix code recorded. The day-of-week difference is the
        // damaging half: MeetingDays was checked against Friday for a Thursday session.
        Assert.Equal(new DateTime(2026, 1, 16), utcInstant.Date);
        Assert.Equal(DayOfWeek.Friday, utcInstant.DayOfWeek);
    }

    [Fact]
    public void Today_is_the_local_date_and_can_differ_from_the_utc_date()
    {
        var clock = Clock();
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var expected = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pacific).Date;

        Assert.Equal(expected, clock.Today);
        Assert.Equal(clock.Today, clock.Today.Date);   // midnight, not a mid-day instant
    }

    [Fact]
    public void UtcNow_stays_utc_for_timestamps()
    {
        var clock = Clock();

        // Timestamps must not drift into local time — CreatedAt, token expiry and the rest
        // are compared against UTC elsewhere.
        Assert.True(Math.Abs((DateTime.UtcNow - clock.UtcNow).TotalSeconds) < 5);
    }

    [Fact]
    public void Windows_style_timezone_ids_also_resolve()
    {
        // The CI runner is windows-latest while developers are on macOS; .NET 6+ accepts
        // either id form on either platform, and this fails loudly if that stops being true.
        var clock = Clock("Pacific Standard Time");

        Assert.Equal(Clock().Today, clock.Today);
    }

    [Fact]
    public void An_unknown_timezone_fails_at_construction_rather_than_defaulting_to_utc()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Clock("Mars/Olympus_Mons"));

        Assert.Contains("OrgTime:TimeZone", ex.Message);
    }
}
