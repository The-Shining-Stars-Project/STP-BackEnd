using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Tests;

/// <summary>Dictionary-backed <see cref="IFileStorage"/> that records every call in order.</summary>
internal sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Blobs { get; } = new();
    public Dictionary<string, string> ContentTypes { get; } = new();
    public List<string> Deleted { get; } = new();
    public List<string> Log { get; } = new();
    public bool FailDeletes { get; set; }

    public bool IsConfigured => true;

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default)
    {
        Log.Add($"upload:{blobName}");
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Blobs[blobName] = buffer.ToArray();
        ContentTypes[blobName] = contentType;
    }

    public Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default)
    {
        Log.Add($"open:{blobName}");
        return Task.FromResult<Stream?>(Blobs.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes) : null);
    }

    public Task DeleteAsync(string blobName, CancellationToken ct = default)
    {
        Log.Add($"delete:{blobName}");
        if (FailDeletes) throw new IOException("storage is down");
        Blobs.Remove(blobName);
        Deleted.Add(blobName);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Program access that behaves like an Admin for every user, backed by the fake unit of work
/// for the participant lookup — the scoping rule itself is covered by ProgramScopingTests.
/// </summary>
internal sealed class FakeAllowAllAccess : IProgramAccessService
{
    private readonly FakeUnitOfWork _uow;
    public FakeAllowAllAccess(FakeUnitOfWork uow) => _uow = uow;

    public Task<ProgramAccess> ForUserAsync(Guid userId) =>
        Task.FromResult(new ProgramAccess(true, new HashSet<Guid>()));

    public Task<Participant?> RequireParticipantAsync(Guid userId, Guid participantId) =>
        _uow.Participants.GetByIdAsync(participantId);
}

/// <summary>Seekable in-memory streams that start with a real file header, for upload tests.</summary>
internal static class SampleFiles
{
    public static MemoryStream Pdf(int size = 64)
    {
        var bytes = new byte[size];
        "%PDF-1.7"u8.CopyTo(bytes);
        return new MemoryStream(bytes);
    }

    public static MemoryStream Png(int size = 32)
    {
        var bytes = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return new MemoryStream(bytes);
    }

    public static MemoryStream Jpeg(int size = 40)
    {
        var bytes = new byte[size];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(bytes, 0);
        return new MemoryStream(bytes);
    }

    /// <summary>Bytes that are not any allowed format, whatever the file is named.</summary>
    public static MemoryStream Zip() => new("PK definitely a zip archive"u8.ToArray());
}
