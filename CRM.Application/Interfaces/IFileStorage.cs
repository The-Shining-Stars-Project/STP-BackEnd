namespace CRM.Application.Interfaces;

/// <summary>
/// Binary file storage behind the API — Azure Blob Storage in every deployed environment.
///
/// Lives in Application so services can attach files without knowing about Azure; the SDK
/// dependency stays in CRM.Infrastructure, the same split as ITokenService/TokenService.
/// Blob names are opaque keys chosen by the caller ("scripts/{id}/{guid}.pdf"); the container
/// is a deployment decision made in configuration, not by the code that stores a file.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// False when no storage account has been configured (BlobStorage:AccountUrl or
    /// BlobStorage:ConnectionString). Every other member then throws
    /// <see cref="Exceptions.StorageNotConfiguredException"/>, so the API still starts and
    /// everything that does not touch files keeps working.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Writes <paramref name="content"/> under <paramref name="blobName"/>, replacing any
    /// existing blob of that name. Reads the stream from its current position to the end.
    /// </summary>
    Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Opens the blob for reading, or returns null when it does not exist. The caller owns
    /// and disposes the returned stream.
    /// </summary>
    Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default);

    /// <summary>Deletes the blob. A blob that is already gone is not an error.</summary>
    Task DeleteAsync(string blobName, CancellationToken ct = default);
}
