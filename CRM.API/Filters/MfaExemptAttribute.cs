namespace CRM.API.Filters;

/// <summary>
/// Marks an endpoint reachable by an authenticated user who has NOT yet enrolled a second
/// factor. <see cref="MfaEnforcementFilter"/> blocks everything else.
///
/// Keep this list as short as it can be: it is the complete set of things somebody with a
/// stolen password but no second factor can still do. Today that is enrollment itself,
/// /api/auth/me (the frontend needs to know who it is talking to in order to show the
/// enrollment page), and the MFA status check. Logout needs no marker — it is
/// [AllowAnonymous], which the filter already treats as exempt.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MfaExemptAttribute : Attribute
{
}
