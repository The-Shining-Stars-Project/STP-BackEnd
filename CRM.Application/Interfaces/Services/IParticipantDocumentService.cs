using CRM.Application.DTOs.Files;
using CRM.Application.DTOs.Participants;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// A star's paperwork: the intake checklist rows and the scans attached to them. Every
/// method is scoped to the caller's programs the same way the participant itself is (#1) —
/// null means the participant (or document) does not exist, <see cref="UnauthorizedAccessException"/>
/// means it exists but is out of scope.
/// </summary>
public interface IParticipantDocumentService
{
    Task<IReadOnlyList<DocumentRecordDto>?> ListAsync(Guid userId, Guid participantId, CancellationToken ct = default);
    Task<DocumentRecordDto?> CreateAsync(Guid userId, Guid participantId, CreateDocumentRecordDto dto, CancellationToken ct = default);
    Task<DocumentRecordDto?> UpdateAsync(Guid userId, Guid participantId, Guid documentId, UpdateDocumentRecordDto dto, CancellationToken ct = default);
    /// <summary>Deletes the record and its file. False when there is nothing to delete.</summary>
    Task<bool> DeleteAsync(Guid userId, Guid participantId, Guid documentId, CancellationToken ct = default);

    /// <summary>Attaches (or replaces) the file. PDF, PNG or JPG; 400 for anything else.</summary>
    Task<DocumentRecordDto?> AttachFileAsync(Guid userId, Guid participantId, Guid documentId, Stream content, string fileName, CancellationToken ct = default);
    Task<StoredFile?> OpenFileAsync(Guid userId, Guid participantId, Guid documentId, CancellationToken ct = default);
    /// <summary>Removes the file, keeps the record. Idempotent.</summary>
    Task<DocumentRecordDto?> RemoveFileAsync(Guid userId, Guid participantId, Guid documentId, CancellationToken ct = default);
}
