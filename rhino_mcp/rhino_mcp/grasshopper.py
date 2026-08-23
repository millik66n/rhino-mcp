"""Persistent HTTP client for the automatically installed Grasshopper bridge."""

from __future__ import annotations

from typing import Any

import requests

from .config import Settings
from .protocol import BridgeError, BridgeUnavailable


class GrasshopperConnection:
    def __init__(self, settings: Settings):
        self.base_url = f"http://{settings.host}:{settings.grasshopper_port}"
        self.timeout = settings.request_timeout
        self.session = requests.Session()

    def close(self) -> None:
        self.session.close()

    def request(self, command: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        try:
            response = self.session.post(
                f"{self.base_url}/command",
                json={"type": command, "params": params or {}},
                timeout=self.timeout,
            )
            response.raise_for_status()
            value = response.json()
        except requests.RequestException as exc:
            raise BridgeUnavailable(
                "Open Grasshopper once; Rhino MCP installs and starts its bridge automatically."
            ) from exc
        except ValueError as exc:
            raise BridgeError("Grasshopper bridge returned invalid JSON") from exc
        if not isinstance(value, dict):
            raise BridgeError("Grasshopper bridge response must be an object")
        if value.get("status") == "error":
            raise BridgeError(str(value.get("message", "Unknown Grasshopper error")))
        return value

    def health(self) -> dict[str, Any]:
        try:
            response = self.session.get(f"{self.base_url}/health", timeout=2)
            response.raise_for_status()
            value = response.json()
            return value if isinstance(value, dict) else {"status": "ok"}
        except (requests.RequestException, ValueError) as exc:
            raise BridgeUnavailable("Grasshopper is not open or its bridge is not ready.") from exc
