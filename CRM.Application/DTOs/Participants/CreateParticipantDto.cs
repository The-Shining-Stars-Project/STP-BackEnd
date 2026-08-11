using System.ComponentModel.DataAnnotations;
using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Participants;

public class CreateParticipantDto
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string Initials { get; set; } = string.Empty;

    [Required]
    public Guid ProgramId { get; set; }

    public ParticipantStatus Status { get; set; } = ParticipantStatus.Active;

    [Range(1900, 2100)]
    public int? BirthYear { get; set; }

    [StringLength(200)]
    public string? ServiceCoordinator { get; set; }

    public DateTime? StartDate { get; set; }

    [StringLength(200)]
    public string? GuardianName { get; set; }

    [StringLength(50)]
    public string? GuardianPhone { get; set; }

    [StringLength(200)]
    public string? GuardianEmail { get; set; }

    [StringLength(200)]
    public string? ReferralSource { get; set; }

    [StringLength(20)]
    public string? TShirtSize { get; set; }

    [StringLength(2000)]
    public string? IntakeNotes { get; set; }

    public DateTime? AuthorizationExpiry { get; set; }
}

public class UpdateParticipantDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? FullName { get; set; }

    [StringLength(10, MinimumLength = 1)]
    public string? Initials { get; set; }

    public Guid? ProgramId { get; set; }
    public ParticipantStatus? Status { get; set; }

    [Range(1900, 2100)]
    public int? BirthYear { get; set; }

    [StringLength(200)]
    public string? ServiceCoordinator { get; set; }

    [StringLength(200)]
    public string? GuardianName { get; set; }

    [StringLength(50)]
    public string? GuardianPhone { get; set; }

    [StringLength(200)]
    public string? GuardianEmail { get; set; }

    [StringLength(200)]
    public string? ReferralSource { get; set; }

    [StringLength(20)]
    public string? TShirtSize { get; set; }

    [StringLength(2000)]
    public string? IntakeNotes { get; set; }

    public DateTime? AuthorizationExpiry { get; set; }

    /// <summary>True clears the stored expiry (a bare null just means "unchanged" on PUT).</summary>
    public bool ClearAuthorizationExpiry { get; set; }
}
