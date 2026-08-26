import re
from pathlib import Path

ROOT = Path(__file__).parents[1]
PLUGIN = ROOT / "native" / "RhinoMCP.Plugin"
GRASSHOPPER = ROOT / "native" / "RhinoMCP.Grasshopper"


def test_dashboard_is_embedded_local_only_and_read_only():
    service = (PLUGIN / "RhinoMcpDashboardService.cs").read_text()
    project = (PLUGIN / "RhinoMCP.Plugin.csproj").read_text()

    assert "new(IPAddress.Loopback, preferredPort)" in service
    assert "TcpListener" in service
    assert "HttpListener" not in service
    assert 'method != "GET" && !headOnly' in service
    assert '"Allow: GET, HEAD\\r\\n"' in service
    assert "Cache-Control: no-store" in service
    assert "X-Content-Type-Options: nosniff" in service
    assert "Content-Security-Policy:" in service
    assert "UseShellExecute = true" in service
    assert 'LogicalName="RhinoMCP.Dashboard.index.html"' in project


def test_dashboard_opens_once_per_rhino_launch_and_has_a_reopen_command():
    plugin = (PLUGIN / "RhinoMcpPlugin.cs").read_text()
    command = (PLUGIN / "RhinoMcpCommand.cs").read_text()

    assert "Dashboard.Start(UserSettings.DashboardPort);" in plugin
    assert "RhinoApp.Idle += ShowStatusOnStartup;" in plugin
    assert "RhinoApp.Idle -= ShowStatusOnStartup;" in plugin
    assert "Dashboard.OpenBrowser();" in plugin
    assert "Dashboard.Stop();" in plugin
    assert 'EnglishName => "RhinoMCPDashboard"' in command
    assert "dashboard.OpenBrowser()" in command


def test_dashboard_page_has_live_connected_and_offline_states():
    html = (PLUGIN / "Dashboard" / "index.html").read_text()

    for copy in (
        "Connected to Rhino",
        "Everything is ready. Start prompting in",
        "Rhino Bridge",
        "Grasshopper",
        "Regulations",
        "Live status refresh",
        "Rhino is offline",
        "Open Rhino to reconnect. This page will keep trying.",
        "RhinoMCPDashboard",
        "Refresh now",
        "Project files",
        "Installed files",
        "Copy path",
        "Grasshopper needs attention",
    ):
        assert copy in html or copy in (PLUGIN / "RhinoMcpStatusHud.cs").read_text()

    assert 'fetch("/api/status"' in html
    assert "window.setInterval(refresh, 1000)" in html
    assert "navigator.clipboard.writeText(path)" in html
    assert "projectFilesSignature" in html
    assert "installedFilesSignature" in html
    assert 'aria-live="polite"' in html
    assert "@media (max-width: 430px)" in html
    assert not re.search(r'(?:src|href)=["\']https?://', html)


def test_status_api_reports_all_services_and_local_details():
    hud = (PLUGIN / "RhinoMcpStatusHud.cs").read_text()

    for service_id in ("bridge", "client", "grasshopper", "regulations"):
        assert f'"{service_id}"' in hud
    assert '"127.0.0.1 only"' in hud
    assert '"Live status refresh"' in hud
    assert 'title = "Connected to Rhino"' in hud
    assert 'title = "Setup needed"' in hud
    assert 'title = "Rhino bridge stopped"' in hud
    assert 'overallState = "attention"' in hud
    assert '"project_files"' in hud
    assert '"installed_files"' in hud
    assert '"issues"' in hud


def test_dashboard_reports_active_project_and_grasshopper_definition_paths():
    hud = (PLUGIN / "RhinoMcpStatusHud.cs").read_text()
    bridge = (GRASSHOPPER / "GrasshopperBridgeService.cs").read_text()

    assert "RhinoDoc.ActiveDoc" in hud
    assert '"rhino-model"' in hud
    assert '"grasshopper-definition"' in hud
    assert 'root.TryGetProperty("definition_path"' in hud
    assert '["definition_open"]' in bridge
    assert '["definition_name"]' in bridge
    assert '["definition_path"]' in bridge
    assert "document?.FilePath" in bridge


def test_dashboard_inventory_is_read_only_and_explains_addon_compatibility():
    diagnostics = (PLUGIN / "RhinoMcpInstallationDiagnostics.cs").read_text()

    for identifier in (
        "rhino-plugin",
        "grasshopper-addon",
        "mcp-server",
        "settings",
        "regulations",
        "install-log",
    ):
        assert f'"{identifier}"' in diagnostics
    assert "possible-archicad-conflict" in diagnostics
    assert "newer minor SDK" in diagnostics
    assert "MaxScannedDirectories" in diagnostics
    assert "File.Delete" not in diagnostics
    assert "Directory.Delete" not in diagnostics
