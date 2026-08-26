from pathlib import Path

ROOT = Path(__file__).parents[1]


def test_rhino_status_strip_appears_on_every_app_launch():
    plugin = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpPlugin.cs").read_text()

    assert "RhinoApp.Idle += ShowStatusOnStartup;" in plugin
    assert "StatusHud.Start();" in plugin
    assert "Panels.RegisterPanel" not in plugin
    assert "Panels.OpenPanel" not in plugin
    assert not (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpPanel.cs").exists()


def test_status_strip_is_drawn_only_in_the_active_modeling_view():
    hud = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpStatusHud.cs").read_text()

    assert "class RhinoMcpStatusHud : DisplayConduit" in hud
    assert "protected override void DrawForeground" in hud
    assert "activeView.ActiveViewportID != e.Viewport.Id" in hud
    assert "Draw2dRectangle" in hud
    assert "Draw2dText" in hud
    assert '"CONNECTED — READY"' in hud
    assert '"WAITING FOR {clientLabel.ToUpperInvariant()}"' in hud
    assert '"CODEX OPEN — START WRITING"' in hud


def test_status_strip_reports_each_service_and_can_be_hidden():
    hud = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpStatusHud.cs").read_text()
    command = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpCommand.cs").read_text()

    for label in ("Bridge", "Grasshopper", "Rules"):
        assert f'"{label}"' in hud
    assert "UserSettings.Client" in hud
    assert "UserSettings.RegulationsAvailable" in hud
    assert '"Run RhinoMCP to hide or show this strip"' in hud
    assert "status.Toggle();" in command
    assert 'EnglishName => "RhinoMCPStatus"' in command
    assert 'EnglishName => "RhinoMCPDashboard"' in command
    assert 'EnglishName => "RhinoMCPRestart"' in command
    assert 'EnglishName => "RhinoMCPTest"' in command


def test_status_strip_is_not_baked_into_ai_viewport_captures():
    hud = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpStatusHud.cs").read_text()
    dispatcher = (
        ROOT / "native" / "RhinoMCP.Plugin" / "RhinoCommandDispatcher.cs"
    ).read_text()

    assert "WithoutOverlay" in hud
    assert "StatusHud.WithoutOverlay" in dispatcher
