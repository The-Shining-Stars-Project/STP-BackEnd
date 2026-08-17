using System.ComponentModel.DataAnnotations;

namespace CRM.Application.DTOs.Audit;

/// <summary>One row as the audit viewer sees it. Projection only — never the entity.</summary>
public class AuditEventDto
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string? UserRole { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Summary { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Succeeded { get; set; }
    public string? Metadata { get; set; }
}

/// <summary>Filters + paging for GET /api/audit. All filters are optional and combine with AND.</summary>
public class AuditQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? Action { get; set; }
    public string? EntityType { get; set; }
    public bool? Succeeded { get; set; }

    /// <summary>1-based. Already clamped by the controller before it reaches the query.</summary>
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// What the client reports after it has built a CSV in the browser. See
/// <see cref="ExportAuditValidation"/> — every field here is untrusted input.
/// </summary>
public class RecordExportDto
{
    [Required]
    [StringLength(64)]
    public string ExportKind { get; set; } = string.Empty;

    public int RowCount { get; set; }

    [StringLength(256)]
    public string? FileName { get; set; }

    /// <summary>Optional free-text description of what was in scope (filters applied, etc.).</summary>
    [StringLength(256)]
    public string? Scope { get; set; }
}

/// <summary>A client export report that has passed validation and been normalized.</summary>
public sealed record ValidatedExport(string ExportKind, int RowCount, string? FileName, string? Scope);

/// <summary>
/// Validation for the client-reported export endpoint, kept in Application so it can be
/// unit-tested without spinning up the API.
///
/// This endpoint is the one place where a caller writes strings of their choosing straight
/// into the audit log, so it is treated as hostile input: the kind must be one of a known
/// set, the row count is clamped to a sane range, and the free-text fields are length-capped
/// (control characters are stripped later by AuditEventFactory.Sanitize, which every audit
/// write goes through). Without this, the endpoint is a log-flooding and log-forging
/// primitive — a convenient way to bury a real event under plausible-looking noise.
/// </summary>
public static class ExportAuditValidation
{
    /// <summary>
    /// The exports the frontend actually performs. A new export site must be added here as
    /// well, which is deliberate: it keeps the audit log's vocabulary closed and reviewable.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedKinds =
    [
        "reports-summary",
        "star-attendance",
        "participant-roster",
        "stars-detail",
        "staff-onboarding",
    ];

    public const int MaxRowCount = 1_000_000;
    public const int FileNameMaxLength = 128;
    public const int ScopeMaxLength = 128;

    public static bool TryValidate(RecordExportDto? dto, out ValidatedExport validated, out string? error)
    {
        validated = new ValidatedExport(string.Empty, 0, null, null);

        if (dto is null)
        {
            error = "A request body is required.";
            return false;
        }

        var kind = dto.ExportKind?.Trim() ?? string.Empty;
        if (!AllowedKinds.Contains(kind, StringComparer.Ordinal))
        {
            // Do not echo the rejected value back — it is unvalidated input and the caller
            // already knows what they sent.
            error = $"Unknown export kind. Expected one of: {string.Join(", ", AllowedKinds)}.";
            return false;
        }

        var rowCount = Math.Clamp(dto.RowCount, 0, MaxRowCount);
        var fileName = Truncate(dto.FileName, FileNameMaxLength);
        var scope = Truncate(dto.Scope, ScopeMaxLength);

        validated = new ValidatedExport(kind, rowCount, fileName, scope);
        error = null;
        return true;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
