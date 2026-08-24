using System.Drawing;
using System.Net.Sockets;
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
    private static readonly Color Muted = Color.FromArgb(255, 137, 145, 157);
    private static readonly Color Green = Color.FromArgb(255, 52, 190, 116);
    private static readonly Color Amber = Color.FromArgb(255, 235, 168, 55);
    private static readonly Color Red = Color.FromArgb(255, 226, 82, 82);

    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private volatile StatusSnapshot _status = StatusSnapshot.Empty;
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
        RhinoApp.WriteLine($"Rhino MCP: {status.OverallText}");
        RhinoApp.WriteLine($"  Bridge: {(status.BridgeRunning ? "connected" : "stopped")}");
        RhinoApp.WriteLine($"  {status.ClientLabel}: {(status.ClientConnected ? "connected" : status.ClientWaitingText)}");
        RhinoApp.WriteLine($"  Grasshopper: {(status.GrasshopperAvailable ? "connected" : "not open")}");
        RhinoApp.WriteLine($"  Regulations: {(status.RegulationsAvailable ? "loaded" : "not installed")}");
        RhinoApp.WriteLine($"  Ports: Rhino {UserSettings.RhinoPort}, Grasshopper {UserSettings.GrasshopperPort}");
        RhinoApp.WriteLine($"  Dashboard: {(dashboard?.Running == true ? dashboard.Url : "not running")}");
    }

    public Dictionary<string, object?> DashboardStatus()
    {
        StatusSnapshot status = _status;
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
            title = $"Waiting for {status.ClientLabel}";
            message = $"Open or restart {status.ClientLabel}, then start a prompt.";
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
                    "client", status.ClientLabel, ClientDescription(status),
                    status.ClientConnected ? "Connected" : status.ClientConfigured ? "Waiting" : "Not configured",
                    status.ClientConnected ? "success" : status.ClientConfigured ? "warning" : "danger"),
                Service(
                    "grasshopper", "Grasshopper",
                    status.GrasshopperAvailable
                        ? "Grasshopper is open and available."
                        : "Grasshopper is not currently open.",
                    status.GrasshopperAvailable ? "Connected" : "Not open",
                    status.GrasshopperAvailable ? "success" : "neutral"),
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
                status.GrasshopperAvailable ? Green : Muted);
            DrawIndicator(e, left + 166, top + 69, "Rules",
                status.RegulationsAvailable ? Green : Red);
            e.Display.Draw2dText(
                "RhinoMCP toggles this strip", SecondaryText,
                new Point2d(left + 17, top + 88), false, 10, "Segoe UI");
        }
        else
        {
            DrawIndicator(e, left + 242, itemTop, "Grasshopper",
                status.GrasshopperAvailable ? Green : Muted);
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

    private static string ClientDescription(StatusSnapshot status)
    {
        if (!status.ClientConfigured)
            return "Choose an AI client in Rhino MCP setup.";
        return status.ClientConnected
            ? $"{status.ClientLabel} is connected and ready."
            : $"{status.ClientLabel} is configured but has not connected yet.";
    }

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
            if (!next.SameAs(previous))
                RhinoApp.InvokeOnUiThread((Action)Redraw);
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
        bool grasshopperAvailable = probeGrasshopper && PortOpen(UserSettings.GrasshopperPort);
        bool regulationsAvailable = UserSettings.RegulationsAvailable;

        if (!bridgeRunning)
            return new StatusSnapshot(
                bridgeRunning, clientConnected, clientConfigured, clientLabel,
                grasshopperAvailable, regulationsAvailable, "BRIDGE STOPPED", Red);
        if (!clientConfigured)
            return new StatusSnapshot(
                bridgeRunning, clientConnected, clientConfigured, clientLabel,
                grasshopperAvailable, regulationsAvailable, "SETUP NEEDED", Red);
        if (!clientConnected)
            return new StatusSnapshot(
                bridgeRunning, clientConnected, clientConfigured, clientLabel,
                grasshopperAvailable, regulationsAvailable, $"WAITING FOR {clientLabel.ToUpperInvariant()}", Amber);
        return new StatusSnapshot(
            bridgeRunning, clientConnected, clientConfigured, clientLabel,
            grasshopperAvailable, regulationsAvailable, "CONNECTED — READY", Green);
    }

    private static string ClientLabel(string configuredClient)
    {
        if (configuredClient == "Not configured")
            return "AI setup";
        if (configuredClient.IndexOf(",", StringComparison.Ordinal) >= 0)
            return "AI clients";
        return configuredClient.Length > 14 ? "AI client" : configuredClient;
    }

    private static bool PortOpen(int port)
    {
        try
        {
            using TcpClient client = new();
            return client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(75));
        }
        catch
        {
            return false;
        }
    }

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
            false, false, false, "AI setup", false, false, "STARTING", Amber);

        public StatusSnapshot(
            bool bridgeRunning,
            bool clientConnected,
            bool clientConfigured,
            string clientLabel,
            bool grasshopperAvailable,
            bool regulationsAvailable,
            string overallText,
            Color overallColor)
        {
            BridgeRunning = bridgeRunning;
            ClientConnected = clientConnected;
            ClientConfigured = clientConfigured;
            ClientLabel = clientLabel;
            GrasshopperAvailable = grasshopperAvailable;
            RegulationsAvailable = regulationsAvailable;
            OverallText = overallText;
            OverallColor = overallColor;
        }

        public bool BridgeRunning { get; }
        public bool ClientConnected { get; }
        public bool ClientConfigured { get; }
        public string ClientLabel { get; }
        public bool GrasshopperAvailable { get; }
        public bool RegulationsAvailable { get; }
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
            && OverallText == other.OverallText;
    }
}
