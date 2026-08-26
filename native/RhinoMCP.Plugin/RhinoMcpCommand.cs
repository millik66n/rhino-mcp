using Rhino;
using Rhino.Commands;

namespace RhinoMCP;

public sealed class RhinoMcpCommand : Command
{
    public override string EnglishName => "RhinoMCP";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoMcpStatusHud? status = RhinoMcpPlugin.Instance?.StatusHud;
        if (status is null)
            return Result.Failure;
        status.Toggle();
        RhinoApp.WriteLine(status.IsVisible
            ? "Rhino MCP connection strip shown."
            : "Rhino MCP connection strip hidden. Run RhinoMCP again to show it.");
        return Result.Success;
    }
}

public sealed class RhinoMcpStatusCommand : Command
{
    public override string EnglishName => "RhinoMCPStatus";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoMcpStatusHud? status = RhinoMcpPlugin.Instance?.StatusHud;
        if (status is null)
            return Result.Failure;
        status.Show();
        status.WriteStatus();
        return Result.Success;
    }
}

public sealed class RhinoMcpDashboardCommand : Command
{
    public override string EnglishName => "RhinoMCPDashboard";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoMcpDashboardService? dashboard = RhinoMcpPlugin.Instance?.Dashboard;
        if (dashboard is null)
            return Result.Failure;
        dashboard.Start(UserSettings.DashboardPort);
        if (!dashboard.OpenBrowser(force: true))
            return Result.Failure;
        RhinoApp.WriteLine($"Rhino MCP dashboard: {dashboard.Url}");
        return Result.Success;
    }
}

public sealed class RhinoMcpRestartCommand : Command
{
    public override string EnglishName => "RhinoMCPRestart";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoBridgeService.Instance.Restart(UserSettings.RhinoPort);
        RhinoMcpPlugin.Instance?.StatusHud.Show();
        RhinoApp.WriteLine("Rhino MCP bridge restarted.");
        return RhinoBridgeService.Instance.Running ? Result.Success : Result.Failure;
    }
}

public sealed class RhinoMcpTestCommand : Command
{
    public override string EnglishName => "RhinoMCPTest";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        try
        {
            Dictionary<string, object?> result = RhinoCommandDispatcher.TestConnection(true);
            RhinoApp.WriteLine(result.TryGetValue("message", out object? message)
                ? message?.ToString() ?? "Connection test finished."
                : "Connection test finished.");
            return Result.Success;
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine($"Rhino MCP test failed: {exception.Message}");
            return Result.Failure;
        }
    }
}
