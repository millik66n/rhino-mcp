using System.Drawing;
using Grasshopper.Kernel;

namespace RhinoMCP.Grasshopper;

public sealed class RhinoMcpGrasshopperInfo : GH_AssemblyInfo
{
    public override string Name => "Rhino MCP";
    public override string Description => "Automatic Grasshopper bridge for Rhino MCP.";
    public override Guid Id => new("2F2A9720-D8DD-4D86-A245-FF444277A44B");
    public override string AuthorName => "millik66n";
    public override string AuthorContact => "https://github.com/millik66n/rhino-mcp";
    public override Bitmap? Icon => null;
}

public sealed class RhinoMcpGrasshopperPriority : GH_AssemblyPriority
{
    public override GH_LoadingInstruction PriorityLoad()
    {
        GrasshopperBridgeService.Instance.Start(GrasshopperUserSettings.Port);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => GrasshopperBridgeService.Instance.Dispose();
        return GH_LoadingInstruction.Proceed;
    }
}

internal static class GrasshopperUserSettings
{
    private static System.Text.Json.JsonElement? Root
    {
        get
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".rhino-mcp", "config.json");
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
                    File.ReadAllText(path));
                return document.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }
    }

    public static int Port => Root is { } root
        && root.TryGetProperty("grasshopper_port", out var value)
        && value.TryGetInt32(out int port) ? port : 9999;

    public static string Profile => Root is { } root
        && root.TryGetProperty("profile", out var value)
        ? value.GetString() ?? "basic" : "basic";
}
