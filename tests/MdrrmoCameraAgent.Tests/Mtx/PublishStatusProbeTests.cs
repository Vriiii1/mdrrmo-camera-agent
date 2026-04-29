using FluentAssertions;
using MdrrmoCameraAgent.Mtx;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace MdrrmoCameraAgent.Tests.Mtx;

public class PublishStatusProbeTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(fn(req));
    }

    private static HttpClient ClientReturning(object body) =>
        new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(body)
        })) { BaseAddress = new Uri("http://127.0.0.1:9997") };

    private static object Path(string name, long bytesReceived, long bytesSent, int readerCount = 1) =>
        new
        {
            name,
            bytesReceived,
            bytesSent,
            readers = Enumerable.Range(0, readerCount).Select(_ => new { type = "rtspSession", id = Guid.NewGuid().ToString() }).ToArray(),
        };

    [Fact]
    public async Task FirstSnapshot_ReturnsUnknown_NoPriorBaseline()
    {
        var http = ClientReturning(new { items = new[] { Path("muni-x/cam-1", 1024L, 1024L) } });
        var probe = new PublishStatusProbe(http);

        var result = await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);

        result.Should().ContainKey("muni-x/cam-1");
        result["muni-x/cam-1"].Should().Be(PublishStatus.Unknown); // need 2 samples for a delta
    }

    [Fact]
    public async Task BothBytesIncreasing_AndReaderPresent_ReturnsPublishing()
    {
        var inBytes  = 0L;
        var outBytes = 0L;
        var http  = new HttpClient(new StubHandler(_ =>
        {
            inBytes  += 4096;
            outBytes += 4096;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { items = new[] { Path("muni-x/cam-1", inBytes, outBytes, readerCount: 1) } })
            };
        })) { BaseAddress = new Uri("http://127.0.0.1:9997") };

        var probe = new PublishStatusProbe(http);
        await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None); // baseline
        var result = await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);

        result["muni-x/cam-1"].Should().Be(PublishStatus.Publishing);
    }

    [Fact]
    public async Task BytesReceivedRising_ButBytesSentFlat_ReturnsDegradedAuth()
    {
        var inBytes = 0L;
        var http = new HttpClient(new StubHandler(_ =>
        {
            inBytes += 4096;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { items = new[] { Path("muni-x/cam-1", inBytes, bytesSent: 0L, readerCount: 0) } })
            };
        })) { BaseAddress = new Uri("http://127.0.0.1:9997") };

        var probe = new PublishStatusProbe(http);
        await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);
        var result = await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);

        result["muni-x/cam-1"].Should().Be(PublishStatus.DegradedAuth);
    }

    [Theory]
    [InlineData(1024L, 1024L)]  // both flat
    [InlineData(1024L, 2048L)]  // flat received, rising sent (camera dead)
    public async Task BytesReceivedFlat_ReturnsDegradedNoFrames_RegardlessOfBytesSent(
        long firstBytesSent, long secondBytesSent)
    {
        var callCount = 0;
        var http = new HttpClient(new StubHandler(_ =>
        {
            callCount++;
            var sent = callCount == 1 ? firstBytesSent : secondBytesSent;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { items = new[] { Path("muni-x/cam-1", bytesReceived: 1024L, bytesSent: sent) } })
            };
        })) { BaseAddress = new Uri("http://127.0.0.1:9997") };

        var probe = new PublishStatusProbe(http);
        await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);
        var result = await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);

        result["muni-x/cam-1"].Should().Be(PublishStatus.DegradedNoFrames);
    }

    [Fact]
    public async Task BothBytesIncreasing_ButNoReader_ReturnsDegradedAuth()
    {
        // Both legs show byte flow but readers == 0 means the WHIP sink
        // has disconnected (JWT rejected / ICE failure) despite bytes still
        // being counted in the outgoing queue. Per plan §T4 Step 4.
        var inBytes  = 0L;
        var outBytes = 0L;
        var http = new HttpClient(new StubHandler(_ =>
        {
            inBytes  += 4096;
            outBytes += 4096;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { items = new[] { Path("muni-x/cam-1", inBytes, outBytes, readerCount: 0) } })
            };
        })) { BaseAddress = new Uri("http://127.0.0.1:9997") };

        var probe = new PublishStatusProbe(http);
        await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None); // baseline
        var result = await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);

        result["muni-x/cam-1"].Should().Be(PublishStatus.DegradedAuth);
    }

    [Fact]
    public async Task PathMissingFromApi_ReturnsDegradedNoFrames()
    {
        var http  = ClientReturning(new { items = Array.Empty<object>() });
        var probe = new PublishStatusProbe(http);

        var result = await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);

        result["muni-x/cam-1"].Should().Be(PublishStatus.DegradedNoFrames);
    }

    [Fact]
    public async Task ApiUnreachable_ReturnsUnknownForAllCameras_DoesNotThrow()
    {
        var http = new HttpClient(new StubHandler(_ =>
            throw new HttpRequestException("connection refused")))
            { BaseAddress = new Uri("http://127.0.0.1:9997") };
        var probe = new PublishStatusProbe(http);

        var result = await probe.SampleAsync(new[] { "muni-x/cam-1", "muni-x/cam-2" }, CancellationToken.None);

        result["muni-x/cam-1"].Should().Be(PublishStatus.Unknown);
        result["muni-x/cam-2"].Should().Be(PublishStatus.Unknown);
    }
}
