using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RhinoMCP;

[Guid("6A8B5410-F94F-45DC-A95C-F43A2464BB87")]
public sealed class RhinoMcpPanel : Panel, IPanel
{
    private readonly Label _server = new();
    private readonly Label _rhino = new();
    private readonly Label _grasshopper = new();
    private readonly Label _client = new();
    private readonly Label _ports = new();
    private readonly TextArea _logs = new() { ReadOnly = true, Height = 180 };
    private readonly UITimer _timer;
    private bool _grasshopperAvailable;
    private int _grasshopperProbeRunning;

    public static Guid PanelId => typeof(RhinoMcpPanel).GUID;

    public RhinoMcpPanel(uint documentSerialNumber)
    {
        Button restart = new() { Text = "Restart" };
        restart.Click += (_, _) =>
        {
            RhinoBridgeService.Instance.Restart(UserSettings.RhinoPort);
            RefreshStatus();
        };

        Button test = new() { Text = "Create test cube" };
        test.Click += (_, _) =>
        {
            Dictionary<string, object?> result = RhinoCommandDispatcher.TestConnection(true);
            BridgeLog.Write(result.TryGetValue("message", out object? value)
                ? value?.ToString() ?? "Test finished." : "Test finished.");
            RefreshStatus();
        };

        Button doctor = new() { Text = "Copy doctor command" };
        doctor.Click += (_, _) => Clipboard.Instance.Text = "rhino-mcp doctor";

        DynamicLayout layout = new() { Padding = 14, DefaultSpacing = new Size(8, 8) };
        layout.AddRow(new Label { Text = "RHINO MCP", Font = new Font(SystemFont.Bold, 15) });
        layout.AddRow(new Label { Text = "Prompt Rhino from Codex, Claude, or Cursor." });
        layout.AddRow(new Label { Text = "Connection" });
        layout.AddRow(new Label { Text = "MCP server" }, _server);
        layout.AddRow(new Label { Text = "Rhino bridge" }, _rhino);
        layout.AddRow(new Label { Text = "Grasshopper" }, _grasshopper);
        layout.AddRow(new Label { Text = "AI client" }, _client);
        layout.AddRow(new Label { Text = "Ports" }, _ports);
        layout.AddSeparateRow(restart, test, doctor, null);
        layout.AddRow(new Label { Text = "Logs" });
        layout.AddRow(_logs);
        layout.Add(null);
        Content = layout;

        BridgeLog.Changed += OnLogChanged;
        _timer = new UITimer { Interval = 1.0 };
        _timer.Elapsed += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
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

    private void RefreshStatus()
    {
        SetStatus(_server, RhinoBridgeService.Instance.ClientCount > 0,
            "Connected", "Waiting for AI client");
        SetStatus(_rhino, RhinoBridgeService.Instance.Running, "Connected", "Stopped");
        SetStatus(_grasshopper, _grasshopperAvailable, "Available", "Not running");
        _client.Text = $"{UserSettings.Client} · {UserSettings.Profile}";
        _ports.Text = $"Rhino {UserSettings.RhinoPort} · GH {UserSettings.GrasshopperPort}";
        _logs.Text = BridgeLog.Text;
        _logs.CaretIndex = _logs.Text.Length;
        ProbeGrasshopper();
    }

    private void ProbeGrasshopper()
    {
        if (Interlocked.Exchange(ref _grasshopperProbeRunning, 1) != 0)
            return;
        _ = Task.Run(() => PortOpen(UserSettings.GrasshopperPort)).ContinueWith(task =>
        {
            _grasshopperAvailable = task.Status == TaskStatus.RanToCompletion && task.Result;
            Interlocked.Exchange(ref _grasshopperProbeRunning, 0);
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                SetStatus(_grasshopper, _grasshopperAvailable, "Available", "Not running")));
        }, TaskScheduler.Default);
    }

    private static void SetStatus(Label label, bool ready, string readyText, string waitingText)
    {
        label.Text = $"●  {(ready ? readyText : waitingText)}";
        label.TextColor = ready ? Color.FromArgb(34, 160, 91) : Color.FromArgb(205, 132, 27);
    }

    private void OnLogChanged() => Rhino.RhinoApp.InvokeOnUiThread((Action)RefreshStatus);

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason) => RefreshStatus();
    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        _timer.Stop();
        BridgeLog.Changed -= OnLogChanged;
    }
}
