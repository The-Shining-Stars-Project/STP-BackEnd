using CRM.Application.DTOs.Calendar;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class YearCalendarService : IYearCalendarService
{
    private readonly IUnitOfWork _uow;

    public YearCalendarService(IUnitOfWork uow) => _uow = uow;

    public async Task<YearCalendarDto> GetCalendarAsync()
    {
        var themes = await _uow.CalendarThemes.GetAllAsync();
        var dates = await _uow.KeyArtsDates.GetAllAsync();
        return new YearCalendarDto
        {
            Themes = themes.OrderBy(t => t.Month).Select(ToThemeDto).ToList(),
            KeyArtsDates = dates.OrderBy(k => k.Month).ThenBy(k => k.SortOrder).Select(ToDateDto).ToList(),
        };
    }

    public async Task<CalendarThemeDto> UpsertThemeAsync(UpsertCalendarThemeDto dto)
    {
        var theme = (await _uow.CalendarThemes.ListAsync(t => t.Month == dto.Month)).FirstOrDefault();
        if (theme is null)
        {
            theme = new CalendarTheme { Month = dto.Month };
            Apply(theme, dto);
            await _uow.CalendarThemes.AddAsync(theme);
        }
        else
        {
            Apply(theme, dto);
            await _uow.CalendarThemes.UpdateAsync(theme);
        }
        await _uow.SaveChangesAsync();
        return ToThemeDto(theme);
    }

    public async Task<IReadOnlyList<KeyArtsDateDto>> GetKeyArtsDatesAsync()
    {
        var dates = await _uow.KeyArtsDates.GetAllAsync();
        return dates.OrderBy(k => k.Month).ThenBy(k => k.SortOrder).Select(ToDateDto).ToList();
    }

    public async Task<IReadOnlyList<ResolvedKeyArtsDateDto>> GetResolvedKeyArtsDatesAsync(int year)
    {
        if (year is < 2000 or > 2100) throw new ArgumentException("Year must be between 2000 and 2100.");
        var dates = await _uow.KeyArtsDates.GetAllAsync();
        return dates
            .OrderBy(k => k.Month).ThenBy(k => k.SortOrder)
            .Select(k =>
            {
                var r = KeyArtsDateResolver.Resolve(k.DateText, k.Month, year);
                return new ResolvedKeyArtsDateDto
                {
                    Id = k.Id,
                    Month = k.Month,
                    SortOrder = k.SortOrder,
                    DateText = k.DateText,
                    Observance = k.Observance,
                    ObservanceType = k.ObservanceType,
                    ProgrammingTieIn = k.ProgrammingTieIn,
                    Start = r.Start?.ToString("yyyy-MM-dd"),
                    End = r.End?.ToString("yyyy-MM-dd"),
                    IsMonthLong = r.IsMonthLong,
                };
            })
            .ToList();
    }

    private static void Apply(CalendarTheme t, UpsertCalendarThemeDto dto)
    {
        t.ThemeTitle = dto.ThemeTitle;
        t.ThemeSubtitle = dto.ThemeSubtitle;
        t.KeyArtsDatesText = dto.KeyArtsDatesText;
        t.FeaturedGamesText = dto.FeaturedGamesText;
        t.AlternativeOptionsText = dto.AlternativeOptionsText;
        t.ProductionPhase = dto.ProductionPhase;
        t.ProgrammingNotes = dto.ProgrammingNotes;
        t.LegendArc = dto.LegendArc;
    }

    private static CalendarThemeDto ToThemeDto(CalendarTheme t) => new()
    {
        Month = t.Month,
        ThemeTitle = t.ThemeTitle,
        ThemeSubtitle = t.ThemeSubtitle,
        KeyArtsDatesText = t.KeyArtsDatesText,
        FeaturedGamesText = t.FeaturedGamesText,
        AlternativeOptionsText = t.AlternativeOptionsText,
        ProductionPhase = t.ProductionPhase,
        ProgrammingNotes = t.ProgrammingNotes,
        LegendArc = t.LegendArc,
    };

    private static KeyArtsDateDto ToDateDto(KeyArtsDate k) => new()
    {
        Id = k.Id,
        Month = k.Month,
        SortOrder = k.SortOrder,
        DateText = k.DateText,
        Observance = k.Observance,
        ObservanceType = k.ObservanceType,
        ProgrammingTieIn = k.ProgrammingTieIn,
    };
}
