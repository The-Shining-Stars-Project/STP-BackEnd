using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// One Star's attendance at one event. Separate from <see cref="AttendanceRecord"/> so that
/// event attendance can never be summed into a class attendance percentage — the client asked
/// for these to be "tracked separately from regular class attendance", and a separate type is
/// what makes that a compile-time guarantee rather than a filter somebody has to remember.
/// </summary>
public class EventAttendanceRecord : BaseEntity
{
    public Guid EventSessionId { get; set; }
    public Guid ParticipantId { get; set; }

    public AttendanceStatus Status { get; set; } = AttendanceStatus.Unmarked;

    /// <summary>
    /// Which participating location this Star performed with, when it matters. Null is valid.
    /// Constrained by the service to a site on the register — see EventAttendanceService.
    /// </summary>
    public Guid? SiteId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public EventSession EventSession { get; set; } = null!;
    public Participant Participant { get; set; } = null!;
    public Site? Site { get; set; }
}
