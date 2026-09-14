using CRM.Application.DTOs.Programs;

namespace CRM.Application.Interfaces.Services;

public interface IProgramService
{
    Task<IReadOnlyList<ProgramSummaryDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>The programs the caller may work in: all for an Admin, otherwise their linked staff's assigned programs.</summary>
    Task<IReadOnlyList<ProgramSummaryDto>> GetForUserAsync(Guid userId, CancellationToken ct = default);

    Task<ProgramSummaryDto?> GetBySlugAsync(string slug);
    /// <summary>The program page (roster + staff). Throws <see cref="UnauthorizedAccessException"/> when the caller isn't assigned to it.</summary>
    Task<ProgramDetailDto?> GetDetailAsync(Guid userId, string slug);
    Task<ProgramSummaryDto> CreateAsync(CreateProgramDto dto);

    /// <summary>Updates a program's editable fields by id (slug is immutable). Returns null if not found.</summary>
    Task<ProgramSummaryDto?> UpdateAsync(Guid id, UpdateProgramDto dto);

    /// <summary>Assigns a staff member to a program. Idempotent. Returns false if either id is unknown.</summary>
    Task<bool> AssignStaffAsync(Guid programId, Guid staffMemberId);

    /// <summary>Removes a staff member from a program. Returns false if either id is unknown.</summary>
    Task<bool> UnassignStaffAsync(Guid programId, Guid staffMemberId);
}
