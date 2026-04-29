using System.Runtime.Versioning;
using FluentAssertions;
using MdrrmoCameraAgent.Storage;

namespace MdrrmoCameraAgent.Tests.Storage;

[SupportedOSPlatform("windows")]
public class DpapiVaultTests
{
    [Fact]
    public void RoundTrip_PreservesPlaintext()
    {
        var path = Path.GetTempFileName();
        try
        {
            DpapiVault.WriteString(path, "hello-secret-123");
            var roundTripped = DpapiVault.ReadString(path);
            roundTripped.Should().Be("hello-secret-123");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OnDiskBytes_AreNotPlaintext()
    {
        var path = Path.GetTempFileName();
        try
        {
            DpapiVault.WriteString(path, "needle");
            var raw = File.ReadAllBytes(path);
            System.Text.Encoding.UTF8.GetString(raw).Should().NotContain("needle");
        }
        finally { File.Delete(path); }
    }
}
