from pathlib import Path

from rhino_mcp.config import Settings
from rhino_mcp.protocol import BridgeUnavailable

from rhino_mcp import startup


class FakeConnection:
    def __init__(self, health):
        self.health = list(health)
        self.requests = []

    def ping(self):
        result = self.health.pop(0)
        if isinstance(result, Exception):
            raise result
        return {"data": result}

    def request(self, command, params):
        self.requests.append((command, params))
        return {
            "data": {
                "opened": True,
                "browser": "Google Chrome",
                "url": "http://127.0.0.1:9877/",
            }
        }


def test_ready_rhino_forces_dashboard_to_display_again():
    connection = FakeConnection([{"rhino_version": "8", "document": "Test.3dm"}])

    result = startup.RhinoStartup(Settings(), connection).ensure_ready()

    assert result["ok"] is True
    assert result["mcp_server"] == "connected"
    assert result["dashboard"]["browser"] == "Google Chrome"
    assert connection.requests == [
        ("open_dashboard", {"force": True, "prefer_chrome": True})
    ]


def test_closed_rhino_launches_and_waits_for_bridge(monkeypatch, tmp_path):
    executable = tmp_path / "Rhino.exe"
    executable.touch()
    connection = FakeConnection(
        [
            BridgeUnavailable("offline"),
            {"rhino_version": "8.1", "document": "Untitled"},
        ]
    )
    launches = []

    class Process:
        def poll(self):
            return None

    monkeypatch.setattr(startup.os, "name", "nt")
    monkeypatch.setattr(startup, "rhino_process_running", lambda: False)
    monkeypatch.setattr(startup, "find_rhino_executable", lambda: executable)
    monkeypatch.setattr(
        startup.subprocess,
        "Popen",
        lambda command, **kwargs: launches.append((command, kwargs)) or Process(),
    )
    monkeypatch.setattr(startup.time, "sleep", lambda _seconds: None)
    monkeypatch.setattr(startup.time, "monotonic", lambda: 0.0)

    result = startup.RhinoStartup(Settings(), connection).ensure_ready(wait_seconds=30)

    assert result["ok"] is True
    assert result["launched_rhino"] is True
    assert launches[0][0] == [str(executable)]
    assert connection.requests == [
        ("open_dashboard", {"force": False, "prefer_chrome": True})
    ]


def test_missing_windows_rhino_returns_actionable_error(monkeypatch):
    connection = FakeConnection([BridgeUnavailable("offline")])
    monkeypatch.setattr(startup.os, "name", "nt")
    monkeypatch.setattr(startup, "rhino_process_running", lambda: False)
    monkeypatch.setattr(startup, "find_rhino_executable", lambda: None)

    result = startup.RhinoStartup(Settings(), connection).ensure_ready()

    assert result["ok"] is False
    assert result["state"] == "rhino_not_found"
    assert "RHINO_MCP_RHINO_EXE" in result["next_step"]


def test_find_rhino_executable_uses_explicit_override(monkeypatch, tmp_path):
    executable = Path(tmp_path) / "Custom" / "Rhino.exe"
    executable.parent.mkdir()
    executable.touch()
    monkeypatch.setenv("RHINO_MCP_RHINO_EXE", str(executable))

    assert startup.find_rhino_executable() == executable
