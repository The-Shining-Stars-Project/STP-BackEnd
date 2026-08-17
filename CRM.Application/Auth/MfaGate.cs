namespace CRM.Application.Auth;

/// <summary>
/// The whole decision behind mandatory MFA, as a pure function.
///
/// It lives here rather than inside the API filter that calls it for one reason: CRM.Tests
/// references only CRM.Application and CRM.Infrastructure, so a rule buried in an ASP.NET
/// filter would be untestable without adding Microsoft.AspNetCore.Mvc.Testing. Splitting the
/// decision from the plumbing means the part with the security judgement in it is covered by
/// MfaGateTests, and the part left in CRM.API is a five-line adapter.
/// </summary>
public static class MfaGate
{
    /// <summary>
    /// Whether a request that has already passed authentication and authorization should
    /// nonetheless be refused because the caller has not enrolled a second factor.
    /// </summary>
    /// <param name="mfaRequired">Config: Mfa:Required. False turns the gate off entirely.</param>
    /// <param name="isAuthenticated">
    /// Anonymous requests are none of this rule's business — login itself must stay reachable.
    /// </param>
    /// <param name="isExempt">
    /// The endpoint is one of the few a not-yet-enrolled user must reach: the enrollment
    /// endpoints themselves, /api/auth/me, and logout. Without these the gate would lock out
    /// every user who has not enrolled, including the one trying to.
    /// </param>
    /// <param name="isEnrolled">
    /// Read from the database on this request, not from the JWT. The token's mfa claim is up
    /// to an hour stale, and stale in the dangerous direction: after an admin MFA reset the
    /// outstanding access cookie still says true.
    /// </param>
    public static bool ShouldBlock(bool mfaRequired, bool isAuthenticated, bool isExempt, bool isEnrolled) =>
        mfaRequired && isAuthenticated && !isExempt && !isEnrolled;
}
