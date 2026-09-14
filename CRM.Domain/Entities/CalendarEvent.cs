using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class CalendarEvent : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Meta { get; set; }
    public DateTime Date { get; set; }
    public string? TimeRange { get; set; }
    public Guid? ProgramId { get; set; }
    public bool IsUpcoming { get; set; } = true;

    public CrmProgram? Program { get; set; }
    public ICollection<CalendarEventSite> Sites { get; set; } = new List<CalendarEventSite>();
}
