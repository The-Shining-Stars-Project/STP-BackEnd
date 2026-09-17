using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class Participant : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    /// <summary>What the star goes by in class when it differs from the legal name ("JJ" for Jordan). Shown beside the full name.</summary>
    public string? PreferredName { get; set; }
    public string Initials { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    public ParticipantStatus Status { get; set; } = ParticipantStatus.Active;
    public Guid ProgramId { get; set; }
    public string? ServiceCoordinator { get; set; }
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public int AttendancePct { get; set; }

    // Intake information
    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
    public string? GuardianEmail { get; set; }
    public string? ReferralSource { get; set; }
    public string? TShirtSize { get; set; }
    public string? IntakeNotes { get; set; }

    /// <summary>When the star's program authorization (regional-center POS) expires.</summary>
    public DateTime? AuthorizationExpiry { get; set; }

    /// <summary>When the star's IPP (Individual Program Plan) expires — tracked separately from the POS.</summary>
    public DateTime? IppExpiry { get; set; }

    /// <summary>Full birth date (BirthYear stays for older records that only captured the year).</summary>
    public DateTime? DateOfBirth { get; set; }

    public string? Allergies { get; set; }
    /// <summary>True when any listed allergy is anaphylactic (the contact list's "*" convention).</summary>
    public bool AllergyAnaphylactic { get; set; }

    public string? AreasOfConcern { get; set; }
    public string? ServiceCoordinatorEmail { get; set; }
    public string? ServiceCoordinatorPhone { get; set; }
    /// <summary>Who is set up in the Remind app, and when (free text from intake).</summary>
    public string? ContactInRemind { get; set; }
    public bool IntakeDocsSubmitted { get; set; }
    public bool? HasHighSchoolDiploma { get; set; }

    // Self-Determination Program (regional-center funding model). When a star is an SDP
    // client the intake records their Financial Management Service and Independent
    // Facilitator; the SDP start date is always the first of a month and is typed in by an
    // admin (never defaulted).
    public bool? IsSdpClient { get; set; }
    public string? SdpFmsName { get; set; }
    public string? SdpIndependentFacilitator { get; set; }
    public string? SdpIndependentFacilitatorEmail { get; set; }
    public DateTime? SdpStartDate { get; set; }

    /// <summary>
    /// Up to five emergency contacts, one per line, each as free text ("Maria Rivera – (209) 555-0100").
    /// Stored newline-joined; the DTOs expose it as a list.
    /// </summary>
    public string? EmergencyContacts { get; set; }

    /// <summary>
    /// Soft delete: a removed star vanishes from every list and lookup (global query filter)
    /// but its attendance, scores and documents stay on disk. A hard delete failed on the
    /// Restrict foreign keys the moment a star had any attendance history, and erasing a
    /// child's records outright is not something a click should be able to do.
    /// </summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>Second program enrollment — a star can attend Part-Time and Pathways at once.</summary>
    public Guid? SecondaryProgramId { get; set; }
    public CrmProgram? SecondaryProgram { get; set; }

    public CrmProgram Program { get; set; } = null!;
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<DocumentRecord> Documents { get; set; } = new List<DocumentRecord>();
}
