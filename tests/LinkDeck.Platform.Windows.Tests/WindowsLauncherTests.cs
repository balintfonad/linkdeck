using System.Diagnostics;
using System.Text;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Models;
using LinkDeck.Core.Tests;

namespace LinkDeck.Platform.Windows.Tests;

public sealed class RecordingRunner : IProcessRunner
{
    public List<ProcessStartInfo> Started { get; } = [];

    /// <summary>Captured contents of any file passed as the first argument (temp .rdp files are deleted later).</summary>
    public List<string> FileContents { get; } = [];

    public void Start(ProcessStartInfo startInfo)
    {
        Started.Add(startInfo);
        if (startInfo.ArgumentList.Count > 0 && File.Exists(startInfo.ArgumentList[0]))
        {
            FileContents.Add(File.ReadAllText(startInfo.ArgumentList[0]));
        }
    }
}

public class WindowsLauncherTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly RecordingRunner _runner = new();
    private readonly WindowsLauncher _launcher;
    private static readonly string Mstsc = Path.Combine(Environment.SystemDirectory, "mstsc.exe");

    public WindowsLauncherTests() =>
        _launcher = new WindowsLauncher(_runner, Path.Combine(_dir.Path, "tmp"), TimeSpan.FromMilliseconds(200));

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Url_opens_with_shell_execute()
    {
        var result = _launcher.Launch(new UrlItem { Id = "1", Name = "X", Url = "https://example.com/a?b=c" });

        Assert.True(result.Success);
        var psi = Assert.Single(_runner.Started);
        Assert.Equal("https://example.com/a?b=c", psi.FileName);
        Assert.True(psi.UseShellExecute);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:")]
    public void Url_with_disallowed_scheme_is_blocked(string url)
    {
        var result = _launcher.Launch(new UrlItem { Id = "1", Name = "X", Url = url });

        Assert.False(result.Success);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void Folder_opens_with_shell_execute_after_env_expansion()
    {
        Environment.SetEnvironmentVariable("LINKDECK_TEST_DIR", _dir.Path);

        var result = _launcher.Launch(new PathItem { Id = "1", Name = "X", Path = "%LINKDECK_TEST_DIR%" });

        Assert.True(result.Success);
        var psi = Assert.Single(_runner.Started);
        Assert.Equal(_dir.Path, psi.FileName);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void Document_opens_with_default_app()
    {
        var file = _dir.File("notes.txt");
        File.WriteAllText(file, "");

        Assert.True(_launcher.Launch(new PathItem { Id = "1", Name = "X", Path = file }).Success);

        var psi = Assert.Single(_runner.Started);
        Assert.Equal(file, psi.FileName);
        Assert.Equal("", psi.Arguments);
    }

    [Fact]
    public void Executable_gets_arguments_and_working_directory()
    {
        var exe = _dir.File("tool.exe");
        File.WriteAllText(exe, "");
        var work = Directory.CreateDirectory(_dir.File("work")).FullName;

        var result = _launcher.Launch(new PathItem { Id = "1", Name = "X", Path = exe, Args = "--new-window \"a b\"", WorkingDir = work });

        Assert.True(result.Success);
        var psi = Assert.Single(_runner.Started);
        Assert.Equal(exe, psi.FileName);
        Assert.Equal("--new-window \"a b\"", psi.Arguments);
        Assert.Equal(work, psi.WorkingDirectory);
    }

    [Fact]
    public void Executable_without_working_dir_uses_its_own_folder()
    {
        var exe = _dir.File("tool.exe");
        File.WriteAllText(exe, "");

        _launcher.Launch(new PathItem { Id = "1", Name = "X", Path = exe });

        Assert.Equal(_dir.Path, Assert.Single(_runner.Started).WorkingDirectory);
    }

    [Fact]
    public void Missing_path_fails_without_starting_anything()
    {
        var result = _launcher.Launch(new PathItem { Id = "1", Name = "Gone", Path = _dir.File("nope.exe") });

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error, StringComparison.Ordinal);
        Assert.Empty(_runner.Started);
    }

    [Theory]
    [InlineData("Portal.url")]
    [InlineData("Portal.website")]
    [InlineData("Explorer.scf")]
    [InlineData("Find.search-ms")]
    [InlineData("Share.library-ms")]
    [InlineData("Find.searchConnector-ms")]
    public void Link_files_that_can_open_any_scheme_are_blocked(string name)
    {
        var file = _dir.File(name);
        File.WriteAllText(file, "[InternetShortcut]\r\nURL=search-ms:query=x\r\n");

        var result = _launcher.Launch(new PathItem { Id = "1", Name = "X", Path = file });

        Assert.False(result.Success);
        Assert.Empty(_runner.Started);
    }

    [Theory]
    [InlineData("server.corp", null, "server.corp:3389")]
    [InlineData("10.0.0.15", 3390, "10.0.0.15:3390")]
    [InlineData("fe80::1", null, "[fe80::1]:3389")]
    public void Rdp_without_username_runs_mstsc_with_a_generated_rdp_file(string host, int? port, string expectedAddress)
    {
        var result = _launcher.Launch(new RdpItem { Id = "1", Name = "X", Host = host, Port = port });

        Assert.True(result.Success);
        var psi = Assert.Single(_runner.Started);
        Assert.Equal(Mstsc, psi.FileName);
        Assert.False(psi.UseShellExecute);
        var file = Assert.Single(psi.ArgumentList);
        Assert.EndsWith(".rdp", file, StringComparison.OrdinalIgnoreCase);
        var content = Assert.Single(_runner.FileContents);
        Assert.StartsWith($"full address:s:{expectedAddress}\r\n", content, StringComparison.Ordinal);
        Assert.DoesNotContain("username:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_rdp_file_sets_safe_defaults_so_default_rdp_cannot_redirect_local_drives()
    {
        _launcher.Launch(new RdpItem { Id = "1", Name = "X", Host = "server.corp" });

        var lines = Assert.Single(_runner.FileContents).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("authentication level:i:2", lines);
        Assert.Contains("enablecredsspsupport:i:1", lines);
        Assert.Contains("drivestoredirect:s:", lines);
        Assert.Contains("redirectdrives:i:0", lines);
        Assert.Contains("devicestoredirect:s:", lines);
        Assert.Contains("redirectcomports:i:0", lines);
    }

    [Theory]
    [InlineData("host /admin")]
    [InlineData("host\" /f")]
    [InlineData("")]
    public void Rdp_with_invalid_host_is_blocked(string host)
    {
        var result = _launcher.Launch(new RdpItem { Id = "1", Name = "X", Host = host });

        Assert.False(result.Success);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void Rdp_with_username_writes_temp_rdp_file_without_password_and_deletes_it_later()
    {
        var result = _launcher.Launch(new RdpItem { Id = "1", Name = "X", Host = "server.corp", Port = 3390, Username = "CORP\\admin" });

        Assert.True(result.Success);
        var psi = Assert.Single(_runner.Started);
        Assert.Equal(Mstsc, psi.FileName);
        var file = Assert.Single(psi.ArgumentList);
        Assert.StartsWith(Path.Combine(_dir.Path, "tmp"), file, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".rdp", file, StringComparison.OrdinalIgnoreCase);

        var content = Assert.Single(_runner.FileContents);
        Assert.StartsWith("full address:s:server.corp:3390\r\nusername:s:CORP\\admin\r\n", content, StringComparison.Ordinal);
        Assert.DoesNotContain("password", content, StringComparison.OrdinalIgnoreCase);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (File.Exists(file) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Temp_rdp_file_is_utf16_so_mstsc_reads_non_ascii_usernames()
    {
        _launcher.Launch(new RdpItem { Id = "1", Name = "X", Host = "h", Username = "Bálint" });

        var file = Assert.Single(_runner.Started).ArgumentList[0];
        Assert.Contains("username:s:Bálint", Assert.Single(_runner.FileContents), StringComparison.Ordinal);
        if (File.Exists(file))
        {
            Assert.Equal([0xFF, 0xFE], File.ReadAllBytes(file)[..2]); // UTF-16 LE BOM
        }
    }

    [Fact]
    public void Rdp_username_with_newline_is_blocked()
    {
        var result = _launcher.Launch(new RdpItem { Id = "1", Name = "X", Host = "h", Username = "a\r\nfull address:s:evil" });

        Assert.False(result.Success);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void Cleanup_removes_leftover_rdp_files()
    {
        var tmp = Directory.CreateDirectory(Path.Combine(_dir.Path, "tmp")).FullName;
        File.WriteAllText(Path.Combine(tmp, "old.rdp"), "x", Encoding.Unicode);
        File.WriteAllText(Path.Combine(tmp, "keep.txt"), "x");

        _launcher.CleanupTemporaryFiles();

        Assert.False(File.Exists(Path.Combine(tmp, "old.rdp")));
        Assert.True(File.Exists(Path.Combine(tmp, "keep.txt")));
    }
}
