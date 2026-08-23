"""Exercise the packaged MCP executable over its real stdio transport."""

from __future__ import annotations

import asyncio
import os
import sys
import tempfile
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


async def exercise(executable: Path) -> None:
    with tempfile.TemporaryDirectory() as settings_home:
        parameters = StdioServerParameters(
            command=str(executable),
            args=["serve"],
            env={**os.environ, "RHINO_MCP_HOME": settings_home},
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
    print(f"Bundled MCP smoke test passed: {executable}")


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: smoke-bundled-server.py <rhino-mcp executable>", file=sys.stderr)
        return 2
    executable = Path(sys.argv[1]).resolve()
    if not executable.is_file():
        print(f"executable not found: {executable}", file=sys.stderr)
        return 2
    asyncio.run(exercise(executable))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
