using System.Security.AccessControl;
using System.Security.Principal;

namespace LinkDeck.Platform.Windows;

/// <summary>Checks that no account other than the current user and the system can change a folder.</summary>
public static class FolderPermissions
{
    private const int GenericAll = 0x10000000;
    private const int GenericWrite = 0x40000000;

    /// <summary>Rights that let someone create, replace or re-permission files.</summary>
    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private static readonly SecurityIdentifier[] SystemSids =
    [
        new(WellKnownSidType.LocalSystemSid, null),
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new(WellKnownSidType.CreatorOwnerSid, null),
        new("S-1-3-4"), // OWNER RIGHTS (the owner is checked separately)
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // TrustedInstaller
    ];

    /// <summary>
    /// True when only the current user, SYSTEM, Administrators and TrustedInstaller can change the folder and its
    /// <c>config.json</c>. False for e.g. <c>C:\Tools\…</c> (Authenticated Users may modify by default) or shared folders.
    /// A removable FAT/exFAT drive has no permissions at all and is accepted: whoever holds the stick controls it anyway.
    /// </summary>
    public static bool IsPrivateFolder(string directory)
    {
        try
        {
            var me = WindowsIdentity.GetCurrent().User;
            var dir = new DirectoryInfo(directory);
            if (IsRemovableWithoutPermissions(dir))
            {
                return true;
            }

            if (!IsRestricted(dir.GetAccessControl(), me))
            {
                return false;
            }

            var config = new FileInfo(Path.Combine(directory, "config.json"));
            return !config.Exists || IsRestricted(config.GetAccessControl(), me);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException or InvalidOperationException
            or ArgumentException or IdentityNotMappedException or System.ComponentModel.Win32Exception)
        {
            return false; // cannot verify: do not trust the folder
        }
    }

    internal static bool IsRestricted(FileSystemSecurity security, SecurityIdentifier? me)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !IsTrusted(owner, me))
        {
            return false;
        }

        var raw = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        if (raw.DiscretionaryAcl is null)
        {
            return false; // a null DACL grants everyone full access
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
            {
                continue;
            }

            var rights = (int)rule.FileSystemRights;
            var canWrite = (rights & ((int)WriteRights | GenericWrite | GenericAll)) != 0;
            if (canWrite && !IsTrusted((SecurityIdentifier)rule.IdentityReference, me))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTrusted(SecurityIdentifier sid, SecurityIdentifier? me) =>
        (me is not null && sid.Equals(me)) || SystemSids.Contains(sid);

    private static bool IsRemovableWithoutPermissions(DirectoryInfo dir)
    {
        var root = Path.GetPathRoot(dir.FullName);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        var drive = new DriveInfo(root);
        return drive.IsReady
            && drive.DriveType == DriveType.Removable
            && drive.DriveFormat is "FAT" or "FAT32" or "exFAT";
    }
}
