using System.Security.Claims;
using CRM.Application.DTOs.Auth;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CRM.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    /// <summary>Cookie holding the short-lived JWT (#15). httpOnly — invisible to XSS.</summary>
    public const string AccessCookie = "ss_access";
    /// <summary>Cookie holding the rotating refresh token (#17).</summary>
    public const string RefreshCookie = "ss_refresh";

    private readonly IAuthService _service;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthService service, IWebHostEnvironment env)
    {
        _service = service;
        _env = env;
    }

    /// <summary>
    /// Exchange email + password for a session. Credentials are delivered as httpOnly
    /// cookies; the body still includes the JWT for API clients (Swagger, scripts) and
    /// older frontend builds.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<AuthResultDto>> Login([FromBody] LoginDto dto)
    {
        var session = await _service.LoginAsync(dto);
        if (session is null)
            return Unauthorized(new { message = "Invalid email or password." });

        SetAuthCookies(session);
        return Ok(session.Auth);
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new JWT + rotated refresh token (#17).
    /// 401 when the cookie is missing/expired/revoked — the client should re-login.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<AuthResultDto>> Refresh()
    {
        var raw = Request.Cookies[RefreshCookie];
        var session = raw is null ? null : await _service.RefreshAsync(raw);
        if (session is null)
        {
            ClearAuthCookies();
            return Unauthorized(new { message = "Session expired. Please sign in again." });
        }

        SetAuthCookies(session);
        return Ok(session.Auth);
    }

    /// <summary>Revokes the refresh token and clears both auth cookies.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies[RefreshCookie] is { } raw)
            await _service.LogoutAsync(raw);

        ClearAuthCookies();
        return NoContent();
    }

    private void SetAuthCookies(AuthSessionDto session)
    {
        // SameSite=Lax works because the frontend proxies API calls through its own
        // origin (Next.js rewrite), making these first-party cookies. Secure is relaxed
        // only for local http development.
        var secure = !_env.IsDevelopment();
        Response.Cookies.Append(AccessCookie, session.Auth.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            // Outlives the JWT inside it so an expired JWT still reaches the server
            // and 401s, which is the frontend's cue to call refresh.
            Expires = session.RefreshExpiresAt,
        });
        Response.Cookies.Append(RefreshCookie, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = session.RefreshExpiresAt,
        });
    }

    private void ClearAuthCookies()
    {
        Response.Cookies.Delete(AccessCookie, new CookieOptions { Path = "/" });
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/" });
    }

    /// <summary>Returns the currently authenticated user.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(idClaim, out var id))
            return Unauthorized();

        var user = await _service.GetByIdAsync(id);
        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>Lists all users. Admin only.</summary>
    [HttpGet("users")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetUsers() =>
        Ok(await _service.GetAllAsync());

    /// <summary>Creates a new user account. Admin only.</summary>
    [HttpPost("register")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserDto>> Register([FromBody] RegisterUserDto dto)
    {
        try
        {
            var created = await _service.RegisterAsync(dto);
            return CreatedAtAction(nameof(Me), new { }, created);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Updates a user's name, role, or active state. Admin only.</summary>
    [HttpPut("users/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserDto>> UpdateUser(Guid id, [FromBody] UpdateUserDto dto)
    {
        try
        {
            var updated = await _service.UpdateUserAsync(id, dto, User.GetUserId());
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Sets a new password for a user. Admin only.</summary>
    [HttpPost("users/{id:guid}/reset-password")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordDto dto)
    {
        try
        {
            var ok = await _service.ResetPasswordAsync(id, dto);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Changes the signed-in user's own password. Their other sessions are revoked (#5);
    /// this one survives, which is why the refresh cookie is passed through.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        try
        {
            var ok = await _service.ChangePasswordAsync(
                User.GetUserId(), dto, Request.Cookies[RefreshCookie]);
            return ok ? NoContent() : Unauthorized();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Deletes a user. Admin only.</summary>
    [HttpDelete("users/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            var ok = await _service.DeleteUserAsync(id, User.GetUserId());
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
