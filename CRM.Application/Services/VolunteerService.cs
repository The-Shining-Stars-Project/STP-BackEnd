using CRM.Application.DTOs.Volunteers;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class VolunteerService : IVolunteerService
{
    private readonly IUnitOfWork _uow;
    private readonly IOrgClock _clock;

    public VolunteerService(IUnitOfWork uow, IOrgClock clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<IReadOnlyList<VolunteerDto>> GetAllAsync(CancellationToken ct = default)
    {
        var volunteers = await _uow.Volunteers.GetAllAsync(ct);
        var programs = (await _uow.Programs.GetAllAsync(ct)).ToDictionary(p => p.Id);
        return volunteers
            .OrderBy(v => v.FullName)
            .Select(v => ToDto(v, programs))
            .ToList();
    }

    public async Task<VolunteerDto?> GetByIdAsync(Guid id)
    {
        var v = await _uow.Volunteers.GetByIdAsync(id);
        if (v is null) return null;
        var programs = (await _uow.Programs.GetAllAsync()).ToDictionary(p => p.Id);
        return ToDto(v, programs);
    }

    public async Task<VolunteerDto> CreateAsync(CreateVolunteerDto dto)
    {
        var volunteer = new Volunteer
        {
            FullName = dto.FullName.Trim(),
            Initials = ToInitials(dto.FullName),
            ProgramId = dto.ProgramId,
            Phone = dto.Phone,
            Email = dto.Email,
            Notes = dto.Notes,
            StartDate = dto.StartDate ?? _clock.Today,
        };
        await _uow.Volunteers.AddAsync(volunteer);
        await _uow.SaveChangesAsync();
        return (await GetByIdAsync(volunteer.Id))!;
    }

    public async Task<VolunteerDto?> UpdateAsync(Guid id, UpdateVolunteerDto dto)
    {
        var v = await _uow.Volunteers.GetByIdAsync(id);
        if (v is null) return null;

        if (dto.FullName is not null) { v.FullName = dto.FullName.Trim(); v.Initials = ToInitials(dto.FullName); }
        if (dto.ProgramId.HasValue) v.ProgramId = dto.ProgramId.Value;
        if (dto.Phone is not null) v.Phone = dto.Phone;
        if (dto.Email is not null) v.Email = dto.Email;
        if (dto.Notes is not null) v.Notes = dto.Notes;
        if (dto.IsActive.HasValue) v.IsActive = dto.IsActive.Value;

        await _uow.Volunteers.UpdateAsync(v);
        await _uow.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var v = await _uow.Volunteers.GetByIdAsync(id);
        if (v is null) return false;
        await _uow.Volunteers.DeleteAsync(v);
        await _uow.SaveChangesAsync();
        return true;
    }

    private static VolunteerDto ToDto(Volunteer v, Dictionary<Guid, CrmProgram> programs)
    {
        programs.TryGetValue(v.ProgramId, out var prog);
        return new VolunteerDto
        {
            Id = v.Id,
            FullName = v.FullName,
            Initials = v.Initials,
            Phone = v.Phone,
            Email = v.Email,
            ProgramId = v.ProgramId,
            ProgramName = prog?.Name ?? string.Empty,
            ProgramSlug = prog?.Slug ?? string.Empty,
            Notes = v.Notes,
            IsActive = v.IsActive,
            StartDate = v.StartDate.ToString("yyyy-MM-dd"),
        };
    }

    private static string ToInitials(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "??";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant();
    }
}
