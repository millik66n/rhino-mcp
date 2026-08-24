"""Rhino MCP stdio server."""

from __future__ import annotations

import logging
import threading
import warnings
from contextlib import asynccontextmanager, suppress

from mcp.server.fastmcp import FastMCP
from pydantic_settings.exceptions import IncompleteFieldDefinitionWarning

from .config import Settings, load_settings
from .grasshopper import GrasshopperConnection
from .protocol import BridgeConnection, BridgeEndpoint, BridgeError
from .regulations import ARCHITECTURE_INSTRUCTIONS, RegulationTools
from .tools import GrasshopperTools, RhinoTools

logger = logging.getLogger("rhino_mcp")
warnings.filterwarnings("ignore", category=IncompleteFieldDefinitionWarning)


class Runtime:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.rhino = BridgeConnection(
            BridgeEndpoint(
                settings.host,
                settings.rhino_port,
                settings.connect_timeout,
                settings.request_timeout,
            )
        )
        self.grasshopper = GrasshopperConnection(settings)
        self._stop = threading.Event()
        self._monitor: threading.Thread | None = None

    def start(self) -> None:
        if self._monitor is not None:
            return
        self._monitor = threading.Thread(target=self._monitor_rhino, daemon=True)
        self._monitor.start()

    def _monitor_rhino(self) -> None:
        while not self._stop.is_set():
            with suppress(BridgeError):
                self.rhino.ping()
            self._stop.wait(5.0)

    def close(self) -> None:
        self._stop.set()
        self.rhino.close()
        self.grasshopper.close()
        if self._monitor is not None:
            self._monitor.join(timeout=1.0)


def create_app(settings: Settings | None = None) -> FastMCP:
    settings = settings or load_settings()
    runtime = Runtime(settings)
    regulation_tools: RegulationTools | None = None

    @asynccontextmanager
    async def lifespan(_):
        logger.info("Rhino MCP ready; profile=%s", settings.profile)
        runtime.start()
        try:
            yield {"runtime": runtime}
        finally:
            runtime.close()
            if regulation_tools is not None:
                regulation_tools.library.close()

    app = FastMCP(
        "Rhino MCP",
        instructions=(
            "Use Rhino MCP's safe, structured tools to inspect and modify Rhino and "
            "Grasshopper. "
            + ARCHITECTURE_INSTRUCTIONS
        ),
        lifespan=lifespan,
    )
    RhinoTools(app, settings, runtime.rhino)
    GrasshopperTools(app, settings, runtime.grasshopper)
    regulation_tools = RegulationTools(app, settings)

    @app.prompt()
    def rhino_workflow() -> str:
        """Safe, efficient workflow for editing a Rhino model."""
        return (
            "Check rhino_status, inspect get_scene_changes, and prefer batch_geometry. "
            "Use dry_run for large edits. Mutations create one Rhino undo checkpoint. "
            "Use the Developer profile only when high-level tools cannot express the task. "
            + ARCHITECTURE_INSTRUCTIONS
        )

    app._rhino_mcp_runtime = runtime
    return app


app = create_app()


def serve() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    app.run(transport="stdio")


def main() -> None:
    """Backward-compatible server entry point."""
    serve()


if __name__ == "__main__":
    serve()
