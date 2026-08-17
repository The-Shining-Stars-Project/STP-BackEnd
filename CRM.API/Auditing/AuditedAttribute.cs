namespace CRM.API.Auditing;

/// <summary>
/// Marks a controller action for audit logging. <see cref="AuditMiddleware"/> records the
/// actor, address, user-agent, entity id and FINAL response status — including responses the
/// action never produced, because authorization, the MFA gate or model validation refused the
/// request first.
///
/// Attribute rather than a call inside each action so that adding an endpoint to the audit
/// log is one line that a reviewer can see at a glance, and so that no endpoint can quietly
/// lose its auditing when somebody edits the method body.
///
/// AuthController is the deliberate exception: its events are recorded inside AuthService,
/// because the filter cannot see what actually happened there (LoginAsync returns null for
/// three different failure reasons; DeleteUserAsync destroys the email the row needs; only
/// UpdateUserAsync has the before/after role in hand). No endpoint carries both, so no
/// action produces two rows.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditedAttribute : Attribute
{
    public AuditedAttribute(string action, string entityType)
    {
        Action = action;
        EntityType = entityType;
    }

    /// <summary>Dotted action key, e.g. "participant.view".</summary>
    public string Action { get; }

    /// <summary>The kind of record acted on, e.g. "Participant".</summary>
    public string EntityType { get; }

    /// <summary>
    /// Route value naming the audited record, when it is not the conventional "id" — e.g. a
    /// route where the interesting subject is a nested item rather than the parent.
    /// </summary>
    public string? IdRouteKey { get; init; }

    /// <summary>Optional fixed summary line for the audit viewer.</summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Extra route values to copy into the row's metadata. One audit row carries one entity
    /// id, but some routes name two things that both matter — granting a staff account access
    /// to a program identifies the account in EntityId and needs the program recorded too, or
    /// the row says an access change happened without saying access to what.
    /// </summary>
    public string[]? MetadataRouteKeys { get; init; }
}
