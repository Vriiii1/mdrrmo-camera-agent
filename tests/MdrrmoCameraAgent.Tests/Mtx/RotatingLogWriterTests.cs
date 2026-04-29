using FluentAssertions;
using MdrrmoCameraAgent.Mtx;

namespace MdrrmoCameraAgent.Tests.Mtx;

public class RotatingLogWriterTests
{
    [Fact]
    public void Write_RotatesAtMaxSize_KeepingHistoricalCopies()
    {
        var dir  = Path.Combine(Path.GetTempPath(), $"mtx-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "mediamtx.log");

        try
        {
            using var w = new RotatingLogWriter(path, maxBytes: 100, maxFiles: 3);
            for (var i = 0; i < 10; i++)
                w.WriteLine(new string('x', 50)); // 50 bytes + newline

            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".1").Should().BeTrue();
            File.Exists(path + ".2").Should().BeTrue(
                "10 writes of 51 bytes with maxBytes=100 should produce at least 3 rotation events");
            File.Exists(path + ".4").Should().BeFalse(
                "maxFiles=3 means we never keep more than mediamtx.log + .1 + .2 + .3");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Dispose_FlushesAndReleasesFileHandle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtx-flush-{Guid.NewGuid():N}.log");
        using (var w = new RotatingLogWriter(path, maxBytes: 1_000_000, maxFiles: 1))
            w.WriteLine("hello");

        // Should be re-openable for reading without sharing-violation.
        File.ReadAllText(path).Should().Contain("hello");
        File.Delete(path);
    }
}
