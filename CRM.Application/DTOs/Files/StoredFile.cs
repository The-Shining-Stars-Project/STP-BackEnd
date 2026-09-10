namespace CRM.Application.DTOs.Files;

/// <summary>
/// A stored attachment opened for download. The caller owns <see cref="Content"/> and must
/// dispose it — the API's File() result does so once the response has been written.
/// </summary>
public sealed record StoredFile(Stream Content, string FileName, string ContentType, long? Length);
