using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MdrrmoCameraAgent.Mtx;

/// <summary>
/// Pre-flight check for an existing mediamtx.yml from a previous (possibly
/// crashed) run. If the file exists but is owned by an unexpected principal
/// or has Users-group ACEs, we refuse to use it — DPAPI-decrypted RTSP
/// credentials may have been leaked. Caller should delete + regenerate.
/// </summary>
[SupportedOSPlatform("windows")]
public static class YmlAclGuard
{
    public sealed record CheckResult(bool Safe, string? Reason);

    public static CheckResult Check(string ymlPath)
    {
        if (!File.Exists(ymlPath)) return new(true, null);

        try
        {
            var fi  = new FileInfo(ymlPath);
            var ac  = fi.GetAccessControl();
            var ownerId = ac.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (ownerId is null) return new(false, "could not read file owner");

            var currentUser = WindowsIdentity.GetCurrent().User;
            var systemSid   = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adminsSid   = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            if ((currentUser is null || !ownerId.Equals(currentUser)) && !ownerId.Equals(systemSid) && !ownerId.Equals(adminsSid))
                return new(false, $"unexpected owner: {ownerId}");

            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            foreach (FileSystemAccessRule rule in ac.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.IdentityReference.Equals(usersSid) && rule.AccessControlType == AccessControlType.Allow)
                    return new(false, "Users group has Allow ACE — refusing to use stale yml");
            }
            return new(true, null);
        }
        catch (Exception ex)
        {
            return new(false, $"acl check failed: {ex.Message}");
        }
    }
}
