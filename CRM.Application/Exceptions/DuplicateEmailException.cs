namespace CRM.Application.Exceptions;

/// <summary>
/// The requested email address already belongs to an account.
///
/// Exists so the API can tell a genuine 409 Conflict apart from the other reasons
/// RegisterAsync throws — chiefly a password that fails validation. Both used to surface as
/// InvalidOperationException, the controller turned every one of them into a 409, and the
/// users screen showed "An account with that email already exists" for a password that was
/// simply too short. An admin creating a colleague's account was told to change the email,
/// which was the one thing that was fine.
///
/// Deliberately derives from InvalidOperationException: any handler that has not been taught
/// about this type — including GlobalExceptionHandler — keeps its existing behaviour.
/// </summary>
public sealed class DuplicateEmailException : InvalidOperationException
{
    public DuplicateEmailException(string email)
        : base($"A user with email '{email}' already exists.") { }
}
