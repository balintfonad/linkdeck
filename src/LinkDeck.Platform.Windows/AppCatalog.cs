using System.Diagnostics;
using LinkDeck.Core.Abstractions;
using LinkDeck.Platform.Windows.Native;

namespace LinkDeck.Platform.Windows;

/// <summary>
/// Apps for the quick-add picker: windows that are open right now (desktop and Store apps) and
/// Start-menu shortcuts. Nothing is recorded; the lists are built on demand.
/// </summary>
public sealed unsafe class AppCatalog : IAppCatalog
{
    private readonly IShortcutResolver _shortcuts;

    public AppCatalog(IShortcutResolver shortcuts) => _shortcuts = shortcuts;

    public IReadOnlyList<AppCandidate> GetRunningApps()
    {
        var self = (uint)Environment.ProcessId;
        var results = new List<AppCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hwnd in TopLevelWindows())
        {
            if (!IsAppWindow(hwnd))
            {
                continue;
            }

            Processes.GetWindowThreadProcessId(hwnd, out var pid);
            var title = GetTitle(hwnd);
            var exe = GetExePath(pid);
            if (pid == self || exe is null || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var exeName = Path.GetFileName(exe);

            // Store apps are hosted by ApplicationFrameHost; the real app process owns a child window.
            if (exeName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                pid = FindHostedProcess(hwnd, pid);
            }

            if (exeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue; // folders and the desktop: drag the folder instead
            }

            AppCandidate candidate;
            var aumid = GetAppUserModelId(pid);
            if (aumid is not null)
            {
                candidate = new AppCandidate(title, "shell:AppsFolder\\" + aumid, null, null, IsRunning: true);
            }
            else
            {
                exe = GetExePath(pid) ?? exe;
                candidate = new AppCandidate(FriendlyName(exe, title), exe, null, null, IsRunning: true);
            }

            if (seen.Add(candidate.Target))
            {
                results.Add(candidate);
            }
        }

        return [.. results.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public IReadOnlyList<AppCandidate> GetInstalledApps()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        var results = new List<AppCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(Path.Combine(root, "Programs"), "*.lnk", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var lnk in files)
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || !seen.Add(name))
                {
                    continue;
                }

                var info = _shortcuts.Resolve(lnk);
                var target = info?.TargetPath;
                results.Add(target is not null && File.Exists(target)
                    ? new AppCandidate(name, target, info!.Arguments, info.WorkingDirectory, IsRunning: false)
                    : new AppCandidate(name, lnk, null, null, IsRunning: false)); // advertised/MSI shortcut: open the .lnk itself
            }
        }

        return [.. results.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static bool IsAppWindow(nint hwnd)
    {
        if (!Processes.IsWindowVisible(hwnd) || Processes.GetWindow(hwnd, Processes.GwOwner) != 0)
        {
            return false;
        }

        if ((Processes.GetWindowLongPtr(hwnd, Processes.GwlExStyle) & Processes.WsExToolWindow) != 0)
        {
            return false;
        }

        // Suspended Store apps and windows on other virtual desktops are "cloaked".
        return Processes.DwmGetWindowAttribute(hwnd, Processes.DwmwaCloaked, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static string GetTitle(nint hwnd)
    {
        var buffer = stackalloc char[512];
        var length = Processes.GetWindowText(hwnd, buffer, 512);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    private static string? GetExePath(uint pid)
    {
        var process = Processes.OpenProcess(Processes.ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return Processes.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            Processes.CloseHandle(process);
        }
    }

    private static string? GetAppUserModelId(uint pid)
    {
        var process = Processes.OpenProcess(Processes.ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[256];
            uint length = 256;
            return Processes.GetApplicationUserModelId(process, ref length, buffer) == 0 && length > 1
                ? new string(buffer, 0, (int)length - 1)
                : null;
        }
        finally
        {
            Processes.CloseHandle(process);
        }
    }

    private static uint FindHostedProcess(nint frame, uint framePid)
    {
        foreach (var child in ChildWindows(frame))
        {
            Processes.GetWindowThreadProcessId(child, out var pid);
            if (pid != framePid)
            {
                return pid;
            }
        }

        return framePid;
    }

    private static List<nint> TopLevelWindows()
    {
        var list = new List<nint>();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(list);
        try
        {
            Processes.EnumWindows(&Collect, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return list;
    }

    private static List<nint> ChildWindows(nint parent)
    {
        var list = new List<nint>();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(list);
        try
        {
            Processes.EnumChildWindows(parent, &Collect, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return list;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint state)
    {
        ((List<nint>)System.Runtime.InteropServices.GCHandle.FromIntPtr(state).Target!).Add(hwnd);
        return 1;
    }

    private static string FriendlyName(string exe, string title)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            var name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }
        catch (FileNotFoundException)
        {
        }

        return string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(exe) : title;
    }
}
