using System.Drawing;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace RhinoMCP;

/// <summary>
/// A small, always-visible connection strip drawn in the active Rhino viewport.
/// It avoids taking panel space and follows whichever viewport the user is working in.
/// </summary>
internal sealed class RhinoMcpStatusHud : DisplayConduit
{
    private static readonly Color Background = Color.FromArgb(238, 29, 33, 39);
    private static readonly Color Border = Color.FromArgb(255, 78, 86, 97);
    private static readonly Color PrimaryText = Color.FromArgb(255, 244, 247, 250);
    private static readonly Color SecondaryText = Color.FromArgb(255, 190, 198, 209);
    private static readonly Color Green = Color.FromArgb(255, 52, 190, 116);
    private static readonly Color Amber = Color.FromArgb(255, 235, 168, 55);
    private static readonly Color Red = Color.FromArgb(255, 226, 82, 82);
    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private volatile StatusSnapshot _status = StatusSnapshot.Empty;
    private volatile object[] _projectFiles = Array.Empty<object>();
    private int _polling;
    private int _suppressDrawing;

    public bool IsVisible => Enabled;

    public void Start()
    {
        lock (_sync)
        {
            if (_timer is not null)
            {
                Show();
                return;
            }

            _status = ReadStatus(probeGrasshopper: false);
            _projectFiles = ReadProjectFiles(_status);
            Enabled = true;
            _timer = new System.Threading.Timer(PollStatus, null, 0, 1000);
        }
        Redraw();
    }

    public void Show()
    {
        Enabled = true;
        Redraw();
    }

    public void Hide()
    {
        Enabled = false;
        Redraw();
    }

    public void Toggle()
    {
        if (Enabled)
            Hide();
        else
            Show();
    }

    public void Stop()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
            Enabled = false;
        }
        Redraw();
    }

    public T WithoutOverlay<T>(Func<T> capture)
    {
        Interlocked.Increment(ref _suppressDrawing);
        try
        {
            return capture();
        }
        finally
        {
            Interlocked.Decrement(ref _suppressDrawing);
        }
    }

    public void WriteStatus()
    {
        StatusSnapshot status = _status;
        RhinoMcpDashboardService? dashboard = RhinoMcpPlugin.Instance?.Dashboard;
        ClientLaunchSnapshot launch = RhinoMcpPlugin.Instance?.ClientLauncher.Snapshot
            ?? ClientLaunchSnapshot.NotAttempted;
        RhinoApp.WriteLine($"Rhino MCP: {status.OverallText}");
        RhinoApp.WriteLine($"  Bridge: {(status.BridgeRunning ? "connected" : "stopped")}");
        RhinoApp.WriteLine($"  {status.ClientLabel}: {(status.ClientConnected ? "connected" : status.ClientWaitingText)}");
        if (UserSettings.CodexConfigured && !status.ClientConnected)
            RhinoApp.WriteLine($"  Codex launch: {launch.Message}");
        RhinoApp.WriteLine($"  Grasshopper: {(status.GrasshopperAvailable ? "connected" : "not available")}");
        RhinoApp.WriteLine($"  Regulations: {(status.RegulationsAvailable ? "loaded" : "not installed")}");
        RhinoApp.WriteLine($"  Ports: Rhino {UserSettings.RhinoPort}, Grasshopper {UserSettings.GrasshopperPort}");
        RhinoApp.WriteLine($"  Dashboard: {(dashboard?.Running == true ? dashboard.Url : "not running")}");
    }

    public Dictionary<string, object?> DashboardStatus()
    {
        StatusSnapshot status = _status;
        RhinoMcpClientLauncher? launcher = RhinoMcpPlugin.Instance?.ClientLauncher;
        ClientLaunchSnapshot clientLaunch = launcher?.Snapshot
            ?? ClientLaunchSnapshot.NotAttempted;
        object[] issues = RhinoMcpInstallationDiagnostics.CompatibilityIssues(
            status.GrasshopperAvailable);
        bool grasshopperDanger = issues.Any(issue =>
            issue is Dictionary<string, object?> value
            && value.TryGetValue("tone", out object? tone)
            && string.Equals(tone?.ToString(), "danger",
                StringComparison.Ordinal));
        string overallState;
        string title;
        string message;
        if (!status.BridgeRunning)
        {
            overallState = "stopped";
            title = "Rhino bridge stopped";
            message = "Run RhinoMCPRestart in Rhino. This page will keep checking.";
        }
        else if (!status.ClientConfigured)
        {
            overallState = "setup";
            title = "Setup needed";
            message = "Run the Rhino MCP installer again and choose Codex, Claude, or Cursor.";
        }
        else if (!status.ClientConnected)
        {
            overallState = "waiting";
            if (UserSettings.CodexConfigured && clientLaunch.State == "opening")
            {
                title = "Opening Codex…";
                message = "When Codex appears, start writing what you want Rhino to create or change.";
            }
            else if (UserSettings.CodexConfigured && ClientLaunchReady(clientLaunch))
            {
                title = "Codex is open — start writing";
                message = "Type your Rhino request in Codex. This page turns green as soon as the prompt connects.";
            }
            else if (UserSettings.CodexConfigured
                && (clientLaunch.State == "not_found" || clientLaunch.State == "failed"))
            {
                title = "Codex needs attention";
                message = clientLaunch.Message;
            }
            else if (UserSettings.CodexConfigured && clientLaunch.State == "disabled")
            {
                title = "Ready for Codex";
                message = "Click Open Codex below, then start writing your Rhino request.";
            }
            else
            {
                title = $"Waiting for {status.ClientLabel}";
                message = $"Open or restart {status.ClientLabel}, then start a prompt.";
            }
        }
        else if (UserSettings.Profile == "grasshopper" && !status.GrasshopperAvailable)
        {
            overallState = "attention";
            title = "Rhino connected — Grasshopper needs attention";
            message = "The Rhino bridge is ready, but the Grasshopper bridge did not start. See the diagnostic and file paths below.";
        }
        else
        {
            overallState = "connected";
            title = "Connected to Rhino";
            message = $"Everything is ready. Start prompting in {status.ClientLabel}.";
        }

        return new Dictionary<string, object?>
        {
            ["version"] = typeof(RhinoMcpPlugin).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            ["updated_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["overall"] = new Dictionary<string, object?>
            {
                ["state"] = overallState,
                ["title"] = title,
                ["message"] = message,
            },
            ["services"] = new object[]
            {
                Service(
                    "bridge", "Rhino Bridge", "Communication bridge between Rhino and MCP.",
                    status.BridgeRunning ? "Connected" : "Stopped",
                    status.BridgeRunning ? "success" : "danger"),
                Service(
                    "client", status.ClientLabel, ClientDescription(status, clientLaunch),
                    ClientStatusText(status, clientLaunch),
                    status.ClientConnected ? "success" : status.ClientConfigured ? "warning" : "danger"),
                Service(
                    "grasshopper", "Grasshopper",
                    status.GrasshopperAvailable
                        ? "Grasshopper is open and available."
                        : "Grasshopper is closed or component loading stopped before the Rhino MCP bridge started.",
                    status.GrasshopperAvailable ? "Connected" : "Not available",
                    status.GrasshopperAvailable ? "success" : grasshopperDanger ? "danger" : "warning"),
                Service(
                    "regulations", "Regulations",
                    status.RegulationsAvailable
                        ? "Regulations data is loaded and available."
                        : "The offline regulations data is not installed.",
                    status.RegulationsAvailable ? "Loaded" : "Not installed",
                    status.RegulationsAvailable ? "success" : "danger"),
            },
            ["details"] = new Dictionary<string, object?>
            {
                ["rhino"] = $"Rhino {RhinoApp.Version.Major}",
                ["profile"] = $"{TitleCase(UserSettings.Profile)} profile",
                ["network"] = "127.0.0.1 only",
                ["refresh"] = "Live status refresh",
            },
            ["issues"] = issues,
            ["project_files"] = _projectFiles,
            ["installed_files"] = RhinoMcpInstallationDiagnostics.InstalledFiles(),
            ["client_launch"] = launcher?.DashboardStatus()
                ?? new Dictionary<string, object?>
                {
                    ["state"] = "unavailable",
                    ["message"] = "Rhino MCP is still starting.",
                    ["can_open"] = false,
                    ["auto_launch_enabled"] = UserSettings.AutoLaunchClient,
                },
        };
    }

    public static Dictionary<string, object?> UnavailableDashboardStatus() => new()
    {
        ["version"] = "unknown",
        ["updated_at"] = DateTimeOffset.UtcNow.ToString("O"),
        ["overall"] = new Dictionary<string, object?>
        {
            ["state"] = "offline",
            ["title"] = "Rhino is offline",
            ["message"] = "Open Rhino to reconnect. This page will keep trying.",
        },
        ["services"] = Array.Empty<object>(),
        ["details"] = new Dictionary<string, object?>(),
        ["issues"] = Array.Empty<object>(),
        ["project_files"] = Array.Empty<object>(),
        ["installed_files"] = Array.Empty<object>(),
        ["client_launch"] = new Dictionary<string, object?>
        {
            ["state"] = "unavailable",
            ["message"] = "Open Rhino before opening Codex from this page.",
            ["can_open"] = false,
            ["auto_launch_enabled"] = false,
        },
    };

    protected override void DrawForeground(DrawEventArgs e)
    {
        if (Volatile.Read(ref _suppressDrawing) > 0)
            return;

        Rhino.Display.RhinoView? activeView = e.RhinoDoc?.Views.ActiveView;
        if (activeView is null || activeView.ActiveViewportID != e.Viewport.Id)
            return;

        Rectangle bounds = e.Viewport.Bounds;
        if (bounds.Width < 260 || bounds.Height < 120)
            return;

        StatusSnapshot status = _status;
        int cardWidth = Math.Min(470, bounds.Width - 24);
        bool compact = cardWidth < 450;
        int cardHeight = compact ? 96 : 78;
        int left = bounds.Right - cardWidth - 12;
        int top = bounds.Top + 12;
        Rectangle card = new(left, top, cardWidth, cardHeight);
        Color overallColor = status.OverallColor;

        e.Display.Draw2dRectangle(card, Border, 1, Background);
        e.Display.Draw2dRectangle(new Rectangle(left, top, 5, cardHeight), overallColor, 1, overallColor);
        e.Display.Draw2dText(
            "RHINO MCP", PrimaryText, new Point2d(left + 17, top + 24), false, 15, "Segoe UI");
        e.Display.Draw2dText(
            status.OverallText, overallColor, new Point2d(left + 118, top + 24), false, 14, "Segoe UI");

        int itemTop = top + 49;
        DrawIndicator(e, left + 17, itemTop, "Bridge", status.BridgeRunning ? Green : Red);
        DrawIndicator(e, left + 112, itemTop, status.ClientLabel,
            status.ClientConnected ? Green : status.ClientConfigured ? Amber : Red);
        if (compact)
        {
            DrawIndicator(e, left + 17, top + 69, "Grasshopper",
                status.GrasshopperAvailable ? Green : Red);
            DrawIndicator(e, left + 166, top + 69, "Rules",
                status.RegulationsAvailable ? Green : Red);
            e.Display.Draw2dText(
                "RhinoMCP toggles this strip", SecondaryText,
                new Point2d(left + 17, top + 88), false, 10, "Segoe UI");
        }
        else
        {
            DrawIndicator(e, left + 242, itemTop, "Grasshopper",
                status.GrasshopperAvailable ? Green : Red);
            DrawIndicator(e, left + 388, itemTop, "Rules",
                status.RegulationsAvailable ? Green : Red);
            e.Display.Draw2dText(
                "Run RhinoMCP to hide or show this strip", SecondaryText,
                new Point2d(left + 17, top + 69), false, 10, "Segoe UI");
        }
    }

    private static void DrawIndicator(
        DrawEventArgs e, int left, int top, string label, Color statusColor)
    {
        e.Display.Draw2dText("●", statusColor, new Point2d(left, top), false, 11, "Segoe UI Symbol");
        e.Display.Draw2dText(label, SecondaryText, new Point2d(left + 14, top), false, 11, "Segoe UI");
    }

    private static Dictionary<string, object?> Service(
        string id, string label, string description, string status, string tone) => new()
    {
        ["id"] = id,
        ["label"] = label,
        ["description"] = description,
        ["status"] = status,
        ["tone"] = tone,
    };

    private static string ClientDescription(
        StatusSnapshot status,
        ClientLaunchSnapshot clientLaunch)
    {
        if (!status.ClientConfigured)
            return "Choose an AI client in Rhino MCP setup.";
        if (status.ClientConnected)
            return $"{status.ClientLabel} is connected and ready.";
        if (UserSettings.CodexConfigured && ClientLaunchReady(clientLaunch))
            return "Codex is open. Start a prompt to connect it to Rhino.";
        if (UserSettings.CodexConfigured && clientLaunch.State == "opening")
            return "Codex is opening now.";
        return $"{status.ClientLabel} is configured but has not connected yet.";
    }

    private static string ClientStatusText(
        StatusSnapshot status,
        ClientLaunchSnapshot clientLaunch)
    {
        if (status.ClientConnected)
            return "Connected";
        if (!status.ClientConfigured)
            return "Not configured";
        if (UserSettings.CodexConfigured && clientLaunch.State == "opening")
            return "Opening";
        if (UserSettings.CodexConfigured && ClientLaunchReady(clientLaunch))
            return "Start writing";
        return "Waiting";
    }

    private static bool ClientLaunchReady(ClientLaunchSnapshot snapshot) =>
        snapshot.State == "opened" || snapshot.State == "already_running";

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Basic";
        return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }

    private void PollStatus(object? state)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0)
            return;

        try
        {
            StatusSnapshot next = ReadStatus(probeGrasshopper: true);
            StatusSnapshot previous = _status;
            _status = next;
            bool changed = !next.SameAs(previous);
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                _projectFiles = ReadProjectFiles(next);
                if (changed)
                    Redraw();
            }));
        }
        catch
        {
            // Connection UI must never interfere with Rhino's modeling thread.
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private static StatusSnapshot ReadStatus(bool probeGrasshopper)
    {
        bool bridgeRunning = RhinoBridgeService.Instance.Running;
        bool clientConnected = RhinoBridgeService.Instance.ClientCount > 0;
        string configuredClient = UserSettings.Client;
        bool clientConfigured = configuredClient != "Not configured";
        string clientLabel = ClientLabel(configuredClient);
        ClientLaunchSnapshot clientLaunch = RhinoMcpPlugin.Instance?.ClientLauncher.Snapshot
            ?? ClientLaunchSnapshot.NotAttempted;
        GrasshopperSnapshot grasshopper = ReadGrasshopperStatus(probeGrasshopper);
        bool grasshopperAvailable = grasshopper.Available;
        bool regulationsAvailable = UserSettings.RegulationsAvailable;

        if (!bridgeRunning)
            return new StatusSnapshot(
                bridgeRunning, clientConnected, clientConfigured, clientLabel,
                grasshopperAvailable, regulationsAvailable, grasshopper.DefinitionOpen,
                grasshopper.DefinitionName, grasshopper.DefinitionPath, "BRIDGE STOPPED", Red);
        if (!clientConfigured)
            return new StatusSnapshot(
                bridgeRunning, clientConnected, clientConfigured, clientLabel,
                grasshopperAvailable, regulationsAvailable, grasshopper.DefinitionOpen,
                grasshopper.DefinitionName, grasshopper.DefinitionPath, "SETUP NEEDED", Red);
        if (!clientConnected)
            return new StatusSnapshot(
                bridgeRunning, clientConnected, clientConfigured, clientLabel,
                grasshopperAvailable, regulationsAvailable, grasshopper.DefinitionOpen,
                grasshopper.DefinitionName, grasshopper.DefinitionPath,
                UserSettings.CodexConfigured && ClientLaunchReady(clientLaunch)
                    ? "CODEX OPEN — START WRITING"
                    : $"WAITING FOR {clientLabel.ToUpperInvariant()}", Amber);
        if (UserSettings.Profile == "grasshopper" && !grasshopperAvailable)
            return new StatusSnapshot(
                bridgeRunning, clientConnected, clientConfigured, clientLabel,
                grasshopperAvailable, regulationsAvailable, grasshopper.DefinitionOpen,
                grasshopper.DefinitionName, grasshopper.DefinitionPath,
                "RHINO READY — GH OFFLINE", Amber);
        return new StatusSnapshot(
            bridgeRunning, clientConnected, clientConfigured, clientLabel,
            grasshopperAvailable, regulationsAvailable, grasshopper.DefinitionOpen,
            grasshopper.DefinitionName, grasshopper.DefinitionPath, "CONNECTED — READY", Green);
    }

    private static string ClientLabel(string configuredClient)
    {
        if (configuredClient == "Not configured")
            return "AI setup";
        if (configuredClient.IndexOf(",", StringComparison.Ordinal) >= 0)
            return "AI clients";
        return configuredClient.Length > 14 ? "AI client" : configuredClient;
    }

    private static GrasshopperSnapshot ReadGrasshopperStatus(bool probe)
    {
        if (!probe)
            return GrasshopperSnapshot.Unavailable;

        try
        {
            string? json = ReadGrasshopperHealthResponse();
            if (json is null || json.Length == 0)
                return GrasshopperSnapshot.Unavailable;
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("status", out JsonElement status)
                || status.GetString() != "ok")
                return GrasshopperSnapshot.Unavailable;
            bool definitionOpen = root.TryGetProperty("definition_open", out JsonElement open)
                && open.ValueKind == JsonValueKind.True;
            string name = root.TryGetProperty("definition_name", out JsonElement definitionName)
                ? definitionName.GetString() ?? "" : "";
            string path = root.TryGetProperty("definition_path", out JsonElement definitionPath)
                ? definitionPath.GetString() ?? "" : "";
            return new GrasshopperSnapshot(true, definitionOpen, name, path);
        }
        catch
        {
            return GrasshopperSnapshot.Unavailable;
        }
    }

    private static string? ReadGrasshopperHealthResponse()
    {
        using TcpClient client = new();
        if (!client.ConnectAsync("127.0.0.1", UserSettings.GrasshopperPort)
            .Wait(TimeSpan.FromMilliseconds(250)))
            return null;

        client.NoDelay = true;
        using NetworkStream stream = client.GetStream();
        stream.ReadTimeout = 350;
        stream.WriteTimeout = 350;
        byte[] request = Encoding.ASCII.GetBytes(
            "GET /health HTTP/1.1\r\nHost: 127.0.0.1\r\nAccept: application/json\r\n" +
            "Connection: close\r\n\r\n");
        stream.Write(request, 0, request.Length);

        byte[] response = new byte[64 * 1024];
        int length = 0;
        int headerEnd = -1;
        int contentLength = -1;
        while (length < response.Length)
        {
            int read = stream.Read(response, length, response.Length - length);
            if (read == 0)
                break;
            length += read;
            if (headerEnd < 0)
            {
                headerEnd = FindHeaderEnd(response, length);
                if (headerEnd >= 0)
                {
                    string header = Encoding.ASCII.GetString(response, 0, headerEnd);
                    if (!header.StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal)
                        && !header.StartsWith("HTTP/1.0 200 ", StringComparison.Ordinal))
                        return null;
                    contentLength = ContentLength(header);
                }
            }
            if (headerEnd >= 0 && contentLength >= 0
                && length >= headerEnd + 4 + contentLength)
                break;
        }

        if (headerEnd < 0)
            return null;
        int bodyStart = headerEnd + 4;
        int available = Math.Max(0, length - bodyStart);
        int bodyLength = contentLength >= 0 ? Math.Min(contentLength, available) : available;
        return Encoding.UTF8.GetString(response, bodyStart, bodyLength);
    }

    private static int FindHeaderEnd(byte[] bytes, int length)
    {
        for (int index = 3; index < length; index++)
        {
            if (bytes[index - 3] == '\r' && bytes[index - 2] == '\n'
                && bytes[index - 1] == '\r' && bytes[index] == '\n')
                return index - 3;
        }
        return -1;
    }

    private static int ContentLength(string header)
    {
        foreach (string line in header.Split(new[] { "\r\n" }, StringSplitOptions.None))
        {
            const string prefix = "Content-Length:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.Substring(prefix.Length).Trim(), out int value))
                return value;
        }
        return -1;
    }

    private static object[] ReadProjectFiles(StatusSnapshot status)
    {
        List<object> files = new();
        RhinoDoc? rhinoDocument = RhinoDoc.ActiveDoc;
        string rhinoPath = rhinoDocument?.Path ?? "";
        if (rhinoDocument is null)
        {
            files.Add(ProjectFile(
                "rhino-model", "Rhino model", "No Rhino document is open.", "",
                false, "Not open", "neutral"));
        }
        else if (string.IsNullOrWhiteSpace(rhinoPath))
        {
            files.Add(ProjectFile(
                "rhino-model", "Rhino model", "The current model has not been saved yet.", "",
                false, "Unsaved", "warning"));
        }
        else
        {
            bool exists = File.Exists(rhinoPath);
            files.Add(ProjectFile(
                "rhino-model", "Rhino model", rhinoDocument.Name,
                rhinoPath, exists, exists ? "Found" : "Path missing", exists ? "success" : "danger"));
        }

        if (!status.GrasshopperAvailable)
        {
            files.Add(ProjectFile(
                "grasshopper-definition", "Grasshopper definition",
                "The Grasshopper bridge did not start, so its active definition cannot be read.", "",
                false, "Unavailable", "danger"));
        }
        else if (!status.GrasshopperDefinitionOpen)
        {
            files.Add(ProjectFile(
                "grasshopper-definition", "Grasshopper definition",
                "Grasshopper is open without an active definition.", "",
                false, "Not open", "neutral"));
        }
        else if (string.IsNullOrWhiteSpace(status.GrasshopperDefinitionPath))
        {
            files.Add(ProjectFile(
                "grasshopper-definition", "Grasshopper definition",
                string.IsNullOrWhiteSpace(status.GrasshopperDefinitionName)
                    ? "The current definition has not been saved yet."
                    : status.GrasshopperDefinitionName,
                "", false, "Unsaved", "warning"));
        }
        else
        {
            bool exists = File.Exists(status.GrasshopperDefinitionPath);
            files.Add(ProjectFile(
                "grasshopper-definition", "Grasshopper definition",
                status.GrasshopperDefinitionName,
                status.GrasshopperDefinitionPath, exists,
                exists ? "Found" : "Path missing", exists ? "success" : "danger"));
        }
        return files.ToArray();
    }

    private static Dictionary<string, object?> ProjectFile(
        string id,
        string label,
        string description,
        string path,
        bool exists,
        string status,
        string tone) => new()
    {
        ["id"] = id,
        ["label"] = label,
        ["description"] = description,
        ["path"] = path,
        ["exists"] = exists,
        ["status"] = status,
        ["tone"] = tone,
    };

    private static void Redraw()
    {
        try
        {
            RhinoDoc.ActiveDoc?.Views.Redraw();
        }
        catch
        {
            // Rhino may already be shutting down.
        }
    }

    private sealed class StatusSnapshot
    {
        public static readonly StatusSnapshot Empty = new(
            false, false, false, "AI setup", false, false, false, "", "",
            "STARTING", Amber);

        public StatusSnapshot(
            bool bridgeRunning,
            bool clientConnected,
            bool clientConfigured,
            string clientLabel,
            bool grasshopperAvailable,
            bool regulationsAvailable,
            bool grasshopperDefinitionOpen,
            string grasshopperDefinitionName,
            string grasshopperDefinitionPath,
            string overallText,
            Color overallColor)
        {
            BridgeRunning = bridgeRunning;
            ClientConnected = clientConnected;
            ClientConfigured = clientConfigured;
            ClientLabel = clientLabel;
            GrasshopperAvailable = grasshopperAvailable;
            RegulationsAvailable = regulationsAvailable;
            GrasshopperDefinitionOpen = grasshopperDefinitionOpen;
            GrasshopperDefinitionName = grasshopperDefinitionName;
            GrasshopperDefinitionPath = grasshopperDefinitionPath;
            OverallText = overallText;
            OverallColor = overallColor;
        }

        public bool BridgeRunning { get; }
        public bool ClientConnected { get; }
        public bool ClientConfigured { get; }
        public string ClientLabel { get; }
        public bool GrasshopperAvailable { get; }
        public bool RegulationsAvailable { get; }
        public bool GrasshopperDefinitionOpen { get; }
        public string GrasshopperDefinitionName { get; }
        public string GrasshopperDefinitionPath { get; }
        public string OverallText { get; }
        public Color OverallColor { get; }
        public string ClientWaitingText => ClientConfigured ? "waiting" : "not configured";

        public bool SameAs(StatusSnapshot other) =>
            BridgeRunning == other.BridgeRunning
            && ClientConnected == other.ClientConnected
            && ClientConfigured == other.ClientConfigured
            && ClientLabel == other.ClientLabel
            && GrasshopperAvailable == other.GrasshopperAvailable
            && RegulationsAvailable == other.RegulationsAvailable
            && GrasshopperDefinitionOpen == other.GrasshopperDefinitionOpen
            && GrasshopperDefinitionName == other.GrasshopperDefinitionName
            && GrasshopperDefinitionPath == other.GrasshopperDefinitionPath
            && OverallText == other.OverallText;
    }

    private sealed class GrasshopperSnapshot
    {
        public static readonly GrasshopperSnapshot Unavailable = new(false, false, "", "");

        public GrasshopperSnapshot(bool available, bool definitionOpen, string definitionName, string definitionPath)
        {
            Available = available;
            DefinitionOpen = definitionOpen;
            DefinitionName = definitionName;
            DefinitionPath = definitionPath;
        }

        public bool Available { get; }
        public bool DefinitionOpen { get; }
        public string DefinitionName { get; }
        public string DefinitionPath { get; }
    }
}
