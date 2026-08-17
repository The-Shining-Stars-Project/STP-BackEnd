using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// One append-only record of a security-relevant action: who did what, to which record,
/// from where, and whether it worked. Rows are written once and never updated or deleted —
/// there is deliberately no repository, no update path, and no delete endpoint for this type.
/// </summary>
public class AuditEvent : BaseEntity
{
    /// <summary>
    /// When the audited action happened. Distinct from <see cref="BaseEntity.CreatedAt"/>
    /// on purpose: CreatedAt is a row-insert artifact, OccurredAt is the indexed event time
    /// that every filter and sort in the audit viewer keys off. The audit write happens on a
    /// separate context that may commit slightly after the business action, so the two are
    /// not guaranteed identical and only one of them means "when it happened".
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The acting user's id, stored as a plain Guid with NO foreign key and NO navigation
    /// property. DeleteUserAsync hard-deletes user rows, and a cascade (or a delete blocked
    /// by a restrict rule) would mean an administrator could erase their own history simply
    /// by deleting the account. An audit log a user can erase is not an audit log. Null for
    /// events with no identified actor — a failed login against an unknown email.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// The actor's email, denormalized on purpose. This is what survives the user row being
    /// deleted, so it is the only field that can still answer "who was this" afterwards.
    /// On a failed login it is the SUBMITTED string — untrusted input, sanitized on the way in.
    /// </summary>
    public string UserEmail { get; set; } = string.Empty;

    /// <summary>The actor's role at the time of the action, as a string so a later enum rename cannot rewrite history.</summary>
    public string? UserRole { get; set; }

    /// <summary>Dotted action key, e.g. "participant.view", "auth.login", "export.csv".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The kind of record acted on, e.g. "Participant".</summary>
    public string? EntityType { get; set; }

    /// <summary>The specific record acted on, when one can be identified.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Short human-readable description for the audit viewer.</summary>
    public string? Summary { get; set; }

    /// <summary>
    /// The client address as the server saw it. Behind the frontend's proxy this is the
    /// proxy unless a trusted forwarded-headers hop rewrote it; the raw client-asserted
    /// X-Forwarded-For value is kept separately in <see cref="Metadata"/> and is untrusted.
    /// </summary>
    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Whether the action succeeded. Failed attempts are the interesting half of an audit log.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Free-form JSON detail (failure reason, before/after role, export row count).</summary>
    public string? Metadata { get; set; }
}
