using Rhino;
using Rhino.PlugIns;
using System.Runtime.InteropServices;

namespace RhinoMCP;

[Guid("0E59A34D-7906-45DC-B8A1-B1D8219A841E")]
public sealed class RhinoMcpPlugin : PlugIn
{
    public static RhinoMcpPlugin? Instance { get; private set; }
    internal RhinoMcpStatusHud StatusHud { get; } = new();
    internal RhinoMcpDashboardService Dashboard { get; } = new();
    internal RhinoMcpClientLauncher ClientLauncher { get; } = new();

    public RhinoMcpPlugin() => Instance = this;

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        BridgeLog.Write("Rhino MCP plug-in loaded; starting local services.");
        StartSafely("scene tracking", SceneChangeTracker.Start);
        StartSafely("Rhino bridge", () =>
        {
            RhinoBridgeService.Instance.Start(UserSettings.RhinoPort);
        });
        StartSafely("connection dashboard", () =>
        {
            Dashboard.Start(UserSettings.DashboardPort);
        });
        _ = Task.Run(RhinoMcpInstallationDiagnostics.RefreshCompatibilityScan);
        BridgeLog.Write(RhinoBridgeService.Instance.Running
            ? "Rhino bridge started automatically."
            : "Rhino bridge is stopped; RhinoMCPRestart remains available for recovery.");
        RhinoApp.Idle += ShowStatusOnStartup;
        return LoadReturnCode.Success;
    }

    private static void StartSafely(string component, Action start)
    {
        try
        {
            start();
        }
        catch (Exception exception)
        {
            BridgeLog.Write($"Could not start {component}: {exception.Message}");
        }
    }

    private void ShowStatusOnStartup(object? sender, EventArgs args)
    {
        RhinoApp.Idle -= ShowStatusOnStartup;
        StartSafely("connection strip", () => { StatusHud.Start(); });
        StartSafely("browser dashboard", () => { Dashboard.OpenBrowser(); });
        StartSafely("Codex launcher", () =>
        {
            _ = ClientLauncher.OpenConfiguredCodexAsync(automatic: true);
        });
    }

    protected override void OnShutdown()
    {
        RhinoApp.Idle -= ShowStatusOnStartup;
        Dashboard.Stop();
        StatusHud.Stop();
        SceneChangeTracker.Stop();
        RhinoBridgeService.Instance.Dispose();
        base.OnShutdown();
    }
}
