using CRM.Application.DTOs.Files;
using CRM.Application.DTOs.Staff;

namespace CRM.Application.Interfaces.Services;

public interface IStaffService
{
    Task<IReadOnlyList<StaffSummaryDto>> GetAllAsync(CancellationToken ct = default);
    Task<StaffDetailDto?> GetByIdAsync(Guid id);
    Task<StaffDetailDto> CreateAsync(CreateStaffDto dto);
    Task<StaffDetailDto?> UpdateAsync(Guid id, UpdateStaffDto dto);
    Task<StaffDetailDto?> SetOnboardingItemAsync(Guid staffId, Guid itemId, SetOnboardingItemDto dto);

    /// <summary>Attaches (or replaces) the paperwork behind a checklist item. PDF, PNG or JPG.</summary>
    Task<StaffDetailDto?> AttachOnboardingFileAsync(Guid staffId, Guid itemId, Stream content, string fileName, CancellationToken ct = default);
    Task<StoredFile?> OpenOnboardingFileAsync(Guid staffId, Guid itemId, CancellationToken ct = default);
    /// <summary>Removes the file, keeps the checklist item. Idempotent.</summary>
    Task<StaffDetailDto?> RemoveOnboardingFileAsync(Guid staffId, Guid itemId, CancellationToken ct = default);
    Task<IReadOnlyList<ChecklistTemplateItemDto>> GetChecklistTemplateAsync();
    Task<IReadOnlyList<ChecklistTemplateItemDto>> UpdateChecklistTemplateAsync(UpdateChecklistTemplateDto dto);
}
