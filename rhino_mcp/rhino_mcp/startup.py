"""Cold-start Rhino and wait for the local bridge."""

from __future__ import annotations

import os
import subprocess
import time
from pathlib import Path
from typing import Any

from .config import Settings
from .protocol import BridgeConnection, BridgeError


def _data(response: dict[str, Any]) -> dict[str, Any]:
    value = response.get("data")
    return value if isinstance(value, dict) else response


def find_rhino_executable() -> Path | None:
    """Return the Rhino 8 executable used by the one-click Windows installer."""
    override = os.environ.get("RHINO_MCP_RHINO_EXE")
    if override:
        candidate = Path(override).expanduser()
        return candidate if candidate.is_file() else None

    roots: list[str] = []
    for name in ("ProgramW6432", "ProgramFiles"):
        value = os.environ.get(name)
        if value and value not in roots:
            roots.append(value)
    for root in roots:
        candidate = Path(root) / "Rhino 8" / "System" / "Rhino.exe"
        if candidate.is_file():
            return candidate
    return None


def rhino_process_running() -> bool:
    if os.name != "nt":
        return False
    try:
        result = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq Rhino.exe", "/NH"],
            text=True,
            capture_output=True,
            check=False,
            timeout=5,
        )
    except (OSError, subprocess.SubprocessError):
        return False
    return result.returncode == 0 and "rhino.exe" in result.stdout.lower()


class RhinoStartup:
    def __init__(self, settings: Settings, connection: BridgeConnection):
        self.settings = settings
        self.connection = connection

    def _health(self) -> dict[str, Any] | None:
        try:
            return _data(self.connection.ping())
        except BridgeError:
            return None

    def _open_dashboard(self, *, force: bool) -> dict[str, Any]:
        return _data(
            self.connection.request(
                "open_dashboard",
                {"force": force, "prefer_chrome": True},
            )
        )

    def ensure_ready(
        self,
        *,
        open_dashboard: bool = True,
        wait_seconds: int = 60,
    ) -> dict[str, Any]:
        """Start Rhino when necessary, wait for its bridge, and show status."""
        timeout = max(10, min(120, int(wait_seconds)))
        health = self._health()
        launched = False
        already_running = health is not None or rhino_process_running()

        if health is None:
            if os.name != "nt":
                return {
                    "ok": False,
                    "state": "unsupported_platform",
                    "mcp_server": "connected",
                    "bridge": "offline",
                    "message": "Automatic Rhino startup is currently available on Windows.",
                    "next_step": "Open Rhino, then run the /RhinoMCP request again.",
                }

            if not already_running:
                executable = find_rhino_executable()
                if executable is None:
                    return {
                        "ok": False,
                        "state": "rhino_not_found",
                        "mcp_server": "connected",
                        "bridge": "offline",
                        "message": "Rhino 8 could not be found on this computer.",
                        "next_step": (
                            "Install Rhino 8 in its standard location or set "
                            "RHINO_MCP_RHINO_EXE to Rhino.exe, then try again."
                        ),
                    }
                try:
                    process = subprocess.Popen([str(executable)], close_fds=True)
                    process.poll()
                    launched = True
                except OSError as exc:
                    return {
                        "ok": False,
                        "state": "rhino_launch_failed",
                        "mcp_server": "connected",
                        "bridge": "offline",
                        "message": f"Rhino 8 could not be started: {exc}",
                        "next_step": f"Open {executable} manually and try again.",
                    }

            deadline = time.monotonic() + timeout
            while time.monotonic() < deadline:
                time.sleep(0.5)
                health = self._health()
                if health is not None:
                    break

        if health is None:
            state = "bridge_start_timeout" if launched else "bridge_not_ready"
            return {
                "ok": False,
                "state": state,
                "mcp_server": "connected",
                "rhino": "starting" if launched else "running",
                "bridge": "offline",
                "launched_rhino": launched,
                "message": f"Rhino MCP did not connect within {timeout} seconds.",
                "next_step": (
                    "Wait for Rhino to finish opening. If its connection strip says "
                    "Bridge stopped, "
                    "run RhinoMCPRestart, then retry the /RhinoMCP request."
                ),
            }

        dashboard: dict[str, Any] = {"opened": False, "browser": "not requested"}
        if open_dashboard:
            try:
                dashboard = self._open_dashboard(force=not launched)
            except BridgeError as exc:
                return {
                    "ok": False,
                    "state": "dashboard_failed",
                    "mcp_server": "connected",
                    "rhino": "running",
                    "bridge": "connected",
                    "launched_rhino": launched,
                    "message": f"Rhino connected, but the status page could not open: {exc}",
                    "next_step": "Run RhinoMCPDashboard in Rhino to display the status page.",
                }

        return {
            "ok": True,
            "state": "ready",
            "mcp_server": "connected",
            "rhino": "running",
            "bridge": "connected",
            "launched_rhino": launched,
            "dashboard": dashboard,
            "rhino_version": health.get("rhino_version", "connected"),
            "document": health.get("document", "Untitled"),
            "message": (
                "Rhino MCP is connected. The Chrome status page is displayed; "
                "continue with the requested Rhino task."
            ),
        }
