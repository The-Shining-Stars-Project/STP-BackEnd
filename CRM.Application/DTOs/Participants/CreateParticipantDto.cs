using System.ComponentModel.DataAnnotations;
using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Participants;

/// <summary>Field limits shared by the DTOs, the service and the EF configuration.</summary>
public static class ParticipantLimits
{
    /// <summary>Intake notes are free-form (nvarchar(max)); this is a sanity ceiling, not a column width.</summary>
    public const int IntakeNotesMax = 20_000;
    public const int EmergencyContactsMax = 5;
    public const int EmergencyContactMaxLength = 300;
}

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

    [StringLength(ParticipantLimits.IntakeNotesMax)]
    public string? IntakeNotes { get; set; }

    public DateTime? AuthorizationExpiry { get; set; }
    public DateTime? IppExpiry { get; set; }
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Up to five free-text contacts ("name – phone"). Blank entries are dropped.</summary>
    [MaxLength(ParticipantLimits.EmergencyContactsMax)]
    public List<string>? EmergencyContacts { get; set; }

    // Self-Determination Program.
    public bool? IsSdpClient { get; set; }
    [StringLength(200)]
    public string? SdpFmsName { get; set; }
    [StringLength(200)]
    public string? SdpIndependentFacilitator { get; set; }
    public DateTime? SdpStartDate { get; set; }

    [StringLength(500)]
    public string? Allergies { get; set; }
    public bool AllergyAnaphylactic { get; set; }

    [StringLength(1000)]
    public string? AreasOfConcern { get; set; }

    [StringLength(200)]
    public string? ServiceCoordinatorEmail { get; set; }

    [StringLength(50)]
    public string? ServiceCoordinatorPhone { get; set; }

    [StringLength(300)]
    public string? ContactInRemind { get; set; }

    public bool IntakeDocsSubmitted { get; set; }
    public bool? HasHighSchoolDiploma { get; set; }

    public Guid? SecondaryProgramId { get; set; }
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

    [StringLength(ParticipantLimits.IntakeNotesMax)]
    public string? IntakeNotes { get; set; }

    /// <summary>The day the star started; editable because intake often records it after the fact.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Replaces the contact list when present (send an empty list to clear); null means "unchanged".</summary>
    [MaxLength(ParticipantLimits.EmergencyContactsMax)]
    public List<string>? EmergencyContacts { get; set; }

    // Self-Determination Program. Strings follow the usual "null = unchanged" rule; the date
    // has a Clear flag like the other dates.
    public bool? IsSdpClient { get; set; }
    [StringLength(200)]
    public string? SdpFmsName { get; set; }
    [StringLength(200)]
    public string? SdpIndependentFacilitator { get; set; }
    public DateTime? SdpStartDate { get; set; }
    public bool ClearSdpStartDate { get; set; }

    public DateTime? AuthorizationExpiry { get; set; }

    /// <summary>True clears the stored expiry (a bare null just means "unchanged" on PUT).</summary>
    public bool ClearAuthorizationExpiry { get; set; }

    public DateTime? IppExpiry { get; set; }
    public bool ClearIppExpiry { get; set; }

    public DateTime? DateOfBirth { get; set; }

    [StringLength(500)]
    public string? Allergies { get; set; }
    public bool? AllergyAnaphylactic { get; set; }

    [StringLength(1000)]
    public string? AreasOfConcern { get; set; }

    [StringLength(200)]
    public string? ServiceCoordinatorEmail { get; set; }

    [StringLength(50)]
    public string? ServiceCoordinatorPhone { get; set; }

    [StringLength(300)]
    public string? ContactInRemind { get; set; }

    public bool? IntakeDocsSubmitted { get; set; }
    public bool? HasHighSchoolDiploma { get; set; }

    public Guid? SecondaryProgramId { get; set; }
    /// <summary>True removes the secondary enrollment (null alone means "unchanged").</summary>
    public bool ClearSecondaryProgram { get; set; }
}

/// <summary>
/// The one field a teacher may change on a star: the intake notes. Everything else on the
/// profile stays management-only (client rule, Sep 2026).
/// </summary>
public class UpdateIntakeNotesDto
{
    [StringLength(ParticipantLimits.IntakeNotesMax)]
    public string? IntakeNotes { get; set; }
}
