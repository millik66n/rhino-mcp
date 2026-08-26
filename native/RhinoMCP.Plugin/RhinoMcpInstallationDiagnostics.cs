using Rhino;

namespace RhinoMCP;

/// <summary>
/// Builds the read-only file and compatibility information shown by the local dashboard.
/// No file contents leave the computer; the dashboard is loopback-only.
/// </summary>
internal static class RhinoMcpInstallationDiagnostics
{
    private const int MaxCompatibilityFiles = 8;
    private const int MaxScannedDirectories = 1800;
    private static string[] _possibleArchicadLibraries = Array.Empty<string>();

    public static void RefreshCompatibilityScan()
    {
        Volatile.Write(ref _possibleArchicadLibraries, FindPossibleArchicadLibraries());
    }

    public static object[] InstalledFiles()
    {
        string pluginPath = typeof(RhinoMcpPlugin).Assembly.Location;
        string pluginDirectory = Path.GetDirectoryName(pluginPath) ?? "";
        string grasshopperPath = Path.Combine(pluginDirectory, "RhinoMCP.Grasshopper.gha");
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appRoot = Path.Combine(localAppData, "Programs", "Rhino MCP");
        string serverPath = Path.Combine(appRoot, "server", "rhino-mcp.exe");
        string installLog = Path.Combine(localAppData, "Rhino MCP", "Logs", "install.log");

        List<object> files = new()
        {
            FileItem(
                "rhino-plugin", "Rhino plug-in", "Rhino startup bridge",
                pluginPath, File.Exists(pluginPath), required: true),
            FileItem(
                "grasshopper-addon", "Grasshopper add-on", "Grasshopper bridge",
                grasshopperPath, File.Exists(grasshopperPath), required: true),
            FileItem(
                "mcp-server", "MCP server", "AI client process",
                serverPath, File.Exists(serverPath), required: true),
            FileItem(
                "settings", "Rhino MCP settings", "Ports, profile, and selected clients",
                UserSettings.ConfigFilePath, File.Exists(UserSettings.ConfigFilePath), required: true),
        };

        string regulationsPath = UserSettings.RegulationsDatabase;
        files.Add(FileItem(
            "regulations", "Regulations library", "Offline architecture references",
            regulationsPath, File.Exists(regulationsPath), required: true,
            emptyStatus: "Not configured"));

        foreach (string client in UserSettings.ConfiguredClients)
        {
            string path = ClientConfigPath(client);
            files.Add(FileItem(
                $"client-{client.ToLowerInvariant()}", $"{TitleCase(client)} configuration",
                "MCP connection entry", path, File.Exists(path), required: true));
        }

        files.Add(FileItem(
            "install-log", "Installation log", "Installer troubleshooting details",
            installLog, File.Exists(installLog), required: false,
            missingStatus: "Not created"));
        return files.ToArray();
    }

    public static object[] CompatibilityIssues(bool grasshopperAvailable)
    {
        if (grasshopperAvailable)
            return Array.Empty<object>();

        string pluginDirectory = Path.GetDirectoryName(typeof(RhinoMcpPlugin).Assembly.Location) ?? "";
        string grasshopperPath = Path.Combine(pluginDirectory, "RhinoMCP.Grasshopper.gha");
        if (!File.Exists(grasshopperPath))
        {
            return new object[]
            {
                Issue(
                    "grasshopper-addon-missing",
                    "Rhino MCP's Grasshopper add-on is missing",
                    "The Rhino bridge can run, but the Grasshopper bridge cannot start without this file.",
                    "Repair Rhino MCP with the installer, then restart Rhino.",
                    "danger",
                    new[] { grasshopperPath }),
            };
        }

        string[] candidates = Volatile.Read(ref _possibleArchicadLibraries);
        if (candidates.Length > 0)
        {
            return new object[]
            {
                Issue(
                    "possible-archicad-conflict",
                    "A Grasshopper add-on may be blocking startup",
                    "Grasshopper is not responding, and an Archicad Connection library was found. " +
                    "If Grasshopper shows a 'newer minor SDK' breakpoint, that library was built for a newer Rhino release.",
                    $"Install an Archicad Connection build compatible with Rhino " +
                    $"{RhinoApp.Version.Major}.{RhinoApp.Version.Minor}, or disable that add-on, then restart Rhino.",
                    "danger",
                    candidates),
            };
        }

        return new object[]
        {
            Issue(
                "grasshopper-unavailable",
                "Grasshopper bridge is not running",
                "Grasshopper is closed or its component loading was interrupted before Rhino MCP could start.",
                "Run Grasshopper. If a breakpoint names another add-on, install a version compatible with this Rhino release or disable that add-on.",
                "warning",
                Array.Empty<string>()),
        };
    }

    public static Dictionary<string, object?> FileItem(
        string id,
        string label,
        string description,
        string path,
        bool exists,
        bool required,
        string missingStatus = "Missing",
        string emptyStatus = "No path")
    {
        bool hasPath = !string.IsNullOrWhiteSpace(path);
        string status = exists ? "Found" : hasPath ? missingStatus : emptyStatus;
        string tone = exists ? "success" : required ? "danger" : "neutral";
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["label"] = label,
            ["description"] = description,
            ["path"] = hasPath ? path : "",
            ["exists"] = exists,
            ["status"] = status,
            ["tone"] = tone,
        };
    }

    private static Dictionary<string, object?> Issue(
        string id,
        string title,
        string message,
        string action,
        string tone,
        string[] paths) => new()
    {
        ["id"] = id,
        ["title"] = title,
        ["message"] = message,
        ["action"] = action,
        ["tone"] = tone,
        ["paths"] = paths,
    };

    private static string ClientConfigPath(string client)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        switch (client.Trim().ToLowerInvariant())
        {
            case "codex":
                string? codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
                return Path.Combine(
                    string.IsNullOrWhiteSpace(codexHome) ? Path.Combine(home, ".codex") : codexHome,
                    "config.toml");
            case "claude":
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Claude", "claude_desktop_config.json");
            case "cursor":
                return Path.Combine(home, ".cursor", "mcp.json");
            default:
                return "";
        }
    }

    private static string[] FindPossibleArchicadLibraries()
    {
        List<string> results = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> directories = new();
        foreach (string root in CompatibilityRoots())
        {
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root) && seen.Add(root))
                directories.Enqueue(root);
        }

        int scanned = 0;
        while (directories.Count > 0 && scanned < MaxScannedDirectories
               && results.Count < MaxCompatibilityFiles)
        {
            string directory = directories.Dequeue();
            scanned++;
            try
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    string extension = Path.GetExtension(file);
                    if ((extension.Equals(".gha", StringComparison.OrdinalIgnoreCase)
                         || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                         || extension.Equals(".rhp", StringComparison.OrdinalIgnoreCase))
                        && Path.GetFileNameWithoutExtension(file)
                            .IndexOf("archicad", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(file);
                        if (results.Count >= MaxCompatibilityFiles)
                            break;
                    }
                }

                foreach (string child in Directory.GetDirectories(directory))
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) == 0 && seen.Add(child))
                            directories.Enqueue(child);
                    }
                    catch
                    {
                        // Skip protected or disappearing folders.
                    }
                }
            }
            catch
            {
                // Diagnostics must never interrupt Rhino startup.
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray();
    }

    private static IEnumerable<string> CompatibilityRoots()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(appData, "Grasshopper", "Libraries");
        yield return Path.Combine(appData, "McNeel", "Rhinoceros", "packages", "8.0");
        yield return Path.Combine(programFiles, "GRAPHISOFT");
        yield return Path.Combine(programFilesX86, "GRAPHISOFT");
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "AI client";
        return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }
}
