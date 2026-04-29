using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using MdrrmoCameraAgent.Probe;

namespace MdrrmoCameraAgent.Tests.Probe;

public class RtspProbeTests
{
    /// <summary>
    /// Stand up a TcpListener, accept one connection, reply with RTSP 200 OK.
    /// </summary>
    [Fact]
    public async Task Probe_Returns_Ok()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[512];
            await stream.ReadAsync(buf);
            var response = Encoding.ASCII.GetBytes("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n");
            await stream.WriteAsync(response);
        });

        try
        {
            var uri    = new Uri($"rtsp://127.0.0.1:{port}/stream");
            var result = await RtspProbe.ProbeAsync(uri, TimeSpan.FromSeconds(5), default);
            result.Status.Should().Be(RtspProbeStatus.Ok);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    /// <summary>
    /// Same but server replies with 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task Probe_Returns_AuthRequired()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[512];
            await stream.ReadAsync(buf);
            var response = Encoding.ASCII.GetBytes("RTSP/1.0 401 Unauthorized\r\nCSeq: 1\r\n\r\n");
            await stream.WriteAsync(response);
        });

        try
        {
            var uri    = new Uri($"rtsp://127.0.0.1:{port}/stream");
            var result = await RtspProbe.ProbeAsync(uri, TimeSpan.FromSeconds(5), default);
            result.Status.Should().Be(RtspProbeStatus.AuthRequired);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    /// <summary>
    /// No server on the port — should return Unreachable.
    /// </summary>
    [Fact]
    public async Task Probe_Returns_Unreachable_When_NoServer()
    {
        // Bind and immediately close to get a port that is definitely free.
        var tmp = new TcpListener(IPAddress.Loopback, 0);
        tmp.Start();
        int port = ((IPEndPoint)tmp.LocalEndpoint).Port;
        tmp.Stop();

        var uri    = new Uri($"rtsp://127.0.0.1:{port}/stream");
        var result = await RtspProbe.ProbeAsync(uri, TimeSpan.FromSeconds(3), default);
        result.Status.Should().Be(RtspProbeStatus.Unreachable);
    }
}
