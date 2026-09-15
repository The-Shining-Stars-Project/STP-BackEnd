using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Participants;

public class ParticipantSummaryDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    /// <summary>Preferred / class name, when it differs from the full name.</summary>
    public string? PreferredName { get; set; }
    public string Initials { get; set; } = string.Empty;
    public ParticipantStatus Status { get; set; }
    public Guid ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramSlug { get; set; } = string.Empty;
    public int AttendancePct { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public bool HasDocAlerts { get; set; }
    public int? BirthYear { get; set; }
    public string? ServiceCoordinator { get; set; }

    // Intake information
    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
    public string? GuardianEmail { get; set; }
    public string? ReferralSource { get; set; }
    public string? TShirtSize { get; set; }
    public string? IntakeNotes { get; set; }
    /// <summary>yyyy-MM-dd, null when not set.</summary>
    public string? AuthorizationExpiry { get; set; }

    /// <summary>yyyy-MM-dd, null when not set.</summary>
    public string? IppExpiry { get; set; }

    /// <summary>yyyy-MM-dd, null when not set.</summary>
    public string? DateOfBirth { get; set; }

    public string? Allergies { get; set; }
    public bool AllergyAnaphylactic { get; set; }
    public string? AreasOfConcern { get; set; }
    public string? ServiceCoordinatorEmail { get; set; }
    public string? ServiceCoordinatorPhone { get; set; }
    public string? ContactInRemind { get; set; }
    public bool IntakeDocsSubmitted { get; set; }
    public bool? HasHighSchoolDiploma { get; set; }

    /// <summary>Up to five free-text emergency contacts, in the order entered.</summary>
    public List<string> EmergencyContacts { get; set; } = new();

    // Self-Determination Program.
    public bool? IsSdpClient { get; set; }
    public string? SdpFmsName { get; set; }
    public string? SdpIndependentFacilitator { get; set; }
    /// <summary>yyyy-MM-dd, null when not set. Always the first of a month by policy.</summary>
    public string? SdpStartDate { get; set; }

    public Guid? SecondaryProgramId { get; set; }
    public string? SecondaryProgramName { get; set; }
    public string? SecondaryProgramSlug { get; set; }
}
