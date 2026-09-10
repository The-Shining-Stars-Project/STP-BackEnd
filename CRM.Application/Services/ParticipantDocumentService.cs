using CRM.Application.DTOs.Files;
using CRM.Application.DTOs.Participants;
using CRM.Application.Files;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CRM.Application.Services;

public class ParticipantDocumentService : IParticipantDocumentService
{
    public const long MaxFileBytes = FileValidation.DefaultMaxBytes;

    private readonly IUnitOfWork _uow;
    private readonly IFileStorage _files;
    private readonly IProgramAccessService _access;
    private readonly ILogger<ParticipantDocumentService> _logger;

    public ParticipantDocumentService(
        IUnitOfWork uow, IFileStorage files, IProgramAccessService access, ILogger<ParticipantDocumentService> logger)
    {
        _uow = uow;
        _files = files;
        _access = access;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentRecordDto>?> ListAsync(Guid userId, Guid participantId, CancellationToken ct = default)
    {
        if (await _access.RequireParticipantAsync(userId, participantId) is null) return null;
        var docs = await _uow.DocumentRecords.ListAsync(d => d.ParticipantId == participantId, ct);
        return docs.OrderBy(d => d.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<DocumentRecordDto?> CreateAsync(Guid userId, Guid participantId, CreateDocumentRecordDto dto, CancellationToken ct = default)
    {
        if (await _access.RequireParticipantAsync(userId, participantId) is null) return null;

        var doc = new DocumentRecord
        {
            ParticipantId = participantId,
            DocumentType = dto.DocumentType.Trim(),
            ExpiryDate = dto.ExpiryDate,
            IsComplete = dto.IsComplete,
        };
        await _uow.DocumentRecords.AddAsync(doc);
        await _uow.SaveChangesAsync();
        return ToDto(doc);
    }

    public async Task<DocumentRecordDto?> UpdateAsync(Guid userId, Guid participantId, Guid documentId, UpdateDocumentRecordDto dto, CancellationToken ct = default)
    {
        var doc = await FindAsync(userId, participantId, documentId, ct);
        if (doc is null) return null;

        if (dto.DocumentType is not null) doc.DocumentType = dto.DocumentType.Trim();
        if (dto.ExpiryDate.HasValue) doc.ExpiryDate = dto.ExpiryDate;
        else if (dto.ClearExpiry) doc.ExpiryDate = null;
        if (dto.IsComplete.HasValue) doc.IsComplete = dto.IsComplete.Value;
        doc.UpdatedAt = DateTime.UtcNow;

        await _uow.DocumentRecords.UpdateAsync(doc);
        await _uow.SaveChangesAsync();
        return ToDto(doc);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid participantId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await FindAsync(userId, participantId, documentId, ct);
        if (doc is null) return false;

        var blob = doc.BlobName;
        await _uow.DocumentRecords.DeleteAsync(doc);
        await _uow.SaveChangesAsync();

        // Row first, blob second: the failure mode of the other order is a record pointing
        // at a file that is gone, which the UI would then offer for download.
        if (blob is not null) await TryDeleteAsync(blob);
        return true;
    }

    public async Task<DocumentRecordDto?> AttachFileAsync(Guid userId, Guid participantId, Guid documentId, Stream content, string fileName, CancellationToken ct = default)
    {
        var doc = await FindAsync(userId, participantId, documentId, ct);
        if (doc is null) return null;

        var file = FileValidation.Validate(content, fileName, FileValidation.Documents, MaxFileBytes);

        // A fresh blob name per upload (never overwrite in place): the old file stays intact
        // until the new pointer is durable, and nothing can serve a half-written replacement.
        var blobName = $"participants/{participantId:D}/documents/{documentId:D}/{Guid.NewGuid():N}{file.Extension}";
        await _files.UploadAsync(blobName, content, file.ContentType, ct);

        var previous = doc.BlobName;
        doc.BlobName = blobName;
        doc.FileName = file.FileName;
        doc.ContentType = file.ContentType;
        doc.SizeBytes = file.Length;
        doc.UploadedAt = DateTime.UtcNow;
        doc.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _uow.DocumentRecords.UpdateAsync(doc);
            await _uow.SaveChangesAsync();
        }
        catch
        {
            await TryDeleteAsync(blobName);
            throw;
        }

        if (previous is not null && previous != blobName)
            await TryDeleteAsync(previous);

        return ToDto(doc);
    }

    public async Task<StoredFile?> OpenFileAsync(Guid userId, Guid participantId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await FindAsync(userId, participantId, documentId, ct);
        if (doc?.BlobName is null) return null;

        var stream = await _files.OpenReadAsync(doc.BlobName, ct);
        if (stream is null)
        {
            _logger.LogWarning(
                "Document {DocumentId} points at blob {BlobName}, which no longer exists in storage.",
                documentId, doc.BlobName);
            return null;
        }

        return new StoredFile(stream, doc.FileName ?? "document", doc.ContentType ?? "application/octet-stream", doc.SizeBytes);
    }

    public async Task<DocumentRecordDto?> RemoveFileAsync(Guid userId, Guid participantId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await FindAsync(userId, participantId, documentId, ct);
        if (doc is null) return null;

        var blobName = doc.BlobName;
        if (blobName is null) return ToDto(doc);

        doc.BlobName = null;
        doc.FileName = null;
        doc.ContentType = null;
        doc.SizeBytes = null;
        doc.UploadedAt = null;
        doc.UpdatedAt = DateTime.UtcNow;

        await _uow.DocumentRecords.UpdateAsync(doc);
        await _uow.SaveChangesAsync();

        await TryDeleteAsync(blobName);
        return ToDto(doc);
    }

    /// <summary>Scope check on the participant, then the document must belong to them.</summary>
    private async Task<DocumentRecord?> FindAsync(Guid userId, Guid participantId, Guid documentId, CancellationToken ct)
    {
        if (await _access.RequireParticipantAsync(userId, participantId) is null) return null;
        return await _uow.DocumentRecords.FirstOrDefaultAsync(
            d => d.Id == documentId && d.ParticipantId == participantId, ct);
    }

    private async Task TryDeleteAsync(string blobName)
    {
        try
        {
            await _files.DeleteAsync(blobName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete blob {BlobName}; it is now an orphan.", blobName);
        }
    }

    public static DocumentRecordDto ToDto(DocumentRecord d) => new()
    {
        Id = d.Id,
        DocumentType = d.DocumentType,
        ExpiryDate = d.ExpiryDate?.ToString("yyyy-MM-dd"),
        IsComplete = d.IsComplete,
        HasFile = d.BlobName is not null,
        FileName = d.FileName,
        ContentType = d.ContentType,
        SizeBytes = d.SizeBytes,
        UploadedAt = d.UploadedAt,
    };
}
