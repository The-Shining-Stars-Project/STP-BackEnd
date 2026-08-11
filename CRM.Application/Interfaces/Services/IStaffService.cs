using CRM.Application.DTOs.Staff;

namespace CRM.Application.Interfaces.Services;

public interface IStaffService
{
    Task<IReadOnlyList<StaffSummaryDto>> GetAllAsync(CancellationToken ct = default);
    Task<StaffDetailDto?> GetByIdAsync(Guid id);
    Task<StaffDetailDto> CreateAsync(CreateStaffDto dto);
    Task<StaffDetailDto?> UpdateAsync(Guid id, UpdateStaffDto dto);
    Task<StaffDetailDto?> SetOnboardingItemAsync(Guid staffId, Guid itemId, SetOnboardingItemDto dto);
    Task<IReadOnlyList<ChecklistTemplateItemDto>> GetChecklistTemplateAsync();
    Task<IReadOnlyList<ChecklistTemplateItemDto>> UpdateChecklistTemplateAsync(UpdateChecklistTemplateDto dto);
}
