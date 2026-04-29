namespace MdrrmoCameraAgent;

public static class Program
{
    public const string AgentName = "MdrrmoCameraAgent";

    public static int Main(string[] args)
    {
        Console.WriteLine($"{AgentName} v{ThisAssembly.Version}");
        return 0;
    }
}

internal static class ThisAssembly
{
    public const string Version = "0.0.1";
}
