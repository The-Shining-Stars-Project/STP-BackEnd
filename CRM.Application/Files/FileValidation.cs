using CRM.Application.Exceptions;

namespace CRM.Application.Files;

/// <summary>
/// The rules every uploaded attachment passes before a byte reaches storage: a size ceiling,
/// an allowed-type list checked by extension AND by file header (so a Word document renamed
/// to .pdf is refused rather than stored and served as application/pdf), and a file name that
/// is safe to put in a Content-Disposition header. Scripts keep their own PDF-only copy of
/// these rules; this is the shared version for star and staff paperwork, which arrives as
/// scans (PDF or a phone photo) far more often than as clean PDFs.
/// </summary>
public static class FileValidation
{
    /// <summary>25 MB — a multi-page scan at phone resolution is a few MB; this leaves room.</summary>
    public const long DefaultMaxBytes = 25L * 1024 * 1024;

    private const int MaxFileNameLength = 255;

    public sealed record Kind(string Extension, string ContentType, byte[] Magic);

    public static readonly Kind Pdf = new(".pdf", "application/pdf", "%PDF-"u8.ToArray());
    public static readonly Kind Png = new(".png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    public static readonly Kind Jpeg = new(".jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF });

    /// <summary>PDF plus the two image formats phone cameras and scanners actually produce.</summary>
    public static readonly IReadOnlyList<Kind> Documents = new[] { Pdf, Png, Jpeg };

    public sealed record Result(string FileName, string ContentType, string Extension, long Length);

    /// <summary>
    /// Validates and returns the sanitised name, the content type to store, and the length.
    /// Rewinds <paramref name="content"/> to the start so the upload that follows sends the
    /// whole file. Throws <see cref="InvalidFileException"/> (→ 400) on any rule failure.
    /// </summary>
    public static Result Validate(Stream content, string? fileName, IReadOnlyList<Kind> allowed, long maxBytes = DefaultMaxBytes)
    {
        if (!content.CanSeek)
            throw new ArgumentException("Upload content must be a seekable stream.", nameof(content));

        var length = content.Length;
        if (length <= 0)
            throw new InvalidFileException("The uploaded file is empty.");
        if (length > maxBytes)
            throw new InvalidFileException(
                $"That file is {length / (1024d * 1024):0.#} MB; the limit is {maxBytes / (1024 * 1024)} MB.");

        var name = SanitizeFileName(fileName);
        if (name.Length == 0)
            throw new InvalidFileException("The uploaded file has no name.");

        // .jpeg is the same format as .jpg; accept the spelling, store the canonical one.
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext == ".jpeg") ext = ".jpg";
        var kind = allowed.FirstOrDefault(k => k.Extension == ext);
        if (kind is null)
            throw new InvalidFileException(
                $"Only {string.Join(", ", allowed.Select(k => k.Extension.TrimStart('.').ToUpperInvariant()))} files can be attached.");

        if (name.Length > MaxFileNameLength)
            name = name[..(MaxFileNameLength - ext.Length)] + ext;

        content.Position = 0;
        Span<byte> header = stackalloc byte[8];
        var read = content.ReadAtLeast(header, kind.Magic.Length, throwOnEndOfStream: false);
        content.Position = 0;
        if (read < kind.Magic.Length || !header[..kind.Magic.Length].SequenceEqual(kind.Magic))
            throw new InvalidFileException(
                $"That file is not a {kind.Extension.TrimStart('.').ToUpperInvariant()} — its name says so but its contents do not.");

        return new Result(name, kind.ContentType, kind.Extension, length);
    }

    /// <summary>
    /// Keeps only the final path segment (browsers send a bare name; older clients and some
    /// tools send a full path, with either separator) and drops control characters.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        var raw = (fileName ?? string.Empty).Trim();
        var cut = raw.LastIndexOfAny(['/', '\\']);
        if (cut >= 0) raw = raw[(cut + 1)..];
        return new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
    }
}
