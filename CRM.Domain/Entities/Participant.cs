using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class Participant : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
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

    /// <summary>Second program enrollment — a star can attend Part-Time and Pathways at once.</summary>
    public Guid? SecondaryProgramId { get; set; }
    public CrmProgram? SecondaryProgram { get; set; }

    public CrmProgram Program { get; set; } = null!;
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<DocumentRecord> Documents { get; set; } = new List<DocumentRecord>();
}
