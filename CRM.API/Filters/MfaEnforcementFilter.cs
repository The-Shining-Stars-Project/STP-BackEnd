using CRM.Application.Auth;
using CRM.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CRM.API.Filters;

/// <summary>
/// Makes MFA mandatory: an authenticated user without an enrolled second factor gets 403 on
/// every endpoint except the handful marked <see cref="MfaExemptAttribute"/>.
///
/// WHY A FILTER AND NOT AN AUTHORIZATION POLICY. The obvious implementation — add the
/// requirement to AuthorizationOptions.DefaultPolicy — does not work, and fails in the worst
/// possible way: silently, on exactly the endpoints that matter. DefaultPolicy applies only
/// to a bare [Authorize]. An endpoint carrying [Authorize(Roles = "Admin")] or
/// [Authorize(Policy = "ManagementWrite")] builds its own policy and never consults the
/// default, so the gate would have been wide open on every admin endpoint and every
/// management-write endpoint while appearing to work on the read endpoints. FallbackPolicy is
/// no better — it applies only where there is no authorization metadata at all.
///
/// A globally-registered MVC authorization filter has no such hole. Since ASP.NET Core 3.0
/// the [Authorize] metadata is enforced by AuthorizationMiddleware BEFORE MVC filters run, so
/// by the time this executes, authentication and every role/policy check have already passed.
/// It therefore sees precisely the set of requests that would otherwise have been served,
/// whichever flavour of [Authorize] the endpoint used.
///
/// If you are here to "fix" the missing policy in Program.cs's AddAuthorization block: don't.
/// </summary>
public sealed class MfaEnforcementFilter : IAsyncAuthorizationFilter
{
    /// <summary>
    /// HttpContext.Items key holding the DATABASE-FRESH enrollment flag, stamped by
    /// OnTokenValidated. The JWT's "mfa" claim is not used here: it is fixed at mint time and
    /// stale for up to the token lifetime, so after an admin MFA reset the victim's (or
    /// thief's) outstanding access cookie would keep sailing through this gate for an hour —
    /// which is the exact scenario the reset exists to handle.
    /// </summary>
    public const string EnrollmentKey = "ss.mfa.enrolled";

    private readonly MfaSettings _settings;

    public MfaEnforcementFilter(IOptions<MfaSettings> settings) => _settings = settings.Value;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authenticated = context.HttpContext.User.Identity?.IsAuthenticated == true;

        var exempt = context.ActionDescriptor.EndpointMetadata
            .Any(m => m is IAllowAnonymous or MfaExemptAttribute);

        // Fail closed. A missing key can only mean authentication came from a path that
        // skipped OnTokenValidated, and "we could not tell" must not read as "enrolled".
        var enrolled = context.HttpContext.Items.TryGetValue(EnrollmentKey, out var value)
                       && value is true;

        if (!MfaGate.ShouldBlock(_settings.Required, authenticated, exempt, enrolled))
            return Task.CompletedTask;

        // The code field is load-bearing: the frontend has to tell "go and enroll" apart from
        // an ordinary role-based 403, and matching on a message string is not a contract.
        context.Result = new ObjectResult(new
        {
            message = "Two-factor authentication setup is required before you can use the app.",
            code = "mfa_enrollment_required",
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };

        return Task.CompletedTask;
    }
}
