using System.Security.AccessControl;
using System.Security.Principal;
using LinkDeck.Core.Tests;

namespace LinkDeck.Platform.Windows.Tests;

public class ShortcutResolverTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Resolves_a_real_shortcut_target_arguments_and_working_directory()
    {
        var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var lnk = _dir.File("Notepad.lnk");
        ShortcutResolver.Create(lnk, target, "--test \"x y\"", _dir.Path);

        var info = new ShortcutResolver().Resolve(lnk);

        Assert.NotNull(info);
        Assert.Equal(target, info.TargetPath, ignoreCase: true);
        Assert.Equal("--test \"x y\"", info.Arguments);
        Assert.Equal(_dir.Path, info.WorkingDirectory, ignoreCase: true);
    }

    [Fact]
    public void Shortcut_with_network_icon_is_not_opened_for_its_icon()
    {
        var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var lnk = _dir.File("Report.lnk");
        ShortcutResolver.Create(lnk, target, null, null, @"\\evil.example@SSL\i\icon.ico");

        Assert.True(ShortcutResolver.PointsToNetwork(lnk));
        Assert.True(ShellIconProvider.MustNotOpen(lnk));
    }

    [Fact]
    public void Local_shortcut_is_opened_for_its_icon()
    {
        var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var lnk = _dir.File("Notepad.lnk");
        ShortcutResolver.Create(lnk, target, null, null, target);

        Assert.False(ShortcutResolver.PointsToNetwork(lnk));
        Assert.False(ShellIconProvider.MustNotOpen(lnk));
    }

    [Theory]
    [InlineData(@"\\evil.example\share\setup.exe")]
    [InlineData(@"C:\Downloads\Portal.url")]
    public void Network_paths_and_link_files_get_a_generic_icon_without_being_opened(string path)
    {
        Assert.True(ShellIconProvider.MustNotOpen(path));
        Assert.NotNull(new ShellIconProvider().GetIcon(path));
    }

    [Fact]
    public void Missing_or_invalid_shortcut_returns_null()
    {
        var bogus = _dir.File("bogus.lnk");
        File.WriteAllText(bogus, "not a shortcut");

        Assert.Null(new ShortcutResolver().Resolve(_dir.File("missing.lnk")));
        Assert.Null(new ShortcutResolver().Resolve(bogus));
    }
}

public class FolderPermissionsTests : IDisposable
{
    private readonly TempDir _dir = new();

    /// <summary>%TEMP% itself may carry extra entries (security software), so each test starts from a clean, owner-only folder.</summary>
    public FolderPermissionsTests()
    {
        var dir = new DirectoryInfo(_dir.Path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        dir.SetAccessControl(security);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Folder_in_the_user_profile_is_private()
    {
        Assert.True(FolderPermissions.IsPrivateFolder(_dir.Path));
    }

    [Theory]
    [InlineData(WellKnownSidType.BuiltinUsersSid)]
    [InlineData(WellKnownSidType.AuthenticatedUserSid)]
    [InlineData(WellKnownSidType.WorldSid)]
    public void Folder_other_accounts_can_write_is_not_private(WellKnownSidType others)
    {
        var dir = new DirectoryInfo(_dir.Path);
        var security = dir.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(others, null), FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(security);

        Assert.False(FolderPermissions.IsPrivateFolder(_dir.Path));
    }

    [Fact]
    public void Read_only_access_for_others_is_fine()
    {
        var dir = new DirectoryInfo(_dir.Path);
        var security = dir.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(security);

        Assert.True(FolderPermissions.IsPrivateFolder(_dir.Path));
    }

    [Fact]
    public void Config_file_other_accounts_can_write_makes_the_folder_not_private()
    {
        var config = new FileInfo(_dir.File("config.json"));
        File.WriteAllText(config.FullName, "{}");
        var security = config.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write, AccessControlType.Allow));
        config.SetAccessControl(security);

        Assert.False(FolderPermissions.IsPrivateFolder(_dir.Path));
    }

    [Fact]
    public void Missing_folder_is_not_private()
    {
        Assert.False(FolderPermissions.IsPrivateFolder(_dir.File("missing")));
    }
}

public class DpapiProtectorTests
{
    [Fact]
    public void Round_trips_and_rejects_tampered_data()
    {
        var protector = new DpapiProtector();
        var data = "linkdeck-trust-v1\nABC\n"u8.ToArray();

        var protectedBytes = protector.Protect(data);

        Assert.False(protectedBytes.AsSpan().SequenceEqual(data));
        Assert.Equal(data, protector.Unprotect(protectedBytes));

        protectedBytes[^1] ^= 0xFF;
        Assert.Null(protector.Unprotect(protectedBytes));
        Assert.Null(protector.Unprotect("not protected"u8.ToArray()));
    }
}

public class AutostartTests
{
    [Fact]
    public void Enable_and_disable_toggle_the_run_value()
    {
        // A private test key so the real Run key is never touched.
        var keyPath = $@"Software\LinkDeck.Tests\{Guid.NewGuid():N}";
        var autostart = new Autostart(@"C:\Apps\LinkDeck.exe", "LinkDeckTest", keyPath);
        try
        {
            Assert.False(autostart.IsEnabled);

            autostart.SetEnabled(true);
            Assert.True(autostart.IsEnabled);
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
            {
                Assert.Equal("\"C:\\Apps\\LinkDeck.exe\" --autostart", key!.GetValue("LinkDeckTest"));
            }

            autostart.SetEnabled(false);
            Assert.False(autostart.IsEnabled);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\LinkDeck.Tests", throwOnMissingSubKey: false);
        }
    }
}

public class ShellIconProviderTests
{
    [Fact]
    public void Returns_bgra_pixels_for_an_executable()
    {
        using var provider = new ShellIconProvider();
        var icon = provider.GetIcon(Path.Combine(Environment.SystemDirectory, "notepad.exe"));

        Assert.NotNull(icon);
        Assert.True(icon.Width > 0 && icon.Height > 0);
        Assert.Equal(icon.Width * icon.Height * 4, icon.Bgra.Length);
        Assert.Contains(icon.Bgra.Where((_, i) => i % 4 == 3), a => a != 0);
    }

    [Fact]
    public void Returns_null_for_missing_paths()
    {
        using var provider = new ShellIconProvider();
        Assert.Null(provider.GetIcon(@"C:\definitely\not\here.exe"));
    }
}

public class ShellIconConcurrencyTests
{
    [Fact]
    public async Task Parallel_requests_for_folders_and_programs_all_succeed()
    {
        using var provider = new ShellIconProvider();
        string[] paths =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.SystemDirectory,
            Path.Combine(Environment.SystemDirectory, "notepad.exe"),
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        ];

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() => provider.GetIcon(paths[i % paths.Length]))));

        Assert.All(results, Assert.NotNull);
    }
}
