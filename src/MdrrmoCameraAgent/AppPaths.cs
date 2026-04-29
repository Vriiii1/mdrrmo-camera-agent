namespace MdrrmoCameraAgent;

public static class AppPaths
{
    public static string Root =>
        Environment.GetEnvironmentVariable("MDRRMO_AGENT_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MDRRMO", "CameraAgent");

    public static string CredsFile   => Path.Combine(Root, "creds.dpapi");
    public static string CamerasFile => Path.Combine(Root, "cameras.json");
    public static string CameraCreds => Path.Combine(Root, "cameras.creds.dpapi");
    public static string MediaMtxDir => Path.Combine(Root, "mediamtx");
    public static string MediaMtxYml => Path.Combine(MediaMtxDir, "mediamtx.yml");
    public static string MediaMtxExe => Path.Combine(MediaMtxDir, "mediamtx.exe");
    public static string LogsDir     => Path.Combine(Root, "logs");

    private static string RuntimeRoot =>
        Environment.GetEnvironmentVariable("MDRRMO_AGENT_RUNTIME_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MdrrmoCameraAgent");

    public static string RuntimeYml     => Path.Combine(RuntimeRoot, "runtime", "mediamtx.yml");
    public static string RuntimeLogsDir => Path.Combine(RuntimeRoot, "logs");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(MediaMtxDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(RuntimeYml)!);
        Directory.CreateDirectory(RuntimeLogsDir);
    }
}
