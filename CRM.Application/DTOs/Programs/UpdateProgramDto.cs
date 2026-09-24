using System.ComponentModel.DataAnnotations;
using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Programs;

/// <summary>
/// Full update of a program's editable fields. The slug is intentionally immutable
/// (it backs routes and links), so it is not part of this DTO.
/// </summary>
public class UpdateProgramDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$",
        ErrorMessage = "ColorHex must be a hex color such as #378add.")]
    public string ColorHex { get; set; } = string.Empty;

    [StringLength(100)]
    public string? SessionSchedule { get; set; }

    [StringLength(100)]
    public string? DefaultLocation { get; set; }

    public MeetingDays MeetingDays { get; set; } = MeetingDays.None;
    /// <summary>Pathways or PartTime — which weekly-data framework the program's stars use.</summary>
    public ProgramTrack Track { get; set; } = ProgramTrack.PartTime;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
}
