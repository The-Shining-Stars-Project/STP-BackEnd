using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

/// <summary>
/// Whether a Star has outstanding intake paperwork.
///
/// <see cref="ParticipantSummaryDto.HasDocAlerts"/> was hardcoded false in all three places
/// that produced it, so the dashboard's "Document Alerts" tile read 0 with the delta "All
/// clear" no matter what, and the students page's alert badge could never fire. A card that
/// reports "All clear" off a literal false is worse than no card: it answers a question the
/// system was never asking.
///
/// The rule is deliberately narrow and needs no extra query — IntakeDocsSubmitted is a column
/// on Participant, so this stays free on list endpoints that already load the roster.
/// Expiring authorizations and program plans are NOT counted here; those have their own dated
/// alerts on the dashboard, and folding them in would double-report the same Star.
/// </summary>
public static class DocumentAlerts
{
    /// <summary>
    /// True when a Star who has actually started still owes intake documents.
    ///
    /// Deliberately narrower than "every Star who is not Former". Prospective and Inquiry
    /// Stars have not started — missing intake paperwork is the normal state for them, not an
    /// exception, and including them turned the alert into a description of the pipeline
    /// rather than a list of anything to act on. Former and NotInterested are excluded for the
    /// same reason in reverse: nobody is chasing documents for someone who has left.
    ///
    /// KNOWN DATA GAP: nothing populates IntakeDocsSubmitted except the per-Star profile form
    /// — neither the seeder nor the migration loader sets it, and the client's source
    /// spreadsheet records it as free text ("Yes, Mar 06 2026") rather than a checkbox. Every
    /// imported Star therefore starts false and will alert until someone ticks the box. That
    /// is arguably correct — the documents genuinely are not recorded — but it means the count
    /// is high on day one by construction. Treat the first pass through this list as data
    /// entry rather than as a backlog.
    /// </summary>
    public static bool For(Participant p) =>
        !p.IntakeDocsSubmitted && HasStarted(p.Status);

    private static bool HasStarted(ParticipantStatus status) => status is
        ParticipantStatus.Active or
        ParticipantStatus.Attention or
        ParticipantStatus.AuthPending;
}
