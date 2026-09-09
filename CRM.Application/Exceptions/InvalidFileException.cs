namespace CRM.Application.Exceptions;

/// <summary>
/// An uploaded file was refused: wrong type, empty, too large, or unnamed. The message is
/// written for the person who chose the file and is returned verbatim as the detail of a
/// 400 Bad Request (see GlobalExceptionHandler).
///
/// Derives from InvalidOperationException so a handler that has not been taught this type
/// keeps its existing behaviour — the DuplicateEmailException pattern.
/// </summary>
public sealed class InvalidFileException : InvalidOperationException
{
    public InvalidFileException(string message) : base(message) { }
}
