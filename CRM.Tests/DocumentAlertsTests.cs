using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// HasDocAlerts was hardcoded false everywhere it was produced, so the dashboard tile read
/// "All clear" on the strength of a literal. These pin the rule that replaced it.
/// </summary>
public class DocumentAlertsTests
{
    private static Participant Star(ParticipantStatus status, bool docsIn) =>
        new() { FullName = "Test", Status = status, IntakeDocsSubmitted = docsIn };

    [Theory]
    [InlineData(ParticipantStatus.Active)]
    [InlineData(ParticipantStatus.Attention)]
    [InlineData(ParticipantStatus.AuthPending)]
    public void A_started_star_missing_intake_docs_alerts(ParticipantStatus status)
    {
        Assert.True(DocumentAlerts.For(Star(status, docsIn: false)));
    }

    [Theory]
    [InlineData(ParticipantStatus.Prospective)]
    [InlineData(ParticipantStatus.Inquiry)]
    public void Stars_who_have_not_started_are_not_alerted(ParticipantStatus status)
    {
        // Missing intake paperwork is the normal state before a Star starts. Alerting on it
        // would describe the intake pipeline rather than list anything to act on — and on the
        // client's real data that is ~47 of 128 Stars.
        Assert.False(DocumentAlerts.For(Star(status, docsIn: false)));
    }

    [Theory]
    [InlineData(ParticipantStatus.Active)]
    [InlineData(ParticipantStatus.AuthPending)]
    public void Docs_submitted_clears_the_alert(ParticipantStatus status)
    {
        Assert.False(DocumentAlerts.For(Star(status, docsIn: true)));
    }

    [Theory]
    [InlineData(ParticipantStatus.Former)]
    [InlineData(ParticipantStatus.NotInterested)]
    public void Stars_who_have_left_are_not_chased(ParticipantStatus status)
    {
        // Otherwise the tile would sit permanently non-zero on people who are gone.
        Assert.False(DocumentAlerts.For(Star(status, docsIn: false)));
    }
}
