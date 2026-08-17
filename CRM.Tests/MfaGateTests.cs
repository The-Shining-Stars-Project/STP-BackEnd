using CRM.Application.Auth;

namespace CRM.Tests;

/// <summary>
/// The mandatory-MFA decision, exhaustively. It is four booleans, so the truth table is
/// sixteen rows and there is no excuse for sampling it.
///
/// MfaEnforcementFilter is the adapter that feeds this function and turns a true into a 403;
/// that adapter is not covered here, because CRM.Tests references only CRM.Application and
/// CRM.Infrastructure and an integration test would need Microsoft.AspNetCore.Mvc.Testing —
/// a new package, which the project rules forbid without approval. The filter is verified by
/// the manual checklist in the handover notes instead.
/// </summary>
public class MfaGateTests
{
    [Theory]
    // required, authenticated, exempt, enrolled  ->  blocked
    [InlineData(true, true, false, false, true)]    // the whole point: enrol before you do anything
    [InlineData(true, true, false, true, false)]    // enrolled, carry on
    [InlineData(true, true, true, false, false)]    // the enrollment endpoints themselves
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, false, false)]  // anonymous: login must stay reachable
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, true, false, false, false)]  // Mfa:Required off — gate is inert
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, true, true, false)]
    public void Covers_the_whole_truth_table(
        bool required, bool authenticated, bool exempt, bool enrolled, bool expected) =>
        Assert.Equal(expected, MfaGate.ShouldBlock(required, authenticated, exempt, enrolled));

    [Fact]
    public void Blocks_exactly_one_combination()
    {
        // Stated as a property rather than a row: the gate closes on authenticated,
        // not-exempt, not-enrolled requests and on nothing else. If a future edit widens it,
        // this fails even if somebody also "fixed" the table above.
        var blocked = 0;
        foreach (var required in new[] { true, false })
        foreach (var authenticated in new[] { true, false })
        foreach (var exempt in new[] { true, false })
        foreach (var enrolled in new[] { true, false })
            if (MfaGate.ShouldBlock(required, authenticated, exempt, enrolled))
                blocked++;

        Assert.Equal(1, blocked);
    }
}
