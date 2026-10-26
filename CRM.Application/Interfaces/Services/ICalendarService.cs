using CRM.Application.DTOs.Calendar;

namespace CRM.Application.Interfaces.Services;

public interface ICalendarService
{
    Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(int month, int year, CancellationToken ct = default);
    Task<CalendarEventDto> CreateEventAsync(CreateCalendarEventDto dto);

    /// <summary>
    /// Replaces the event's fields. Null if it doesn't exist. For a repeating event,
    /// <paramref name="scope"/> and <paramref name="occurrence"/> say which part of the series the
    /// edit covers (see <see cref="EventEditScope"/>); This/Following need the occurrence date.
    /// Returns the event that now holds the edit — a new one-off or new series for This/Following.
    /// </summary>
    Task<CalendarEventDto?> UpdateEventAsync(Guid id, UpdateCalendarEventDto dto, EventEditScope scope = EventEditScope.All, DateOnly? occurrence = null);

    /// <summary>Deletes the event, or for a repeating one just the occurrence / the rest of the series.</summary>
    Task<bool> DeleteEventAsync(Guid id, EventEditScope scope = EventEditScope.All, DateOnly? occurrence = null);
}
