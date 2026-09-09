using CRM.Application.Exceptions;
using CRM.Application.Interfaces;

namespace CRM.Infrastructure.Storage;

/// <summary>
/// The <see cref="IFileStorage"/> registered when no storage account is configured. Exists so
/// the API boots and serves everything that does not touch files; every file operation
/// answers with <see cref="StorageNotConfiguredException"/> (503) naming the missing setting.
/// </summary>
public sealed class UnconfiguredFileStorage : IFileStorage
{
    public bool IsConfigured => false;

    public Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default) =>
        throw new StorageNotConfiguredException();

    public Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default) =>
        throw new StorageNotConfiguredException();

    public Task DeleteAsync(string blobName, CancellationToken ct = default) =>
        throw new StorageNotConfiguredException();
}
