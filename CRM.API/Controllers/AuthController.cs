using System.Security.Claims;
using CRM.API.Filters;
using CRM.Application.DTOs.Auth;
using CRM.Application.Exceptions;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CRM.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    /// <summary>Cookie holding the short-lived JWT (#15). httpOnly — invisible to XSS.</summary>
    public const string AccessCookie = "ss_access";
    /// <summary>Cookie holding the rotating refresh token (#17).</summary>
    public const string RefreshCookie = "ss_refresh";
    /// <summary>
    /// Cookie carrying the MFA challenge between the password step and the code step. It is
    /// NOT a session: on its own it grants access to nothing except POST /api/auth/login/mfa.
    /// </summary>
    public const string MfaCookie = "ss_mfa";

    /// <summary>The one response every failed code submission gets, whatever went wrong.</summary>
    private const string GenericCodeFailure = "Invalid or expired code.";

    private readonly IAuthService _service;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthService service, IWebHostEnvironment env)
    {
        _service = service;
        _env = env;
    }

    /// <summary>
    /// The password step. For an account without a second factor this completes the sign-in
    /// and sets the auth cookies. For an enrolled account it sets NOTHING except a short-lived
    /// challenge cookie and returns <c>{ mfaRequired: true }</c> — the caller must then post a
    /// code to <c>/api/auth/login/mfa</c>.
    ///
    /// BREAKING CHANGE: this used to return AuthResultDto directly. It now returns
    /// LoginResponseDto, so the JWT moved from <c>.token</c> to <c>.auth.token</c>.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginDto dto)
    {
        var outcome = await _service.LoginAsync(dto);

        if (!outcome.Authenticated)
            return Unauthorized(new { message = "Invalid email or password." });

        if (outcome.MfaRequired)
        {
            SetMfaCookie(outcome.ChallengeToken!, outcome.ChallengeExpiresAt!.Value);
            // No auth cookies, and any existing ones are deliberately left alone — clearing
            // them would hand anyone with a stolen password a free way to log the real user
            // out just by submitting it.
            return Ok(new LoginResponseDto { MfaRequired = true });
        }

        SetAuthCookies(outcome.Session!);
        // A stale challenge cookie from an abandoned attempt would otherwise sit there until
        // it expired, and a browser that already holds a session has no use for one.
        ClearMfaCookie();
        return Ok(new LoginResponseDto { MfaRequired = false, Auth = outcome.Session!.Auth });
    }

    /// <summary>
    /// The code step: exchanges the challenge cookie plus a TOTP or recovery code for a real
    /// session. Rate-limited on its own budget rather than sharing the password step's: one
    /// sign-in spends up to MfaChallenge.MaxAttempts requests here for a single request there,
    /// so sharing let a few typos exhaust the whole organisation's ability to sign in.
    ///
    /// Every failure returns the identical 401. Distinguishing "wrong code" from "expired"
    /// from "out of attempts" would tell an attacker how much of the challenge they had left.
    /// </summary>
    [HttpPost("login/mfa")]
    [AllowAnonymous]
    [EnableRateLimiting("login-mfa")]
    public async Task<ActionResult<LoginResponseDto>> LoginMfa([FromBody] MfaVerifyDto dto)
    {
        MfaVerifyOutcome outcome;
        try
        {
            outcome = await _service.VerifyMfaAsync(Request.Cookies[MfaCookie], dto.Code);
        }
        catch (DbUpdateConcurrencyException)
        {
            // User.RowVersion (#26) plus the LastTotpStep write means a double-submitted code
            // — a double-click, or the frontend retrying — collides with itself. Caught here
            // rather than in AuthService because CRM.Application has no EF reference. Left to
            // GlobalExceptionHandler it would surface as a 409 "This item was changed by
            // someone else while you were editing", which on a login screen is both baffling
            // and distinguishable: it would tell the caller their code got as far as the user
            // write, i.e. that it was correct.
            return Unauthorized(new { message = GenericCodeFailure });
        }

        if (outcome.Session is null)
        {
            // The challenge survives only while attempts remain, so a mistyped digit leaves
            // the user on the code screen instead of sending them back to the password.
            if (!outcome.ChallengeSurvives) ClearMfaCookie();
            return Unauthorized(new { message = GenericCodeFailure });
        }

        SetAuthCookies(outcome.Session);
        ClearMfaCookie();
        return Ok(new LoginResponseDto { MfaRequired = false, Auth = outcome.Session.Auth });
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

    /// <summary>Revokes the refresh token and clears every auth cookie, challenge included.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies[RefreshCookie] is { } raw)
            await _service.LogoutAsync(raw);

        ClearAuthCookies();
        // Logout is the one place that SHOULD kill a half-finished challenge: the user asked
        // to stop. ClearAuthCookies must not do this on its own — see below.
        ClearMfaCookie();
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

    /// <summary>
    /// The challenge cookie. Same flags as the auth cookies, but it expires with the
    /// challenge — five minutes, not fourteen days.
    /// </summary>
    private void SetMfaCookie(string token, DateTime expiresAt)
    {
        Response.Cookies.Append(MfaCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt,
        });
    }

    private void ClearAuthCookies()
    {
        Response.Cookies.Delete(AccessCookie, new CookieOptions { Path = "/" });
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/" });
    }

    /// <summary>
    /// Deliberately separate from <see cref="ClearAuthCookies"/>, and it must stay that way.
    /// The frontend's apiFetch fires a silent refresh on any 401, and a user sitting on the
    /// code screen has no refresh token yet — so that refresh 401s and calls ClearAuthCookies.
    /// If that also dropped ss_mfa, the challenge would die underneath the user for no reason
    /// they could see, mid-login.
    /// </summary>
    private void ClearMfaCookie() =>
        Response.Cookies.Delete(MfaCookie, new CookieOptions { Path = "/" });

    /// <summary>Returns the currently authenticated user.</summary>
    // Exempt from the MFA gate: the frontend calls this to find out who it is talking to, and
    // it has to work before enrollment or the enrollment page cannot render.
    [HttpGet("me")]
    [Authorize]
    [MfaExempt]
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
        catch (DuplicateEmailException ex)
        {
            // 409 belongs to the one case it describes: the address is taken.
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Everything else RegisterAsync rejects is a bad field, not a collision —
            // a password under 8 characters, or without a letter and a digit. Returning
            // 409 for those made the users screen blame the email address instead.
            return BadRequest(new { message = ex.Message });
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

    // =====================================================================================
    // Multi-factor authentication
    //
    // Every action here catches InvalidOperationException explicitly. Left to
    // GlobalExceptionHandler it becomes a 409 with the wrong body, which for "current password
    // is incorrect" on the disable endpoint is actively misleading.
    //
    // The enrollment endpoints carry [MfaExempt] because a user who has not enrolled must be
    // able to reach them — that is the entire escape hatch from the gate. The endpoints that
    // require an existing enrollment do not, because their caller is enrolled by definition.
    // =====================================================================================

    /// <summary>Whether the caller has a second factor, and how many recovery codes are left.</summary>
    [HttpGet("mfa/status")]
    [Authorize]
    [MfaExempt]
    public async Task<ActionResult<MfaStatusDto>> MfaStatus()
    {
        var status = await _service.GetMfaStatusAsync(User.GetUserId());
        return status is null ? Unauthorized() : Ok(status);
    }

    /// <summary>
    /// Begins enrollment: returns the base32 secret for manual entry plus the otpauth:// URI,
    /// which on a phone opens the authenticator directly. Nothing is enabled until
    /// <see cref="MfaEnable"/> confirms a code.
    ///
    /// 409 if MFA is already on — a stray call must not clobber a working enrollment.
    /// </summary>
    [HttpPost("mfa/setup")]
    [Authorize]
    [MfaExempt]
    [EnableRateLimiting("mfa-manage")]
    public async Task<ActionResult<MfaSetupResultDto>> MfaSetup()
    {
        try
        {
            var result = await _service.SetupMfaAsync(User.GetUserId());
            return result is null ? Unauthorized() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Confirms enrollment with a code from the authenticator and returns the ten recovery
    /// codes. THIS IS THE ONLY TIME THEY ARE EVER SHOWN — there is no endpoint that retrieves
    /// them again, only one that replaces them.
    ///
    /// Every pre-existing session is revoked (they never presented a second factor) and this
    /// browser gets a fresh pair of cookies carrying mfa:true.
    /// </summary>
    [HttpPost("mfa/enable")]
    [Authorize]
    [MfaExempt]
    [EnableRateLimiting("mfa-manage")]
    public async Task<ActionResult<MfaEnableResultDto>> MfaEnable([FromBody] MfaEnableDto dto)
    {
        try
        {
            var outcome = await _service.EnableMfaAsync(User.GetUserId(), dto.Code);
            if (outcome is null) return Unauthorized();

            SetAuthCookies(outcome.Session);
            return Ok(new MfaEnableResultDto { RecoveryCodes = outcome.RecoveryCodes });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "That request collided with another. Try again." });
        }
    }

    /// <summary>
    /// Issues ten fresh recovery codes and destroys the old ten. Requires a current code, so
    /// a hijacked session cannot mint itself a permanent way back in.
    /// </summary>
    [HttpPost("mfa/recovery-codes")]
    [Authorize]
    [EnableRateLimiting("mfa-manage")]
    public async Task<ActionResult<MfaEnableResultDto>> MfaRegenerateRecoveryCodes([FromBody] MfaVerifyDto dto)
    {
        try
        {
            var codes = await _service.RegenerateRecoveryCodesAsync(User.GetUserId(), dto.Code);
            return codes is null
                ? Unauthorized()
                : Ok(new MfaEnableResultDto { RecoveryCodes = codes });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "That request collided with another. Try again." });
        }
    }

    /// <summary>
    /// Removes the caller's authenticator. Requires their password AND a current code.
    ///
    /// NAME IT "RESET MY AUTHENTICATOR APP" IN THE UI, not "turn off two-factor". While
    /// Mfa:Required is true this does not disable anything: clearing enrollment means the very
    /// next request is refused by the MFA gate, and the user is confined to the enrollment
    /// endpoints until they set up a new authenticator. That is the correct behaviour for the
    /// case it exists for — a new phone — but labelling it "disable" guarantees a bug report.
    /// </summary>
    [HttpPost("mfa/disable")]
    [Authorize]
    [EnableRateLimiting("mfa-manage")]
    public async Task<IActionResult> MfaDisable([FromBody] MfaDisableDto dto)
    {
        try
        {
            var session = await _service.DisableMfaAsync(User.GetUserId(), dto.CurrentPassword, dto.Code);
            if (session is null) return Unauthorized();

            SetAuthCookies(session);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "That request collided with another. Try again." });
        }
    }

    /// <summary>
    /// Clears a user's second factor. Admin only, for the lost-phone case.
    ///
    /// Revokes the target's sessions and their outstanding challenges, and returns nothing
    /// about the secret — an admin who could read it could impersonate the user indefinitely.
    /// The target must re-enroll before they can use the app again.
    ///
    /// Targeting your OWN account additionally requires currentPassword in the body: an admin
    /// session alone must not be able to strip its own second factor, or a stolen cookie
    /// walks straight around the password-AND-code guard on /mfa/disable.
    /// </summary>
    [HttpPost("users/{id:guid}/mfa/reset")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("mfa-manage")]
    public async Task<IActionResult> AdminResetMfa(Guid id, [FromBody] AdminResetMfaDto? dto = null)
    {
        try
        {
            var ok = await _service.AdminResetMfaAsync(id, User.GetUserId(), dto?.CurrentPassword);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
