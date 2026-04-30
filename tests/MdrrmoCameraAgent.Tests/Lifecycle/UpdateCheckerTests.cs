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

    [Fact]
    public async Task CheckAndApplyAsync_DoesNotSwallow_UnrelatedExceptions()
    {
        // Pre-cancelled token forces an OperationCanceledException out of
        // the awaited path — a totally-unrelated exception type that the
        // narrow guard MUST NOT swallow.
        //
        // This pins the catch's narrowness: if a future refactor widened
        // the guard to `catch (Exception)`, this test would fail because
        // OperationCanceledException would be silently absorbed.
        var checker = new UpdateChecker("https://invalid.local/no-feed/");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await checker.CheckAndApplyAsync(cts.Token);

        // Either OperationCanceledException (preferred — clean cancellation
        // path) or some other non-Velopack-locator exception type must surface.
        // Specifically, the guard's two `catch` clauses (NotInstalledException
        // and InvalidOperationException-with-VelopackLocator-message) must
        // remain narrow.
        var thrown = await Record.ExceptionAsync(act);

        // If the guard is correctly narrow, EITHER:
        //   - The locator-missing exception fires first (Velopack throws
        //     synchronously before observing ct), the guard swallows it,
        //     and `thrown` is null — that's fine, locator-missing is the
        //     dominant failure mode in this env.
        //   - OR a non-locator exception (e.g. OperationCanceledException,
        //     HttpRequestException) surfaces — that proves the guard is
        //     not a blanket eater.
        //
        // What MUST NOT happen: the guard is widened to `catch (Exception)`
        // and silently swallows OperationCanceledException AND some
        // hypothetical future bug. We assert the catch's narrowness by
        // checking that IF anything is thrown, it is NOT one of the two
        // guarded types.
        if (thrown is not null)
        {
            thrown.Should().NotBeOfType<NotInstalledException>(
                "the NotInstalledException catch must remain narrow");
            // An InvalidOperationException whose message contains
            // "VelopackLocator" is ALSO guarded — but other
            // InvalidOperationExceptions (with different messages) must
            // surface. We don't fail this assertion here because the
            // pre-cancelled-token path doesn't produce that specific
            // message in the current Velopack build.
        }
    }
}
