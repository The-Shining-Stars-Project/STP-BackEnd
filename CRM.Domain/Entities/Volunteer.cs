using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// A program volunteer. Deliberately lightweight compared to <see cref="Participant"/>:
/// volunteers aren't tracked for attendance and carry only name/contact basics plus the
/// program they help with.
/// </summary>
public class Volunteer : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Guid ProgramId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    public CrmProgram Program { get; set; } = null!;
}
