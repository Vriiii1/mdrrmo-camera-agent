using System.Runtime.Versioning;
using MdrrmoCameraAgent.Config;
using MdrrmoCameraAgent.Lifecycle;

namespace MdrrmoCameraAgent;

public static class Program
{
    public const string AgentName = "MdrrmoCameraAgent";

    [SupportedOSPlatform("windows")]
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"{AgentName} v{ThisAssembly.Version}");

        if (args is ["enroll", var bundlePath])
        {
            var bundle = ProvisioningLoader.LoadFromFile(bundlePath);
            var agent  = new MinimumViableAgent(bundle, Environment.MachineName);

            using var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler? handler = null;
            handler = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.CancelKeyPress -= handler;
            };
            Console.CancelKeyPress += handler;

            Console.WriteLine("MDRRMO Camera Agent — heartbeat loop running. Ctrl-C to stop.");
            await agent.RunAsync(cts.Token);
            Console.WriteLine("Stopped.");
            return 0;
        }

        Console.WriteLine("Usage: MdrrmoCameraAgent.exe enroll <bundle.json>");
        return 1;
    }
}

internal static class ThisAssembly
{
    public const string Version = "0.0.1";
}
