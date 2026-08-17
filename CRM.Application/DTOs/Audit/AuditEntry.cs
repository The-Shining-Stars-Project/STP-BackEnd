namespace CRM.Application.DTOs.Audit;

/// <summary>
/// What a caller hands to <c>IAuditService.RecordAsync</c>. Every actor field is nullable:
/// left null they are filled from the ambient request context, which is what the action
/// filter and most service calls want. They are set explicitly for the cases where the actor
/// is not the subject and the context cannot know the difference — a failed login has no
/// authenticated principal, and an admin password reset is performed by the admin against
/// somebody else's account.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>Dotted action key, e.g. "auth.login". Required.</summary>
    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserRole { get; set; }

    public string? Summary { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public bool Succeeded { get; set; } = true;

    /// <summary>Free-form JSON detail. Never put a password, token, or secret in here.</summary>
    public string? Metadata { get; set; }

    /// <summary>Defaults to UtcNow when left null.</summary>
    public DateTime? OccurredAt { get; set; }
}
