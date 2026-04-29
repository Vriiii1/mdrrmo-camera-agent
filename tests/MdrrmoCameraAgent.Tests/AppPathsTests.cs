using FluentAssertions;
using MdrrmoCameraAgent;

namespace MdrrmoCameraAgent.Tests;

[Collection("SequentialIntegration")]
public class AppPathsTests
{
    [Fact]
    public void Root_IsUnderProgramData()
    {
        var root = AppPaths.Root;
        root.Should().EndWith(@"MDRRMO\CameraAgent");
        Path.IsPathRooted(root).Should().BeTrue();
    }

    [Fact]
    public void CredsFile_IsUnderRoot()
    {
        AppPaths.CredsFile.Should().Be(Path.Combine(AppPaths.Root, "creds.dpapi"));
    }

    [Fact]
    public void EnsureDirectoriesExist_CreatesRootAndSubdirs()
    {
        AppPaths.EnsureDirectoriesExist();
        Directory.Exists(AppPaths.Root).Should().BeTrue();
        Directory.Exists(AppPaths.MediaMtxDir).Should().BeTrue();
        Directory.Exists(AppPaths.LogsDir).Should().BeTrue();
    }
}
