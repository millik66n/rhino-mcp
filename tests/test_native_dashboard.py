import re
from pathlib import Path

ROOT = Path(__file__).parents[1]
PLUGIN = ROOT / "native" / "RhinoMCP.Plugin"
GRASSHOPPER = ROOT / "native" / "RhinoMCP.Grasshopper"


def test_dashboard_is_embedded_local_and_protects_its_only_launch_action():
    service = (PLUGIN / "RhinoMcpDashboardService.cs").read_text()
    project = (PLUGIN / "RhinoMCP.Plugin.csproj").read_text()

    assert "new(IPAddress.Loopback, preferredPort)" in service
    assert "TcpListener" in service
    assert "HttpListener" not in service
    assert 'method != "GET" && !headOnly' in service
    assert 'method == "POST" && path == "/api/open-codex"' in service
    assert '"X-Rhino-MCP-Action"' in service
    assert 'host, $"127.0.0.1:{Port}"' in service
    assert 'HeaderValue(request, "Origin")' in service
    assert 'Guid.NewGuid().ToString("N")' in service
    assert '"Allow: GET, HEAD, POST\\r\\n"' in service
    assert "Cache-Control: no-store" in service
    assert "X-Content-Type-Options: nosniff" in service
    assert "Content-Security-Policy:" in service
    assert "UseShellExecute = true" in service
    assert 'LastBrowser = "Google Chrome"' in service
    assert '"chrome.exe"' in service
    assert 'LogicalName="RhinoMCP.Dashboard.index.html"' in project


def test_dashboard_opens_once_per_rhino_launch_and_has_a_reopen_command():
    plugin = (PLUGIN / "RhinoMcpPlugin.cs").read_text()
    command = (PLUGIN / "RhinoMcpCommand.cs").read_text()

    assert "Dashboard.Start(UserSettings.DashboardPort);" in plugin
    assert "RhinoApp.Idle += ShowStatusOnStartup;" in plugin
    assert "RhinoApp.Idle -= ShowStatusOnStartup;" in plugin
    assert "Dashboard.OpenBrowser();" in plugin
    assert "ClientLauncher.OpenConfiguredCodexAsync(automatic: true)" in plugin
    assert "Dashboard.Stop();" in plugin
    assert 'EnglishName => "RhinoMCPDashboard"' in command
    assert "dashboard.OpenBrowser(force: true)" in command


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
        "Codex is open — start writing",
        "Open Codex",
        "Begin every Rhino request with",
        "/RhinoMCP",
        "Copy /RhinoMCP",
    ):
        assert copy in html or copy in (PLUGIN / "RhinoMcpStatusHud.cs").read_text()

    assert 'fetch("/api/status"' in html
    assert 'fetch("/api/open-codex"' in html
    assert '"X-Rhino-MCP-Action": actionToken' in html
    assert 'const actionToken = "__RHINO_MCP_ACTION_TOKEN__"' in html
    assert "window.setInterval(refresh, 1000)" in html
    assert "await copyText(path)" in html
    assert 'await copyText("/RhinoMCP ")' in html
    assert "document.execCommand(\"copy\")" in html
    assert "projectFilesSignature" in html
    assert "installedFilesSignature" in html
    assert 'aria-live="polite"' in html
    assert "@media (max-width: 430px)" in html
    assert not re.search(r'(?:src|href)=["\']https?://', html)


def test_bridge_can_display_dashboard_in_chrome_for_cold_start_requests():
    dispatcher = (PLUGIN / "RhinoCommandDispatcher.cs").read_text()
    dashboard = (PLUGIN / "RhinoMcpDashboardService.cs").read_text()

    assert '"open_dashboard" => OpenDashboard(parameters)' in dispatcher
    assert 'GetBool(parameters, "prefer_chrome", true)' in dispatcher
    assert 'GetBool(parameters, "force", false)' in dispatcher
    assert "TryOpenChrome()" in dashboard
    assert 'Arguments = $"--new-tab' in dashboard


def test_codex_launcher_discovers_windows_store_and_desktop_installs():
    launcher = (PLUGIN / "RhinoMcpClientLauncher.cs").read_text()
    settings = (PLUGIN / "UserSettings.cs").read_text()

    for evidence in (
        'Process.GetProcessesByName(processName)',
        'Environment.SpecialFolder.StartMenu',
        'Environment.SpecialFolder.CommonStartMenu',
        '"Codex.exe"',
        '"ChatGPT.exe"',
        'Get-StartApps',
        "shell:AppsFolder",
        'SetForegroundWindow',
    ):
        assert evidence in launcher
    assert 'ReadBool("auto_launch_client", true)' in settings
    assert '"codex", StringComparer.OrdinalIgnoreCase' in settings


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
    assert '"client_launch"' in hud


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
        "codex-skill",
        "codex-prompt",
        "codex-guidance",
    ):
        assert f'"{identifier}"' in diagnostics
    assert "possible-archicad-conflict" in diagnostics
    assert "newer minor SDK" in diagnostics
    assert "MaxScannedDirectories" in diagnostics
    assert "File.Delete" not in diagnostics
    assert "Directory.Delete" not in diagnostics
