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
        SceneChangeTracker.Start();
        RhinoBridgeService.Instance.Start(UserSettings.RhinoPort);
        Dashboard.Start(UserSettings.DashboardPort);
        _ = Task.Run(RhinoMcpInstallationDiagnostics.RefreshCompatibilityScan);
        BridgeLog.Write("Rhino bridge started automatically.");
        RhinoApp.Idle += ShowStatusOnStartup;
        return LoadReturnCode.Success;
    }

    private void ShowStatusOnStartup(object? sender, EventArgs args)
    {
        RhinoApp.Idle -= ShowStatusOnStartup;
        StatusHud.Start();
        Dashboard.OpenBrowser();
        _ = ClientLauncher.OpenConfiguredCodexAsync(automatic: true);
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
