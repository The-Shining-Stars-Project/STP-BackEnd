using CRM.Application.Interfaces;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

public class AttendanceStatsTests
{
    private static AttendanceRecord Record(AttendanceStatus status, Guid? participantId = null) =>
        new() { ParticipantId = participantId ?? Guid.NewGuid(), SessionId = Guid.NewGuid(), Status = status };

    [Fact]
    public void PercentFor_NoRecords_ReturnsZero()
    {
        Assert.Equal(0, AttendanceStats.PercentFor([]));
    }

    [Fact]
    public void PercentFor_OnlyUnmarked_ReturnsZero()
    {
        var records = new[] { Record(AttendanceStatus.Unmarked), Record(AttendanceStatus.Unmarked) };
        Assert.Equal(0, AttendanceStats.PercentFor(records));
    }

    [Fact]
    public void PercentFor_AllPresent_Returns100()
    {
        var records = new[] { Record(AttendanceStatus.Present), Record(AttendanceStatus.Present) };
        Assert.Equal(100, AttendanceStats.PercentFor(records));
    }

    [Fact]
    public void PercentFor_RescheduledAndNotScheduled_AreExcludedFromTheRate()
    {
        // 1 present, 1 absent, plus two records that say nothing about showing up → 50%, not 25%.
        var records = new[]
        {
            Record(AttendanceStatus.Present), Record(AttendanceStatus.Absent),
            Record(AttendanceStatus.Rescheduled), Record(AttendanceStatus.NotScheduled),
        };
        Assert.Equal(50, AttendanceStats.PercentFor(records));
    }

    [Fact]
    public void PercentFor_OnlyRescheduled_ReturnsZero_NotAnError()
    {
        Assert.Equal(0, AttendanceStats.PercentFor([Record(AttendanceStatus.Rescheduled)]));
    }

    [Fact]
    public void PercentFor_AllAbsent_ReturnsZero()
    {
        var records = new[] { Record(AttendanceStatus.Absent), Record(AttendanceStatus.Absent) };
        Assert.Equal(0, AttendanceStats.PercentFor(records));
    }

    [Fact]
    public void PercentFor_MixedWithUnmarked_IgnoresUnmarked()
    {
        // 3 Present, 1 Absent, 2 Unmarked → 3/4 = 75%; Unmarked must not dilute the rate.
        var records = new[]
        {
            Record(AttendanceStatus.Present),
            Record(AttendanceStatus.Present),
            Record(AttendanceStatus.Present),
            Record(AttendanceStatus.Absent),
            Record(AttendanceStatus.Unmarked),
            Record(AttendanceStatus.Unmarked),
        };
        Assert.Equal(75, AttendanceStats.PercentFor(records));
    }

    [Fact]
    public void PercentFor_Rounds()
    {
        // 2 Present, 1 Absent → 66.67% → 67.
        var records = new[]
        {
            Record(AttendanceStatus.Present),
            Record(AttendanceStatus.Present),
            Record(AttendanceStatus.Absent),
        };
        Assert.Equal(67, AttendanceStats.PercentFor(records));
    }

    [Fact]
    public void PercentByParticipant_GroupsPerParticipant()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var records = new[]
        {
            Record(AttendanceStatus.Present, alice),
            Record(AttendanceStatus.Absent, alice),
            Record(AttendanceStatus.Present, bob),
        };

        var map = AttendanceStats.PercentByParticipant(records);

        Assert.Equal(50, map[alice]);
        Assert.Equal(100, map[bob]);
    }

    [Fact]
    public void PercentByParticipant_EmptyInput_EmptyMap()
    {
        Assert.Empty(AttendanceStats.PercentByParticipant(Array.Empty<AttendanceRecord>()));
    }

    [Fact]
    public void PercentByParticipant_FromAggregates_MatchesRecordMath()
    {
        var alice = Guid.NewGuid();
        var aggs = new[] { new ParticipantAttendanceAgg(alice, Guid.NewGuid(), PresentCount: 2, AbsentCount: 1) };

        // 2/3 → 67, same rounding as the record-based overload.
        Assert.Equal(67, AttendanceStats.PercentByParticipant(aggs)[alice]);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 4, 75)]
    [InlineData(1, 3, 33)]
    public void Percent_FromCounts(int present, int marked, int expected)
    {
        Assert.Equal(expected, AttendanceStats.Percent(present, marked));
    }
}
