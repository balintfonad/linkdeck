using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Core.Models;

namespace LinkDeck.Platform.Windows;

/// <summary>Launches items on Windows. Every process start goes through <see cref="IProcessRunner"/>.</summary>
public sealed class WindowsLauncher : ILauncher
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".com", ".bat", ".cmd" };

    private readonly IProcessRunner _runner;
    private readonly string _tempDirectory;
    private readonly TimeSpan _rdpFileLifetime;
    private readonly string _mstscPath;

    public WindowsLauncher(IProcessRunner runner)
        : this(runner, Path.Combine(Path.GetTempPath(), "LinkDeck"), TimeSpan.FromSeconds(10))
    {
    }

    public WindowsLauncher(IProcessRunner runner, string tempDirectory, TimeSpan rdpFileLifetime)
    {
        _runner = runner;
        _tempDirectory = tempDirectory;
        _rdpFileLifetime = rdpFileLifetime;
        // Absolute path: never resolve mstsc through PATH or the current directory.
        _mstscPath = Path.Combine(Environment.SystemDirectory, "mstsc.exe");
    }

    public string TempDirectory => _tempDirectory;

    public LaunchResult Launch(Item item)
    {
        try
        {
            return item switch
            {
                UrlItem u => LaunchUrl(u),
                PathItem p => LaunchPath(p),
                RdpItem r => LaunchRdp(r),
                _ => LaunchResult.Fail("Unknown item type."),
            };
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return LaunchResult.Fail($"Could not open '{item.Name}': {ex.Message}");
        }
    }

    private LaunchResult LaunchUrl(UrlItem item)
    {
        if (!UrlPolicy.IsAllowed(item.Url))
        {
            return LaunchResult.Fail($"Blocked '{item.Name}': only http and https URLs can be opened.");
        }

        _runner.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
        return LaunchResult.Ok;
    }

    private LaunchResult LaunchPath(PathItem item)
    {
        var path = Environment.ExpandEnvironmentVariables(item.Path.Trim()).Trim('"');
        if (ItemCategories.IsShellTarget(path))
        {
            // Start-menu and Store apps (shell:AppsFolder…): Explorer resolves and launches them.
            if (path.Any(char.IsControl))
            {
                return LaunchResult.Fail($"Blocked '{item.Name}': invalid shell target.");
            }

            var shell = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
            shell.ArgumentList.Add(path);
            _runner.Start(shell);
            return LaunchResult.Ok;
        }

        if (PathPolicy.IsBlockedLinkFile(path))
        {
            return LaunchResult.Fail($"Blocked '{item.Name}': {Path.GetExtension(path)} files can open any kind of link, so LinkDeck does not open them. Add the web address itself instead.");
        }

        if (Directory.Exists(path))
        {
            _runner.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return LaunchResult.Ok;
        }

        if (!File.Exists(path))
        {
            return LaunchResult.Fail($"'{item.Name}' not found: {path}");
        }

        var psi = new ProcessStartInfo(path) { UseShellExecute = true };
        if (ExecutableExtensions.Contains(Path.GetExtension(path)))
        {
            if (!string.IsNullOrWhiteSpace(item.Args))
            {
                psi.Arguments = Environment.ExpandEnvironmentVariables(item.Args);
            }

            var workingDir = string.IsNullOrWhiteSpace(item.WorkingDir)
                ? Path.GetDirectoryName(path)
                : Environment.ExpandEnvironmentVariables(item.WorkingDir.Trim()).Trim('"');
            if (!string.IsNullOrEmpty(workingDir))
            {
                if (!Directory.Exists(workingDir))
                {
                    return LaunchResult.Fail($"Working directory of '{item.Name}' not found: {workingDir}");
                }

                psi.WorkingDirectory = workingDir;
            }
        }

        _runner.Start(psi);
        return LaunchResult.Ok;
    }

    private LaunchResult LaunchRdp(RdpItem item)
    {
        // Strict validation: nothing in host/port/username can become an extra argument or .rdp line.
        var error = ItemValidator.Validate(item);
        if (error is not null)
        {
            return LaunchResult.Fail($"Blocked '{item.Name}': {error}");
        }

        var address = RdpAddress.Format(item.Host, item.EffectivePort);

        // Always a generated .rdp file (never a password): mstsc cannot take a username on the command line, and
        // "mstsc /v:" would inherit Documents\Default.rdp, which any program can edit to redirect local drives.
        Directory.CreateDirectory(_tempDirectory);
        var file = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.rdp");
        File.WriteAllText(file, BuildRdpFile(address, item.Username), Encoding.Unicode);

        var psi = new ProcessStartInfo(_mstscPath) { UseShellExecute = false };
        psi.ArgumentList.Add(file);
        try
        {
            _runner.Start(psi);
        }
        catch
        {
            TryDelete(file);
            throw;
        }

        _ = DeleteLaterAsync(file, _rdpFileLifetime);
        return LaunchResult.Ok;
    }

    /// <summary>
    /// Settings written explicitly so nothing is inherited from Default.rdp: warn when the server cannot be
    /// authenticated, use CredSSP (NLA), and do not hand local drives, plug-and-play devices or COM ports to the server.
    /// Clipboard, printers and smart cards keep their Windows defaults.
    /// </summary>
    private static readonly string[] SafeRdpSettings =
    [
        "authentication level:i:2",
        "enablecredsspsupport:i:1",
        "redirectdrives:i:0",
        "drivestoredirect:s:",
        "devicestoredirect:s:",
        "redirectcomports:i:0",
    ];

    internal static string BuildRdpFile(string address, string? username)
    {
        var sb = new StringBuilder();
        sb.Append("full address:s:").Append(address).Append("\r\n");
        if (!string.IsNullOrEmpty(username))
        {
            sb.Append("username:s:").Append(username).Append("\r\n");
        }

        foreach (var line in SafeRdpSettings)
        {
            sb.Append(line).Append("\r\n");
        }

        return sb.ToString();
    }

    public void CleanupTemporaryFiles()
    {
        if (!Directory.Exists(_tempDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_tempDirectory, "*.rdp"))
        {
            TryDelete(file);
        }
    }

    private static async Task DeleteLaterAsync(string file, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        TryDelete(file);
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
