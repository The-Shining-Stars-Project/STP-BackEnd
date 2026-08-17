using CRM.Application.DTOs.Auth;

namespace CRM.Application.Interfaces.Services;

public interface IAuthService
{
    /// <summary>
    /// The password step. Returns a full session for an unenrolled user, an MFA challenge for
    /// an enrolled one, or <see cref="LoginOutcome.Failed"/>.
    ///
    /// A correct password for an MFA-enrolled account issues NO session and sets NO auth
    /// cookies — that is what makes the second factor mandatory rather than advisory.
    /// </summary>
    Task<LoginOutcome> LoginAsync(LoginDto dto);

    /// <summary>
    /// The code step: exchanges a challenge token plus a TOTP or recovery code for a real
    /// session. Every failure is indistinguishable to the caller; the reason is recorded
    /// server-side.
    /// </summary>
    Task<MfaVerifyOutcome> VerifyMfaAsync(string? challengeToken, string code);

    /// <summary>
    /// Starts enrollment: generates a secret and stores it unconfirmed. Throws
    /// InvalidOperationException if MFA is already enabled, so a stray call cannot clobber a
    /// working enrollment. Returns null if the user is gone.
    /// </summary>
    Task<MfaSetupResultDto?> SetupMfaAsync(Guid userId);

    /// <summary>
    /// Confirms enrollment with a code from the authenticator, issues ten recovery codes, and
    /// revokes every existing refresh token before minting a fresh session — a token created
    /// before enrollment represents a session that never presented a second factor.
    /// Throws InvalidOperationException when there is no pending secret or the code is wrong.
    /// </summary>
    Task<MfaEnableOutcome?> EnableMfaAsync(Guid userId, string code);

    /// <summary>Replaces all ten recovery codes. Requires a valid current code.</summary>
    Task<IReadOnlyList<string>?> RegenerateRecoveryCodesAsync(Guid userId, string code);

    /// <summary>
    /// Clears the user's second factor. Requires their password AND a valid code, revokes
    /// every session, and re-mints the caller's. Under Mfa:Required this does not turn MFA
    /// off — it resets the authenticator, and the gate confines the user to re-enrollment.
    /// </summary>
    Task<AuthSessionDto?> DisableMfaAsync(Guid userId, string currentPassword, string code);

    /// <summary>
    /// Admin recovery for a lost phone: clears the target's secret, recovery codes and
    /// outstanding challenges, and revokes their sessions. Reveals nothing about the secret.
    /// </summary>
    Task<bool> AdminResetMfaAsync(Guid targetUserId);

    Task<MfaStatusDto?> GetMfaStatusAsync(Guid userId);

    /// <summary>
    /// Exchanges a valid refresh token for a new session, rotating the token (the old one
    /// is revoked). Returns null if the token is unknown, expired, revoked, or the user is
    /// inactive (#17/#20).
    /// </summary>
    Task<AuthSessionDto?> RefreshAsync(string refreshToken);

    /// <summary>Revokes a refresh token (sign-out). Unknown tokens are ignored.</summary>
    Task LogoutAsync(string refreshToken);

    /// <summary>Creates a new user. Throws InvalidOperationException if the email is already taken.</summary>
    Task<UserDto> RegisterAsync(RegisterUserDto dto);

    Task<UserDto?> GetByIdAsync(Guid id);

    Task<IReadOnlyList<UserDto>> GetAllAsync();

    /// <summary>
    /// Updates a user's profile/role/active state. <paramref name="actingUserId"/> is the
    /// admin making the change; used to prevent self-lockout. Returns null if not found,
    /// throws InvalidOperationException on a guard violation (e.g. removing the last admin).
    /// </summary>
    Task<UserDto?> UpdateUserAsync(Guid id, UpdateUserDto dto, Guid actingUserId);

    /// <summary>
    /// Sets a new password for a user (admin action) and revokes every one of their active
    /// sessions (#5). Returns false if the user is not found.
    /// </summary>
    Task<bool> ResetPasswordAsync(Guid id, ResetPasswordDto dto);

    /// <summary>
    /// Self-service password change: verifies the caller's current password before
    /// setting the new one, then revokes their other sessions (#5). Pass the caller's own
    /// refresh token as <paramref name="currentRefreshToken"/> to spare the session making
    /// the change; omit it to sign the user out everywhere. Returns false if not found;
    /// throws InvalidOperationException if the current password is wrong or the new
    /// password is invalid.
    /// </summary>
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto dto, string? currentRefreshToken = null);

    /// <summary>Deletes a user. Returns false if not found; throws on a guard violation.</summary>
    Task<bool> DeleteUserAsync(Guid id, Guid actingUserId);
}
