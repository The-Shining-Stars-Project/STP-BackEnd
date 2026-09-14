using CRM.Application.DTOs.Calendar;

namespace CRM.Application.Interfaces.Services;

public interface ICalendarService
{
    Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(int month, int year, CancellationToken ct = default);
    Task<CalendarEventDto> CreateEventAsync(CreateCalendarEventDto dto);
    /// <summary>Replaces the event's fields. Null if it doesn't exist.</summary>
    Task<CalendarEventDto?> UpdateEventAsync(Guid id, UpdateCalendarEventDto dto);
    Task<bool> DeleteEventAsync(Guid id);
}
