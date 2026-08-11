using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class Session : BaseEntity
{
    public Guid ProgramId { get; set; }
    public DateTime Date { get; set; }
    public string? Room { get; set; }
    public string? TimeRange { get; set; }
    public string? Label { get; set; }

    /// <summary>Total hours the session ran — recorded for Pathways attendance reporting.</summary>
    public decimal? HoursLogged { get; set; }

    /// <summary>Open while attendance is being taken; Submitted once finalized (records locked).</summary>
    public SessionStatus Status { get; set; } = SessionStatus.Open;
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Optimistic-concurrency token (#26) — concurrent full-row updates now
    /// surface as conflicts instead of silently overwriting each other.</summary>
    public byte[] RowVersion { get; set; } = [];

    public CrmProgram Program { get; set; } = null!;
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
}
