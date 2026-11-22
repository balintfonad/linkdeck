using System.Runtime.InteropServices;
using LinkDeck.Core.Models;
using LinkDeck.Core.Tests;
using LinkDeck.Platform.Windows.Native;

namespace LinkDeck.Platform.Windows.Tests;

public partial class ShellIdListResolverTests
{
    [Theory]
    [InlineData("::{4234D49B-0245-4DF3-B780-3893943456E1}\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("::{4234d49b-0245-4df3-b780-3893943456e1}\\{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\notepad.exe", "shell:AppsFolder\\{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\notepad.exe")]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("", null)]
    [InlineData("C:\\not-a-shell-name", null)]
    public void Converts_parsing_names_to_shell_targets(string parsing, string? expected) =>
        Assert.Equal(expected, ShellIdListResolver.ToShellTarget(parsing));

    [Fact]
    public void Decodes_a_real_cida_for_a_file()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var cida = BuildCida(notepad);

        var item = Assert.Single(new ShellIdListResolver().Resolve(cida));

        Assert.Equal(notepad, item.Target, ignoreCase: true);
        Assert.False(string.IsNullOrEmpty(item.DisplayName));
    }

    [Fact]
    public void Garbage_input_yields_nothing() =>
        Assert.Empty(new ShellIdListResolver().Resolve([1, 2, 3]));

    /// <summary>CIDA with an empty (desktop) parent and one absolute child PIDL.</summary>
    private static byte[] BuildCida(string path)
    {
        Assert.Equal(0, Shell32.SHParseDisplayName(path, 0, out var pidl, 0, out _));
        try
        {
            var size = (int)ILGetSize(pidl);
            var child = new byte[size];
            Marshal.Copy(pidl, child, 0, size);
            const int header = 4 + (4 * 2);
            var parent = new byte[] { 0, 0 };
            var data = new byte[header + parent.Length + child.Length];
            BitConverter.GetBytes(1u).CopyTo(data, 0);
            BitConverter.GetBytes((uint)header).CopyTo(data, 4);
            BitConverter.GetBytes((uint)(header + parent.Length)).CopyTo(data, 8);
            parent.CopyTo(data, header);
            child.CopyTo(data, header + parent.Length);
            return data;
        }
        finally
        {
            Shell32.ILFree(pidl);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial uint ILGetSize(nint pidl);
}

public class ShellTargetLaunchTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Shell_targets_are_opened_through_explorer_with_one_argument()
    {
        var runner = new RecordingRunner();
        var launcher = new WindowsLauncher(runner, _dir.Path, TimeSpan.FromSeconds(1));

        var result = launcher.Launch(new PathItem { Id = "1", Name = "Calc", Path = "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" });

        Assert.True(result.Success);
        var psi = Assert.Single(runner.Started);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), psi.FileName, ignoreCase: true);
        Assert.Equal(["shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"], psi.ArgumentList);
    }
}

public class AppCatalogTests
{
    [Fact]
    public void Installed_apps_come_from_start_menu_shortcuts()
    {
        var apps = new AppCatalog(new ShortcutResolver()).GetInstalledApps();

        Assert.NotEmpty(apps);
        Assert.All(apps, a => Assert.False(string.IsNullOrWhiteSpace(a.Target)));
        Assert.DoesNotContain(apps, a => a.Name.Contains("uninstall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Running_apps_never_include_this_process()
    {
        var self = Environment.ProcessPath;

        var apps = new AppCatalog(new ShortcutResolver()).GetRunningApps();

        Assert.DoesNotContain(apps, a => string.Equals(a.Target, self, StringComparison.OrdinalIgnoreCase));
        Assert.All(apps, a => Assert.True(a.IsRunning));
    }

    [Fact]
    public void Shell_icon_is_found_for_a_shell_target()
    {
        using var provider = new ShellIconProvider();

        // The Applications folder itself always exists.
        Assert.NotNull(provider.GetIcon("shell:AppsFolder"));
    }
}
