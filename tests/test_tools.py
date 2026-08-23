from rhino_mcp.config import Settings
from rhino_mcp.protocol import BridgeUnavailable
from rhino_mcp.tools import RhinoTools


class App:
    def tool(self):
        return lambda value: value


class Connection:
    def __init__(self):
        self.calls = []

    def request(self, command, params=None, retry=True):
        self.calls.append((command, params, retry))
        return {"status": "ok", "data": {"command": command, "value": len(self.calls)}}


def test_reads_are_unwrapped_and_cached():
    connection = Connection()
    tools = RhinoTools(App(), Settings(cache_ttl=60), connection)
    assert tools.get_scene_summary()["command"] == "get_scene_info"
    assert tools.get_scene_summary()["value"] == 1
    assert len(connection.calls) == 1


def test_mutations_are_not_retried_and_clear_cache():
    connection = Connection()
    tools = RhinoTools(App(), Settings(cache_ttl=60), connection)
    tools.get_scene_summary()
    result = tools.create_geometry("point", {"point": [0, 0, 0]})
    assert result["command"] == "create_geometry"
    assert connection.calls[-1][2] is False
    tools.get_scene_summary()
    assert len(connection.calls) == 3


def test_connection_errors_are_actionable():
    class Offline(Connection):
        def request(self, command, params=None, retry=True):
            raise BridgeUnavailable("Open Rhino and start Rhino MCP.")

    result = RhinoTools(App(), Settings(), Offline()).rhino_status()
    assert result["ok"] is False
    assert "Open Rhino" in result["error"]["message"]
    assert "Restart" in result["error"]["next_step"]
