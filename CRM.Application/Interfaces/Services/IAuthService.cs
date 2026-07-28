using CRM.Application.DTOs.Auth;

namespace CRM.Application.Interfaces.Services;

public interface IAuthService
{
    /// <summary>
    /// Validates credentials and returns a full session (JWT + rotating refresh token),
    /// or null if they are invalid / inactive.
    /// </summary>
    Task<AuthSessionDto?> LoginAsync(LoginDto dto);

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
