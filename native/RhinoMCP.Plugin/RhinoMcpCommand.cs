using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace RhinoMCP;

public sealed class RhinoMcpCommand : Command
{
    public override string EnglishName => "RhinoMCP";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (Panels.IsPanelVisible(RhinoMcpPanel.PanelId))
            Panels.ClosePanel(RhinoMcpPanel.PanelId);
        else
            Panels.OpenPanel(RhinoMcpPanel.PanelId);
        return Result.Success;
    }
}
