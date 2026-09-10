using CRM.Application.DTOs.Taxonomy;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// Sites are managed, not seeded: the organisation adds a location when it opens one and
/// retires it when it closes. Never deleted — roster history and event registers point at them.
/// </summary>
public interface ISiteService
{
    /// <summary>Every site, active and retired, in display order.</summary>
    Task<IReadOnlyList<SiteDto>> GetAllAsync(CancellationToken ct = default);
    Task<SiteDto> CreateAsync(CreateSiteDto dto, CancellationToken ct = default);
    /// <summary>Null when the site does not exist.</summary>
    Task<SiteDto?> UpdateAsync(Guid id, UpdateSiteDto dto, CancellationToken ct = default);
}
