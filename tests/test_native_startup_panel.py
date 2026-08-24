from pathlib import Path

ROOT = Path(__file__).parents[1]


def test_rhino_panel_opens_on_every_app_launch():
    plugin = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpPlugin.cs").read_text()

    assert "RhinoApp.Idle += OpenPanelOnStartup;" in plugin
    assert "Panels.OpenPanel(RhinoMcpPanel.PanelId);" in plugin
    assert "panel_introduced" not in plugin


def test_rhino_panel_has_persistent_overall_connection_message():
    panel = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpPanel.cs").read_text()

    assert "Connected — ready to prompt Rhino" in panel
    assert "Rhino MCP is running — waiting for" in panel
    assert 'new Label { Text = "Regulatory library" }' in panel
    assert "UserSettings.RegulationsAvailable" in panel
    assert "PanelHidden" in panel and "_timer.Stop()" in panel
