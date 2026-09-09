namespace CRM.Application.Exceptions;

/// <summary>
/// A file operation was attempted but no storage account is configured.
///
/// GlobalExceptionHandler turns this into 503 Service Unavailable with the message below as
/// the detail, so the UI can say "not set up yet" instead of "something broke", and so an
/// operator reading the response knows which setting to add. File storage is deliberately
/// optional at startup — the rest of the API must keep working in an environment where the
/// storage account has not been created yet.
///
/// Derives from InvalidOperationException like DuplicateEmailException, and for the same
/// reason: a handler that has not been taught this type keeps its existing behaviour.
/// </summary>
public sealed class StorageNotConfiguredException : InvalidOperationException
{
    public StorageNotConfiguredException()
        : base("File storage is not configured yet. Set BlobStorage:AccountUrl (managed identity) "
             + "or BlobStorage:ConnectionString on the API before uploading files.") { }
}
