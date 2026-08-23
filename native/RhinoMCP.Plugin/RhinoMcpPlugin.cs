using Rhino;
using Rhino.PlugIns;
using Rhino.UI;
using System.Runtime.InteropServices;

namespace RhinoMCP;

[Guid("0E59A34D-7906-45DC-B8A1-B1D8219A841E")]
public sealed class RhinoMcpPlugin : PlugIn
{
    public static RhinoMcpPlugin? Instance { get; private set; }

    public RhinoMcpPlugin() => Instance = this;

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        Panels.RegisterPanel(this, typeof(RhinoMcpPanel), "Rhino MCP", null, PanelType.PerDoc);
        SceneChangeTracker.Start();
        RhinoBridgeService.Instance.Start(UserSettings.RhinoPort);
        BridgeLog.Write("Rhino bridge started automatically.");
        if (!Settings.GetBool("panel_introduced", false))
            RhinoApp.Idle += OpenPanelOnce;
        return LoadReturnCode.Success;
    }

    private void OpenPanelOnce(object? sender, EventArgs args)
    {
        RhinoApp.Idle -= OpenPanelOnce;
        Panels.OpenPanel(RhinoMcpPanel.PanelId);
        Settings.SetBool("panel_introduced", true);
    }

    protected override void OnShutdown()
    {
        RhinoApp.Idle -= OpenPanelOnce;
        SceneChangeTracker.Stop();
        RhinoBridgeService.Instance.Dispose();
        base.OnShutdown();
    }
}
