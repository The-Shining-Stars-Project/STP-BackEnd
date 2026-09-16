using System.ComponentModel.DataAnnotations;
namespace CRM.Application.DTOs.Participants;

public class ParticipantArtsProfileDto
{
    public Guid ParticipantId { get; set; }
    public string? IppSummary { get; set; }
    public string? CurrentLevel { get; set; }
    public string? TsspArtsGoal { get; set; }
    /// <summary>False when no profile has been authored yet (the returned fields are empty defaults).</summary>
    public bool HasProfile { get; set; }
}

public class UpsertArtsProfileDto
{
    [StringLength(ParticipantLimits.IntakeNotesMax)]
    public string? IppSummary { get; set; }
    [StringLength(ParticipantLimits.IntakeNotesMax)]
    public string? CurrentLevel { get; set; }
    [StringLength(ParticipantLimits.IntakeNotesMax)]
    public string? TsspArtsGoal { get; set; }
}
