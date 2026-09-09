using System.Text.RegularExpressions;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CRM.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace CRM.Infrastructure.Storage;

/// <summary>
/// <see cref="IFileStorage"/> over one Azure Blob Storage container.
///
/// Registered as a singleton: BlobContainerClient is thread-safe and owns the HTTP pipeline
/// and (with managed identity) the token cache, so one instance per process is how the SDK
/// is meant to be used. Construction does no I/O — a bad URL fails here, at startup, but the
/// account is not contacted until the first upload.
/// </summary>
public sealed partial class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _containerGate = new(1, 1);
    private volatile bool _containerVerified;

    public AzureBlobFileStorage(IOptions<BlobStorageSettings> options)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
            throw new InvalidOperationException(
                "AzureBlobFileStorage needs BlobStorage:AccountUrl or BlobStorage:ConnectionString. "
                + "DependencyInjection registers UnconfiguredFileStorage when neither is set.");

        if (!ContainerNamePattern().IsMatch(settings.ContainerName))
            throw new InvalidOperationException(
                $"BlobStorage:ContainerName '{settings.ContainerName}' is not a valid container name: "
                + "3-63 characters, lowercase letters, digits and single hyphens only.");

        _container = !string.IsNullOrWhiteSpace(settings.ConnectionString)
            ? new BlobContainerClient(settings.ConnectionString, settings.ContainerName)
            : new BlobContainerClient(
                new Uri($"{settings.AccountUrl!.TrimEnd('/')}/{settings.ContainerName}"),
                // On App Service this resolves to the app's managed identity; on a developer
                // machine to `az login` / Visual Studio credentials.
                new DefaultAzureCredential());
    }

    public bool IsConfigured => true;

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        };
        // The options overload overwrites an existing blob of the same name.
        await _container.GetBlobClient(blobName).UploadAsync(content, options, ct);
    }

    public async Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task DeleteAsync(string blobName, CancellationToken ct = default) =>
        _container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);

    /// <summary>
    /// Creates the container the first time it is needed, once per process. Done lazily rather
    /// than at startup so an unreachable storage account cannot stop the API from booting.
    /// </summary>
    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_containerVerified) return;
        await _containerGate.WaitAsync(ct);
        try
        {
            if (_containerVerified) return;
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            _containerVerified = true;
        }
        finally
        {
            _containerGate.Release();
        }
    }

    // Azure's container naming rules: lowercase alphanumerics and hyphens, no leading, trailing
    // or doubled hyphen, 3-63 characters.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9])){2,62}$")]
    private static partial Regex ContainerNamePattern();
}
