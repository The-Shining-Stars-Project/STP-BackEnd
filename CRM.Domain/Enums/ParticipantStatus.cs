namespace CRM.Domain.Enums;

public enum ParticipantStatus
{
    Active,
    Prospective,
    Attention,
    Former,

    /// <summary>Awaiting authorization from an admin/instructor before joining the program.</summary>
    AuthPending
}
