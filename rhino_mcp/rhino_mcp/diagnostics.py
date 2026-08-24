"""Human-readable diagnostics used by setup, doctor, status, and the Rhino panel."""

from __future__ import annotations

import importlib.util
import os
import platform
import shutil
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

from . import __version__
from .clients import CLIENTS, client_is_configured
from .config import Settings, config_path, load_settings
from .grasshopper import GrasshopperConnection
from .protocol import BridgeConnection, BridgeEndpoint, BridgeError
from .regulations import RegulationLibrary


@dataclass(slots=True)
class Check:
    name: str
    status: str
    message: str
    required: bool = False


def _native_plugin() -> Path | None:
    home = Path.home()
    if sys.platform == "darwin":
        root = (
            home / "Library" / "Application Support" / "McNeel" / "Rhinoceros" / "packages" / "8.0"
        )
    elif os.name == "nt":
        root = (
            Path(os.environ.get("APPDATA", home / "AppData" / "Roaming"))
            / "McNeel"
            / "Rhinoceros"
            / "packages"
            / "8.0"
        )
    else:
        return None
    if not root.exists():
        return None
    try:
        return next(root.rglob("RhinoMCP.rhp"), None)
    except OSError:
        return None


def run_doctor(settings: Settings | None = None) -> list[Check]:
    settings = settings or load_settings()
    frozen = bool(getattr(sys, "frozen", False))
    checks = [
        Check(
            "MCP runtime",
            "pass",
            f"bundled {__version__}" if frozen else f"Python {platform.python_version()}",
            True,
        ),
        Check(
            "MCP SDK",
            "pass" if importlib.util.find_spec("mcp") else "fail",
            "installed" if importlib.util.find_spec("mcp") else "package is missing",
            True,
        ),
        Check(
            "Configuration",
            "pass" if config_path().exists() else "fail",
            str(config_path()) if config_path().exists() else "run rhino-mcp setup",
            True,
        ),
    ]
    configured_clients = 0
    for client in CLIENTS:
        try:
            configured = client_is_configured(client)
        except Exception as exc:  # diagnostic commands should never crash
            configured = False
            message = str(exc)
        else:
            message = "configured" if configured else "not configured"
        configured_clients += int(configured)
        checks.append(Check(client.title(), "pass" if configured else "warn", message))
    checks.append(
        Check(
            "AI client",
            "pass" if configured_clients else "fail",
            f"{configured_clients} configured" if configured_clients else "run rhino-mcp setup",
            True,
        )
    )

    regulations = RegulationLibrary(settings)
    regulation_status = regulations.status()
    regulations.close()
    checks.append(
        Check(
            "Regulations",
            "pass" if regulation_status.get("ok") else "warn",
            (
                f"{regulation_status['indexed_documents']} documents / "
                f"{regulation_status['indexed_pages']} pages"
                if regulation_status.get("ok")
                else str(regulation_status.get("message", "not installed"))
            ),
            True,
        )
    )

    plugin = _native_plugin()
    checks.append(
        Check(
            "Rhino plug-in",
            "pass" if plugin else "fail",
            str(plugin) if plugin else "run or repair the Rhino MCP Windows installer",
            True,
        )
    )

    rhino = BridgeConnection(
        BridgeEndpoint(
            settings.host,
            settings.rhino_port,
            settings.connect_timeout,
            min(settings.request_timeout, 5),
        )
    )
    try:
        response = rhino.ping()
        version = response.get("data", {}).get("rhino_version", "connected")
        checks.append(Check("Rhino bridge", "pass", str(version)))
    except BridgeError as exc:
        checks.append(Check("Rhino bridge", "warn", str(exc)))
    finally:
        rhino.close()

    grasshopper = GrasshopperConnection(settings)
    try:
        response = grasshopper.health()
        checks.append(Check("Grasshopper", "pass", str(response.get("message", "available"))))
    except BridgeError as exc:
        checks.append(Check("Grasshopper", "warn", str(exc)))
    finally:
        grasshopper.close()

    launcher = shutil.which("rhino-mcp") or shutil.which("uvx") or sys.executable
    checks.append(Check("Server launcher", "pass", launcher, True))
    return checks


def checks_as_dict(checks: list[Check]) -> dict[str, Any]:
    return {
        "ok": not any(check.required and check.status == "fail" for check in checks),
        "checks": [asdict(check) for check in checks],
    }
