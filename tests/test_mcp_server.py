import asyncio
import os
import sys

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def test_stdio_server_lists_safe_tools_and_returns_structured_errors(tmp_path):
    async def exercise():
        parameters = StdioServerParameters(
            command=sys.executable,
            args=["-m", "rhino_mcp", "serve"],
            env={**os.environ, "RHINO_MCP_HOME": str(tmp_path)},
        )
        async with (
            stdio_client(parameters) as (read, write),
            ClientSession(read, write) as session,
        ):
            initialized = await session.initialize()
            listed = await session.list_tools()
            names = {tool.name for tool in listed.tools}
            assert initialized.serverInfo.name == "Rhino MCP"
            assert "create_geometry" in names
            assert "execute_rhino_code" not in names
            result = await session.call_tool("rhino_status", {})
            assert result.isError is False
            assert result.structuredContent["ok"] is False
            assert "Open Rhino" in result.structuredContent["error"]["message"]

    asyncio.run(exercise())
