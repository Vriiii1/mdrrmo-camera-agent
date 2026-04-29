using FluentAssertions;
using MdrrmoCameraAgent.Config;

namespace MdrrmoCameraAgent.Tests.Config;

public class ProvisioningLoaderTests
{
    [Fact]
    public void Load_ParsesValidBundle()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            {
              "enrollment_token": "tok123",
              "hub_base_url":     "https://hub.example",
              "jwks_url":         "https://hub.example/.well-known/jwks.json"
            }
            """);
        try
        {
            var bundle = ProvisioningLoader.LoadFromFile(path);
            bundle.EnrollmentToken.Should().Be("tok123");
            bundle.HubBaseUrl.Should().Be("https://hub.example");
            bundle.JwksUrl.Should().Be("https://hub.example/.well-known/jwks.json");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ThrowsWhenTokenMissing()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{ "hub_base_url": "x" }""");
        try
        {
            var act = () => ProvisioningLoader.LoadFromFile(path);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*enrollment_token*");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ThrowsWhenHubBaseUrlMissing()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{ "enrollment_token": "tok" }""");
        try
        {
            var act = () => ProvisioningLoader.LoadFromFile(path);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*hub_base_url*");
        }
        finally { File.Delete(path); }
    }
}
