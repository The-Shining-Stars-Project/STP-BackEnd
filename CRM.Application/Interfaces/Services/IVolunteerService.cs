using CRM.Application.DTOs.Volunteers;

namespace CRM.Application.Interfaces.Services;

public interface IVolunteerService
{
    Task<IReadOnlyList<VolunteerDto>> GetAllAsync(CancellationToken ct = default);
    Task<VolunteerDto?> GetByIdAsync(Guid id);
    Task<VolunteerDto> CreateAsync(CreateVolunteerDto dto);
    Task<VolunteerDto?> UpdateAsync(Guid id, UpdateVolunteerDto dto);
    Task<bool> DeleteAsync(Guid id);
}
