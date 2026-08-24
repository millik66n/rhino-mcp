import re
from pathlib import Path

ROOT = Path(__file__).parents[1]
PLUGIN = ROOT / "native" / "RhinoMCP.Plugin"


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
    ):
        assert copy in html or copy in (PLUGIN / "RhinoMcpStatusHud.cs").read_text()

    assert 'fetch("/api/status"' in html
    assert "window.setInterval(refresh, 1000)" in html
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
