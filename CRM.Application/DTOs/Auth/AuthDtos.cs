using System.ComponentModel.DataAnnotations;
using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Auth;

public class LoginDto
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class RegisterUserDto
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Staff;
    public Guid? StaffMemberId { get; set; }
}

public class UpdateUserDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? FullName { get; set; }

    public UserRole? Role { get; set; }
    public bool? IsActive { get; set; }
    public Guid? StaffMemberId { get; set; }
}

public class ResetPasswordDto
{
    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public Guid? StaffMemberId { get; set; }

    /// <summary>
    /// Whether this account has a confirmed second factor. Not a secret — knowing that an
    /// account has MFA helps nobody who is not already authenticated — and the admin user
    /// list needs it to show who is still unenrolled.
    /// </summary>
    public bool MfaEnabled { get; set; }
}

public class AuthResultDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public UserDto User { get; set; } = new();
}

/// <summary>
/// A full sign-in session: the JWT result plus the rotating refresh token (#15/#17).
/// The controller moves both credentials into httpOnly cookies; the refresh token must
/// never appear in a response body.
/// </summary>
public class AuthSessionDto
{
    public AuthResultDto Auth { get; set; } = new();
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime RefreshExpiresAt { get; set; }
}
