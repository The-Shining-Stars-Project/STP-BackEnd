using CRM.Application.DTOs.Scripts;

namespace CRM.Application.Interfaces.Services;

public interface IScriptService
{
    Task<IReadOnlyList<ScriptDto>> GetAllAsync();
    Task<ScriptDto?> GetByIdAsync(Guid id);
    Task<ScriptDto> CreateAsync(CreateScriptDto dto);
    Task<ScriptDto?> UpdateAsync(Guid id, UpdateScriptDto dto);

    /// <summary>
    /// Stores <paramref name="content"/> as the script's PDF, replacing any previous one.
    /// Returns null when no such script exists. Throws
    /// <see cref="Exceptions.InvalidFileException"/> for anything that is not a PDF (by name
    /// AND by header), is empty, or exceeds <see cref="Services.ScriptService.MaxPdfBytes"/>;
    /// <see cref="Exceptions.StorageNotConfiguredException"/> when no storage account is set.
    /// The stream must be seekable — the header check rewinds it before upload.
    /// </summary>
    Task<ScriptDto?> AttachPdfAsync(Guid id, Stream content, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Opens the script's PDF for download, or returns null when the script does not exist,
    /// has no PDF, or its blob has gone missing. The caller disposes the stream.
    /// </summary>
    Task<ScriptPdfFile?> OpenPdfAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Detaches and deletes the script's PDF. Idempotent: a script with no PDF is returned
    /// unchanged. Returns null when no such script exists.
    /// </summary>
    Task<ScriptDto?> RemovePdfAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Deletes the script, its program links and its PDF. False when no such script exists.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
