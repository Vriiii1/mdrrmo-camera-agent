using System.Runtime.Versioning;
using System.Text.Json;
using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.Config;
using MdrrmoCameraAgent.Lifecycle;
using MdrrmoCameraAgent.Storage;
using Velopack;

namespace MdrrmoCameraAgent;

public static class Program
{
    public const string AgentName = "MdrrmoCameraAgent";

    /// Default install directory under %PROGRAMFILES%.
    private static string DefaultInstallDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "MDRRMO", "CameraAgent");

    // Sync entry point so VelopackApp.Build().Run() executes before the async
    // state machine — Velopack requires this to handle update-restart hooks.
    [SupportedOSPlatform("windows")]
    public static int Main(string[] args)
    {
        VelopackApp.Build().Run();
        return MainAsync(args).GetAwaiter().GetResult();
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> MainAsync(string[] args)
    {
        Console.WriteLine($"{AgentName} v{ThisAssembly.Version}");

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler? ctrlCHandler = null;
        ctrlCHandler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.CancelKeyPress -= ctrlCHandler;
        };
        Console.CancelKeyPress += ctrlCHandler;

        // ── install <bundle.json> ──────────────────────────────────────────────
        if (args is ["install", var bundlePath])
        {
            return await RunInstallAsync(bundlePath, cts.Token);
        }

        // ── uninstall ──────────────────────────────────────────────────────────
        if (args is ["uninstall"])
        {
            return await RunUninstallAsync(cts.Token);
        }

        // ── run  (used by winsw: <arguments>run</arguments>) ──────────────────
        if (args is ["run"])
        {
            return await RunAgentLoopAsync(bundlePath: null, cts.Token);
        }

        // ── enroll <bundle.json>  (backward-compat alias for 'run' one-shot) ──
        if (args is ["enroll", var enrollBundlePath])
        {
            return await RunAgentLoopAsync(enrollBundlePath, cts.Token);
        }

        Console.Error.WriteLine(
            "Usage:\n" +
            $"  {AgentName}.exe install   <bundle.json>   # install + start Windows service\n" +
            $"  {AgentName}.exe uninstall                 # stop + uninstall Windows service\n" +
            $"  {AgentName}.exe run                       # foreground heartbeat loop (used by winsw)\n" +
            $"  {AgentName}.exe enroll    <bundle.json>   # (legacy) foreground heartbeat loop");
        return 1;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Subcommand implementations
    // ─────────────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunInstallAsync(string bundlePath, CancellationToken ct)
    {
        Console.WriteLine($"[install] Loading bundle from: {bundlePath}");
        var bundle = ProvisioningLoader.LoadFromFile(bundlePath);

        var installDir = DefaultInstallDir;
        Console.WriteLine($"[install] Target directory: {installDir}");
        Directory.CreateDirectory(installDir);

        // 1. Copy the running binary, winsw, and service XML to the install dir.
        var exePath    = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Cannot resolve current executable path.");
        var exeDir     = Path.GetDirectoryName(exePath)
                         ?? throw new InvalidOperationException("Cannot resolve executable directory.");
        var winswSrc   = Path.Combine(exeDir, "winsw.exe");
        var xmlSrc     = Path.Combine(exeDir, "MdrrmoCameraAgent.xml");

        var exeDest    = Path.Combine(installDir, Path.GetFileName(exePath));
        var winswDest  = Path.Combine(installDir, "winsw.exe");
        var xmlDest    = Path.Combine(installDir, "MdrrmoCameraAgent.xml");

        // CopyIfDifferent: skip when source and destination resolve to the same
        // path. This makes the install subcommand idempotent when invoked from
        // an installer that has already extracted files into installDir (e.g. the
        // Inno Setup wizard at packaging/installer.iss [Files] step copies these
        // same files to {app}=installDir, then [Run] launches the agent from
        // {app}\MdrrmoCameraAgent.exe — self-copy would otherwise fail with
        // UnauthorizedAccessException because Windows refuses File.Copy of a
        // running executable over itself).
        CopyFileIfDifferent(exePath,   exeDest,   required: true,  label: "agent exe");
        CopyFileIfDifferent(winswSrc,  winswDest, required: false, label: "winsw.exe");
        CopyFileIfDifferent(xmlSrc,    xmlDest,   required: false, label: "MdrrmoCameraAgent.xml");

        // Copy LocalUi/wwwroot so the Kestrel UI can serve static files.
        var wwwSrc  = Path.Combine(exeDir, "LocalUi", "wwwroot");
        var wwwDest = Path.Combine(installDir, "LocalUi", "wwwroot");
        if (Directory.Exists(wwwSrc) &&
            !string.Equals(Path.GetFullPath(wwwSrc), Path.GetFullPath(wwwDest), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[install] Copying wwwroot -> {wwwDest}");
            CopyDirectory(wwwSrc, wwwDest);
        }
        else if (Directory.Exists(wwwSrc))
        {
            Console.WriteLine($"[install] wwwroot already at destination — skipping copy.");
        }

        // 2. Persist the bundle to the data directory so the service can find it
        //    on startup (winsw calls with just "run", no bundle path argument).
        AppPaths.EnsureDirectoriesExist();
        var bundleDestInData = Path.Combine(AppPaths.Root, "bundle.json");
        Console.WriteLine($"[install] Saving bundle to: {bundleDestInData}");
        File.Copy(bundlePath, bundleDestInData, overwrite: true);

        // 4. Pre-enroll so the service starts already enrolled (creds written to
        //    %PROGRAMDATA%\MDRRMO\CameraAgent via AppPaths.CredsFile / DpapiVault).
        Console.WriteLine("[install] Pre-enrolling agent...");
        AppPaths.EnsureDirectoriesExist();
        if (!DpapiVault.Exists(AppPaths.CredsFile))
        {
            using var apiHttp = new HttpClient { BaseAddress = new Uri(bundle.ApiBaseUrl!) };
            var enrollClient  = new EnrollmentClient(apiHttp);
            var creds = await enrollClient.EnrollAsync(
                bundle.EnrollmentToken!, Environment.MachineName, ThisAssembly.Version, ct);

            DpapiVault.WriteString(AppPaths.CredsFile, JsonSerializer.Serialize(creds));
            Console.WriteLine($"[install] Enrolled. Agent ID: {creds.AgentId}");
        }
        else
        {
            Console.WriteLine("[install] Credentials already exist; skipping enrollment.");
        }

        // 5. Register + start the service via winsw.
        if (!File.Exists(winswDest))
        {
            Console.Error.WriteLine("[install] ERROR: winsw.exe not present in install directory. Cannot register service.");
            return 1;
        }

        if (!File.Exists(xmlDest))
        {
            Console.Error.WriteLine("[install] ERROR: MdrrmoCameraAgent.xml not present in install directory. Cannot register service.");
            return 1;
        }

        Console.WriteLine("[install] Registering and starting Windows service...");
        var installer = new ServiceInstaller(new ProcessRunner());
        await installer.InstallAsync(winswDest, xmlDest, ct);

        Console.WriteLine("[install] Service installed and started successfully.");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunUninstallAsync(CancellationToken ct)
    {
        var installDir = DefaultInstallDir;
        var winswExe   = Path.Combine(installDir, "winsw.exe");
        var xmlPath    = Path.Combine(installDir, "MdrrmoCameraAgent.xml");

        if (!File.Exists(winswExe))
        {
            Console.Error.WriteLine($"[uninstall] winsw.exe not found at {winswExe}. Nothing to uninstall.");
            return 1;
        }

        Console.WriteLine("[uninstall] Stopping and removing Windows service...");
        var installer = new ServiceInstaller(new ProcessRunner());
        await installer.UninstallAsync(winswExe, xmlPath, ct);

        Console.WriteLine("[uninstall] Service uninstalled successfully.");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunAgentLoopAsync(string? bundlePath, CancellationToken ct)
    {
        ProvisioningBundle bundle;
        if (bundlePath is not null)
        {
            Console.WriteLine($"[run] Loading bundle from: {bundlePath}");
            bundle = ProvisioningLoader.LoadFromFile(bundlePath);
        }
        else
        {
            // Running as a Windows service (winsw calls with just "run").
            // The provisioning bundle was written to the data directory during install.
            var bundleInData = Path.Combine(AppPaths.Root, "bundle.json");
            if (!File.Exists(bundleInData))
                throw new FileNotFoundException(
                    $"Provisioning bundle not found at {bundleInData}. " +
                    "Run 'install <bundle.json>' first.", bundleInData);
            bundle = ProvisioningLoader.LoadFromFile(bundleInData);
        }

        var agent = new MinimumViableAgent(bundle, Environment.MachineName);

        Console.WriteLine("MDRRMO Camera Agent — heartbeat loop running. Ctrl-C to stop.");
        await agent.RunAsync(ct);
        Console.WriteLine("Stopped.");
        return 0;
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    // Copies src -> dest unless they already resolve to the same file. Required
    // when an external installer has already placed the file at dest (e.g. Inno
    // Setup's [Files] step) and then invokes us from that same location — the
    // running exe cannot be copied over itself.
    private static void CopyFileIfDifferent(string src, string dest, bool required, string label)
    {
        if (!File.Exists(src))
        {
            if (required)
                throw new FileNotFoundException(
                    $"required {label} not found at {src}", src);
            Console.Error.WriteLine($"[install] WARNING: {label} not found at {src}; skipping copy.");
            return;
        }

        var srcFull  = Path.GetFullPath(src);
        var destFull = Path.GetFullPath(dest);
        if (string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[install] {label} already at destination ({destFull}) — skipping copy.");
            return;
        }

        Console.WriteLine($"[install] Copying {srcFull} -> {destFull}");
        File.Copy(srcFull, destFull, overwrite: true);
    }
}

internal static class ThisAssembly
{
    public const string Version = "0.4.0";
}
