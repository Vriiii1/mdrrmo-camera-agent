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
              "enrollment_token":  "tok123",
              "hub_base_url":      "https://hub.example",
              "jwks_url":          "https://hub.example/.well-known/jwks.json",
              "api_base_url":      "https://api.example",
              "supabase_url":      "https://xyz.supabase.co",
              "supabase_anon_key": "anon-key-abc"
            }
            """);
        try
        {
            var bundle = ProvisioningLoader.LoadFromFile(path);
            bundle.EnrollmentToken.Should().Be("tok123");
            bundle.HubBaseUrl.Should().Be("https://hub.example");
            bundle.JwksUrl.Should().Be("https://hub.example/.well-known/jwks.json");
            bundle.ApiBaseUrl.Should().Be("https://api.example");
            bundle.SupabaseUrl.Should().Be("https://xyz.supabase.co");
            bundle.SupabaseAnonKey.Should().Be("anon-key-abc");
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
    public void Load_AcceptsBundle_WithoutOptionalHubFields()
    {
        // hub_base_url and jwks_url are optional (nullable); the loader must not throw when absent.
        // B5 smoke-test fix: removed the hub_base_url required-field check so W0b bundles work before
        // the MediaMTX hub is deployed.
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            {
              "enrollment_token":  "tok",
              "supabase_url":      "https://xyz.supabase.co",
              "supabase_anon_key": "anon-key"
            }
            """);
        try
        {
            var bundle = ProvisioningLoader.LoadFromFile(path);
            bundle.EnrollmentToken.Should().Be("tok");
            bundle.HubBaseUrl.Should().BeNullOrEmpty();
            bundle.JwksUrl.Should().BeNullOrEmpty();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ThrowsWhenSupabaseUrlMissing()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            {
              "enrollment_token":  "tok",
              "hub_base_url":      "https://hub.example",
              "supabase_anon_key": "anon-key"
            }
            """);
        try
        {
            var act = () => ProvisioningLoader.LoadFromFile(path);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*supabase_url*");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ThrowsWhenSupabaseAnonKeyMissing()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            {
              "enrollment_token": "tok",
              "hub_base_url":     "https://hub.example",
              "supabase_url":     "https://xyz.supabase.co"
            }
            """);
        try
        {
            var act = () => ProvisioningLoader.LoadFromFile(path);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*supabase_anon_key*");
        }
        finally { File.Delete(path); }
    }
}
