from rhino_mcp.config import Settings
from rhino_mcp.server import create_app


def names(profile):
    app = create_app(Settings(profile=profile))
    return set(app._tool_manager._tools)


def test_basic_profile_hides_grasshopper_and_code_execution():
    tools = names("basic")
    assert "create_geometry" in tools
    assert "execute_rhino_code" not in tools
    assert "get_grasshopper_context" not in tools


def test_grasshopper_profile_adds_graph_tools_but_not_code_execution():
    tools = names("grasshopper")
    assert "get_grasshopper_context" in tools
    assert "execute_rhino_code" not in tools
    assert "execute_grasshopper_code" not in tools


def test_developer_profile_explicitly_enables_code_execution():
    tools = names("developer")
    assert "execute_rhino_code" in tools
    assert "execute_grasshopper_code" in tools
