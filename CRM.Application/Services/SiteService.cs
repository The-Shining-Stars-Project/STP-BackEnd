using System.Text;
using System.Text.RegularExpressions;
using CRM.Application.DTOs.Taxonomy;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class SiteService : ISiteService
{
    private readonly IUnitOfWork _uow;

    public SiteService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<SiteDto>> GetAllAsync(CancellationToken ct = default)
    {
        var sites = await _uow.Sites.GetAllAsync(ct);
        return sites.OrderByDescending(s => s.IsActive).ThenBy(s => s.SortOrder).ThenBy(s => s.Name).Select(ToDto).ToList();
    }

    public async Task<SiteDto> CreateAsync(CreateSiteDto dto, CancellationToken ct = default)
    {
        var name = Collapse(dto.Name);
        var all = await _uow.Sites.GetAllAsync(ct);

        // A retired site being re-added is far more likely a mistake than a new location
        // with an identical name: reactivate it instead of creating a twin.
        var twin = all.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (twin is not null)
        {
            if (twin.IsActive) throw new InvalidOperationException($"A site named '{twin.Name}' already exists.");
            twin.IsActive = true;
            twin.UpdatedAt = DateTime.UtcNow;
            await _uow.Sites.UpdateAsync(twin);
            await _uow.SaveChangesAsync();
            return ToDto(twin);
        }

        var site = new Site
        {
            Name = name,
            Slug = UniqueSlug(name, all.Select(s => s.Slug)),
            SortOrder = all.Count == 0 ? 1 : all.Max(s => s.SortOrder) + 1,
            IsActive = true,
        };
        await _uow.Sites.AddAsync(site);
        await _uow.SaveChangesAsync();
        return ToDto(site);
    }

    public async Task<SiteDto?> UpdateAsync(Guid id, UpdateSiteDto dto, CancellationToken ct = default)
    {
        var site = await _uow.Sites.GetByIdAsync(id);
        if (site is null) return null;

        if (dto.Name is not null)
        {
            var name = Collapse(dto.Name);
            var others = await _uow.Sites.ListAsync(s => s.Id != id, ct);
            if (others.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A site named '{name}' already exists.");
            // The slug is a stable key (frontend theming, script links) — renaming keeps it.
            site.Name = name;
        }
        if (dto.SortOrder.HasValue) site.SortOrder = dto.SortOrder.Value;
        if (dto.IsActive.HasValue) site.IsActive = dto.IsActive.Value;
        site.UpdatedAt = DateTime.UtcNow;

        await _uow.Sites.UpdateAsync(site);
        await _uow.SaveChangesAsync();
        return ToDto(site);
    }

    private static string Collapse(string s) => string.Join(' ', s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>"MJC Modesto" → "mjc-modesto"; suffixed when taken, so the unique index never fires.</summary>
    private static string UniqueSlug(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var baseSlug = Regex.Replace(name.ToLowerInvariant().Normalize(NormalizationForm.FormD), "[^a-z0-9]+", "-").Trim('-');
        if (baseSlug.Length == 0) baseSlug = "site";
        if (baseSlug.Length > 90) baseSlug = baseSlug[..90].Trim('-');
        if (used.Add(baseSlug)) return baseSlug;
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{baseSlug}-{n}";
            if (used.Add(candidate)) return candidate;
        }
        return $"{baseSlug}-{Guid.NewGuid():N}"[..100];
    }

    public static SiteDto ToDto(Site s) => new()
    {
        Id = s.Id, Name = s.Name, Slug = s.Slug, SortOrder = s.SortOrder, IsActive = s.IsActive,
    };
}
