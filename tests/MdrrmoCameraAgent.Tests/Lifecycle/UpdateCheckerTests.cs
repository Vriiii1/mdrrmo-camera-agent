using System.Runtime.Versioning;
using FluentAssertions;
using MdrrmoCameraAgent.Lifecycle;
using Velopack.Exceptions;

namespace MdrrmoCameraAgent.Tests.Lifecycle;

/// <summary>
/// Verifies the Velopack-locator-missing guard added in v0.4.1 (Bug #3).
///
/// On Inno-installed agents, <see cref="Velopack.UpdateManager.CheckForUpdatesAsync"/>
/// throws because Velopack metadata is absent. Two real-world surfacings:
///
/// 1. <see cref="NotInstalledException"/> — observed on real Inno boxes in the field.
/// 2. <see cref="InvalidOperationException"/> with message
///    "No VelopackLocator has been set..." — observed in this very test environment
///    (out-of-process, VelopackApp.Build() never called).
///
/// Both are equally non-actionable; the agent's only recourse on an Inno install
/// is for a human to re-run the installer. The guard silently no-ops both.
/// Other exception types must NOT be swallowed (narrow catch, not a blanket eater).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task CheckAndApplyAsync_SwallowsLocatorMissingException_OnInnoInstall()
    {
        // The test runs out-of-process (no Velopack install metadata),
        // so UpdateManager.CheckForUpdatesAsync throws either
        // NotInstalledException (field-observed) or InvalidOperationException
        // ("No VelopackLocator has been set...") — observed in this test env.
        // The guard must catch both without surfacing.
        var checker = new UpdateChecker("https://invalid.local/no-feed/");

        var act = async () => await checker.CheckAndApplyAsync(CancellationToken.None);

        // Assert no Velopack-locator-missing surfacing reaches the caller.
        await act.Should().NotThrowAsync<NotInstalledException>();
        await act.Should().NotThrowAsync<InvalidOperationException>();
    }

    // Narrowness of the `when ex.Message.Contains("VelopackLocator")` filter
    // is enforced by code review, not unit test. A behavioral narrowness test
    // would require either (a) injecting an IUpdateManagerFactory seam to stub
    // a non-locator exception, or (b) a tautological filter-predicate test.
    // Both are over-engineered for a 4-line guard whose worst-case regression
    // is a single resumed [update-check] line in err.log every 6 hours —
    // loud, locally-fixable, and detected within one release cycle.
}
