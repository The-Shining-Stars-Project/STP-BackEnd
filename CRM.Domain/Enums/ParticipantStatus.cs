namespace CRM.Domain.Enums;

public enum ParticipantStatus
{
    Active,
    Prospective,
    Attention,
    Former,

    /// <summary>Awaiting authorization from an admin/instructor before joining the program.</summary>
    AuthPending,

    /// <summary>Inquired but hasn't had a trial visit yet (the contact list's tracking tab).</summary>
    Inquiry,

    /// <summary>No longer interested or unreachable — kept for history, out of every pipeline.</summary>
    NotInterested
}
