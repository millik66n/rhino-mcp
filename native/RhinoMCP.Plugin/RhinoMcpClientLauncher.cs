using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RhinoMCP;

/// <summary>
/// Opens the configured Codex desktop experience without depending on a private
/// install path or an undocumented URL scheme.
/// </summary>
internal sealed class RhinoMcpClientLauncher
{
    private const int RestoreWindow = 9;
    private readonly object _sync = new();
    private ClientLaunchSnapshot _snapshot = ClientLaunchSnapshot.NotAttempted;
    private Task<ClientLaunchSnapshot>? _launchTask;

    public ClientLaunchSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    public Dictionary<string, object?> DashboardStatus()
    {
        ClientLaunchSnapshot snapshot = Snapshot;
        return new Dictionary<string, object?>
        {
            ["state"] = snapshot.State,
            ["message"] = snapshot.Message,
            ["can_open"] = IsWindows && UserSettings.CodexConfigured,
            ["auto_launch_enabled"] = UserSettings.AutoLaunchClient,
        };
    }

    public Task<ClientLaunchSnapshot> OpenConfiguredCodexAsync(bool automatic)
    {
        if (!UserSettings.CodexConfigured)
            return Task.FromResult(SetSnapshot(new ClientLaunchSnapshot(
                "not_configured",
                "Codex is not the configured AI client.",
                false)));

        if (automatic && !UserSettings.AutoLaunchClient)
            return Task.FromResult(SetSnapshot(new ClientLaunchSnapshot(
                "disabled",
                "Automatic Codex opening is disabled. Click Open Codex when you are ready.",
                false)));

        if (!IsWindows)
            return Task.FromResult(SetSnapshot(new ClientLaunchSnapshot(
                "unsupported",
                "Automatic Codex opening is currently available on Windows.",
                false)));

        lock (_sync)
        {
            if (_launchTask is not null && !_launchTask.IsCompleted)
                return _launchTask;

            _snapshot = new ClientLaunchSnapshot(
                "opening",
                "Opening Codex. When it appears, start writing your Rhino request.",
                false);
            _launchTask = Task.Run(LaunchCodex);
            return _launchTask;
        }
    }

    private ClientLaunchSnapshot LaunchCodex()
    {
        try
        {
            if (ActivateRunningApp())
                return Finish(new ClientLaunchSnapshot(
                    "already_running",
                    "Codex is open. Start writing what you want Rhino to create or change.",
                    true));

            foreach (string shortcut in StartMenuShortcuts())
            {
                if (TryStart(shortcut))
                    return Finish(Opened());
            }

            foreach (string executable in DesktopExecutables())
            {
                if (TryStart(executable))
                    return Finish(Opened());
            }

            if (TryStartWindowsApp())
                return Finish(Opened());

            return Finish(new ClientLaunchSnapshot(
                "not_found",
                "Codex could not be found. Open the ChatGPT desktop app once from the Windows Start menu, choose Codex, then try again.",
                false));
        }
        catch (Exception exception)
        {
            return Finish(new ClientLaunchSnapshot(
                "failed",
                $"Codex could not be opened automatically: {exception.Message}",
                false));
        }
    }

    private ClientLaunchSnapshot Finish(ClientLaunchSnapshot snapshot)
    {
        SetSnapshot(snapshot);
        BridgeLog.Write(snapshot.Message);
        return snapshot;
    }

    private ClientLaunchSnapshot SetSnapshot(ClientLaunchSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
            return snapshot;
        }
    }

    private static ClientLaunchSnapshot Opened() => new(
        "opened",
        "Codex is open. Start writing what you want Rhino to create or change.",
        true);

    private static bool IsWindows =>
        Environment.OSVersion.Platform == PlatformID.Win32NT;

    private static bool ActivateRunningApp()
    {
        foreach (string processName in new[] { "Codex", "ChatGPT" })
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (Process process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited)
                            continue;
                        IntPtr window = process.MainWindowHandle;
                        if (window != IntPtr.Zero)
                        {
                            ShowWindowAsync(window, RestoreWindow);
                            SetForegroundWindow(window);
                            return true;
                        }
                    }
                    catch
                    {
                        // Another process instance may still expose the main window.
                    }
                }
            }
        }
        return false;
    }

    private static IEnumerable<string> StartMenuShortcuts()
    {
        List<string> candidates = new();
        foreach (Environment.SpecialFolder folder in new[]
        {
            Environment.SpecialFolder.StartMenu,
            Environment.SpecialFolder.CommonStartMenu,
        })
        {
            string root = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;
            try
            {
                foreach (string path in Directory.EnumerateFiles(
                    root, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(path);
                    if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        && !extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.Equals("Codex", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))
                        candidates.Add(path);
                }
            }
            catch
            {
                // Continue with other Windows discovery methods.
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileNameWithoutExtension(path)
                .Equals("Codex", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
    }

    private static IEnumerable<string> DesktopExecutables()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        List<string> candidates = new();

        AddCandidate(candidates, local, "OpenAI", "Codex", "Codex.exe");
        AddCandidate(candidates, local, "OpenAI", "ChatGPT", "ChatGPT.exe");
        AddCandidate(candidates, local, "Programs", "OpenAI", "Codex", "Codex.exe");
        AddCandidate(candidates, local, "Programs", "OpenAI", "ChatGPT", "ChatGPT.exe");
        AddCandidate(candidates, local, "Programs", "Codex", "Codex.exe");
        AddCandidate(candidates, local, "Programs", "ChatGPT", "ChatGPT.exe");
        AddCandidate(candidates, programFiles, "OpenAI", "Codex", "Codex.exe");
        AddCandidate(candidates, programFiles, "OpenAI", "ChatGPT", "ChatGPT.exe");
        AddCandidate(candidates, programFilesX86, "OpenAI", "Codex", "Codex.exe");
        AddCandidate(candidates, programFilesX86, "OpenAI", "ChatGPT", "ChatGPT.exe");

        foreach (string root in new[]
        {
            Path.Combine(local, "OpenAI"),
            Path.Combine(local, "Programs", "OpenAI"),
        })
        {
            if (!Directory.Exists(root))
                continue;
            try
            {
                foreach (string path in Directory.EnumerateFiles(
                    root, "*.exe", SearchOption.AllDirectories)
                    .Where(path =>
                    {
                        string name = Path.GetFileName(path);
                        bool desktopName = name.Equals(
                            "Codex.exe", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
                        bool privateCli = path.IndexOf(
                            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase) >= 0;
                        return desktopName && !privateCli;
                    })
                    .Take(64))
                {
                    candidates.Add(path);
                }
            }
            catch
            {
                // Continue with registered Windows apps.
            }
        }

        return candidates
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path)
                .Equals("Codex.exe", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
    }

    private static void AddCandidate(List<string> candidates, params string[] parts)
    {
        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            candidates.Add(Path.Combine(parts));
    }

    private static bool TryStart(string path)
    {
        try
        {
            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            });
            process?.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStartWindowsApp()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            powershell = "powershell.exe";

        const string script =
            "$app = Get-StartApps | Where-Object { $_.Name -eq 'Codex' } | Select-Object -First 1; " +
            "if ($null -eq $app) { $app = Get-StartApps | Where-Object { $_.Name -eq 'ChatGPT' } | Select-Object -First 1 }; " +
            "if ($null -eq $app) { exit 3 }; " +
            "Start-Process explorer.exe -ArgumentList ('shell:AppsFolder\\' + $app.AppID); exit 0";

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return false;
            if (!process.WaitForExit(8000))
            {
                try { process.Kill(); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
}

internal sealed class ClientLaunchSnapshot
{
    public static readonly ClientLaunchSnapshot NotAttempted = new(
        "not_attempted",
        "Codex will open automatically after Rhino finishes starting.",
        false);

    public ClientLaunchSnapshot(string state, string message, bool succeeded)
    {
        State = state;
        Message = message;
        Succeeded = succeeded;
    }

    public string State { get; }
    public string Message { get; }
    public bool Succeeded { get; }

    public Dictionary<string, object?> ToDictionary() => new()
    {
        ["state"] = State,
        ["message"] = Message,
        ["succeeded"] = Succeeded,
    };
}
