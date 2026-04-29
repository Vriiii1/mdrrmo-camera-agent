using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace MdrrmoCameraAgent.Storage;

/// <summary>
/// Machine-scope DPAPI vault. Required because the agent runs as LocalSystem
/// and cannot decrypt user-scope blobs.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DpapiVault
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MDRRMO.CameraAgent.v1");

    public static void WriteString(string path, string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine);
        var dir = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("path must not be a root path", nameof(path));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, ciphertext);
    }

    public static string? ReadString(string path)
    {
        if (!File.Exists(path)) return null;
        var ciphertext = File.ReadAllBytes(path);
        var plaintext  = ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(plaintext);
    }

    public static bool Exists(string path) => File.Exists(path);
}
