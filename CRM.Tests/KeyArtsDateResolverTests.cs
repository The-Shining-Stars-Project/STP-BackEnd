using CRM.Application.Services;
using CRM.Domain.Entities;
using Xunit;

namespace CRM.Tests;

/// <summary>
/// The key arts dates sheet writes dates as free text. The calendar needs real days, and
/// anything it can't read must stay off the grid rather than land on a guessed day (Oct 2026).
/// </summary>
public class KeyArtsDateResolverTests
{
    private static (string? Start, string? End) R(string text, int month, int year = 2026)
    {
        var r = KeyArtsDateResolver.Resolve(text, month, year);
        return (r.Start?.ToString("yyyy-MM-dd"), r.End?.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("Mar 27", 3, "2026-03-27")]
    [InlineData("March 27", 3, "2026-03-27")]
    [InlineData("Sept. 15", 9, "2026-09-15")]
    [InlineData("3/27", 3, "2026-03-27")]
    [InlineData("15", 9, "2026-09-15")]
    public void Single_days_in_every_spelling(string text, int month, string expected)
    {
        Assert.Equal((expected, expected), R(text, month));
    }

    [Fact]
    public void Numeric_ranges_including_one_that_wraps_the_year()
    {
        Assert.Equal(("2026-09-15", "2026-10-15"), R("9/15-10/15", 9));
        Assert.Equal(("2026-12-26", "2027-01-01"), R("12/26-1/1", 12));
        Assert.Equal(("2026-12-26", "2027-01-01"), R("12/26 – 1/1", 12)); // en dash, spaces
        Assert.Equal(("2026-11-02", "2026-11-06"), R("2-6", 11));
        Assert.Equal(("2026-05-01", "2026-06-15"), R("May 1 - Jun 15", 5));
    }

    [Theory]
    [InlineData("3rd Mon", 1, "2026-01-19")]
    [InlineData("1st Monday", 9, "2026-09-07")]
    [InlineData("Last Fri", 10, "2026-10-30")]
    [InlineData("2nd Tue", 2, "2026-02-10")]
    public void Ordinal_weekdays(string text, int month, string expected)
    {
        Assert.Equal((expected, expected), R(text, month));
    }

    [Fact]
    public void A_fifth_weekday_the_month_lacks_is_unresolved()
    {
        Assert.Equal((null, null), R("5th Mon", 2)); // Feb 2026 has four Mondays
    }

    [Fact]
    public void Full_month_spans_the_month_and_is_flagged_month_long()
    {
        var r = KeyArtsDateResolver.Resolve("Full month", 2, 2026);
        Assert.Equal(new DateOnly(2026, 2, 1), r.Start);
        Assert.Equal(new DateOnly(2026, 2, 28), r.End);
        Assert.True(r.IsMonthLong);

        Assert.True(KeyArtsDateResolver.Resolve("9/15-10/15", 9, 2026).IsMonthLong);
        Assert.False(KeyArtsDateResolver.Resolve("Mar 27", 3, 2026).IsMonthLong);
    }

    [Theory]
    [InlineData("Varies")]
    [InlineData("TBD")]
    [InlineData("Spring")]
    [InlineData("")]
    [InlineData("Blursday 40")]
    [InlineData("13/45")]
    public void Prose_and_nonsense_stay_off_the_grid(string text)
    {
        Assert.Equal((null, null), R(text, 4));
    }

    [Fact]
    public void Feb_29_clamps_in_a_non_leap_year_and_the_month_in_the_text_wins()
    {
        Assert.Equal(("2026-02-28", "2026-02-28"), R("2/29", 2, 2026));
        Assert.Equal(("2028-02-29", "2028-02-29"), R("2/29", 2, 2028));
        // The sheet sometimes files a late-month date under the following month's row.
        Assert.Equal(("2026-03-27", "2026-03-27"), R("Mar 27", 4));
    }

    [Fact]
    public async Task The_service_resolves_every_date_for_the_requested_year_and_rejects_silly_years()
    {
        var uow = new FakeUnitOfWork();
        await uow.KeyArtsDates.AddAsync(new KeyArtsDate { Month = 3, SortOrder = 1, DateText = "Mar 27", Observance = "World Theatre Day" });
        await uow.KeyArtsDates.AddAsync(new KeyArtsDate { Month = 9, SortOrder = 1, DateText = "Varies", Observance = "Fall Cohort Begins" });
        var service = new YearCalendarService(uow);

        var list = await service.GetResolvedKeyArtsDatesAsync(2027);

        Assert.Equal("2027-03-27", list.Single(d => d.Month == 3).Start);
        var varies = list.Single(d => d.Month == 9);
        Assert.Null(varies.Start);
        Assert.Equal("Varies", varies.DateText);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetResolvedKeyArtsDatesAsync(1999));
    }
}
