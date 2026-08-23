"""Safe, compact, structured MCP tools."""

from __future__ import annotations

import base64
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

from mcp.server.fastmcp import Image

from .config import Settings
from .grasshopper import GrasshopperConnection
from .protocol import BridgeConnection, BridgeError


@dataclass(slots=True)
class _CacheEntry:
    created: float
    value: dict[str, Any]


class ResponseCache:
    def __init__(self, ttl: float):
        self.ttl = ttl
        self._values: dict[str, _CacheEntry] = {}
        self._lock = threading.Lock()

    def get(self, key: str, loader: Callable[[], dict[str, Any]]) -> dict[str, Any]:
        now = time.monotonic()
        with self._lock:
            entry = self._values.get(key)
            if entry and now - entry.created <= self.ttl:
                return entry.value
        value = loader()
        with self._lock:
            self._values[key] = _CacheEntry(now, value)
        return value

    def clear(self) -> None:
        with self._lock:
            self._values.clear()


def _error(exc: BridgeError) -> dict[str, Any]:
    return {
        "ok": False,
        "error": {
            "code": exc.__class__.__name__,
            "message": str(exc),
            "next_step": "Open Rhino, open the Rhino MCP panel, and click Restart.",
        },
    }


def _data(response: dict[str, Any]) -> dict[str, Any]:
    value = response.get("data")
    return value if isinstance(value, dict) else response


class RhinoTools:
    def __init__(self, app: Any, settings: Settings, connection: BridgeConnection):
        self.app = app
        self.settings = settings
        self.connection = connection
        self.cache = ResponseCache(settings.cache_ttl)
        self._register()

    def _register(self) -> None:
        for method in (
            self.rhino_status,
            self.get_scene_summary,
            self.list_layers,
            self.list_objects,
            self.get_scene_changes,
            self.create_geometry,
            self.modify_objects,
            self.delete_objects,
            self.organize_layers,
            self.batch_geometry,
            self.test_connection,
            self.capture_viewport,
        ):
            self.app.tool()(method)
        if self.settings.profile == "developer":
            self.app.tool()(self.execute_rhino_code)

    def _read(self, command: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        try:
            return _data(self.connection.request(command, params))
        except BridgeError as exc:
            return _error(exc)

    def _mutate(self, command: str, params: dict[str, Any]) -> dict[str, Any]:
        try:
            result = _data(self.connection.request(command, params, retry=False))
            if not params.get("dry_run", False):
                self.cache.clear()
            return result
        except BridgeError as exc:
            return _error(exc)

    def rhino_status(self) -> dict[str, Any]:
        """Check Rhino, bridge, document, and protocol status."""
        return self._read("health")

    def get_scene_summary(self, force_refresh: bool = False) -> dict[str, Any]:
        """Return a compact scene summary."""
        if force_refresh:
            self.cache.clear()
        return self.cache.get("summary", lambda: self._read("get_scene_info"))

    def list_layers(self, force_refresh: bool = False) -> dict[str, Any]:
        """List scene layers and object counts."""
        if force_refresh:
            self.cache.clear()
        return self.cache.get("layers", lambda: self._read("get_layers"))

    def list_objects(
        self,
        page: int = 1,
        page_size: int | None = None,
        layer: str | None = None,
        object_type: str | None = None,
        fields: list[str] | None = None,
    ) -> dict[str, Any]:
        """List a filtered page of objects with selected fields."""
        size = max(1, min(500, page_size or self.settings.page_size))
        return self._read(
            "list_objects",
            {
                "page": max(1, page),
                "page_size": size,
                "layer": layer,
                "object_type": object_type,
                "fields": fields,
            },
        )

    def get_scene_changes(
        self, since_version: int = 0, page: int = 1, page_size: int | None = None
    ) -> dict[str, Any]:
        """Return only objects changed since a prior scene version."""
        return self._read(
            "get_scene_changes",
            {
                "since_version": max(0, since_version),
                "page": max(1, page),
                "page_size": max(1, min(500, page_size or self.settings.page_size)),
            },
        )

    def create_geometry(
        self,
        kind: str,
        geometry: dict[str, Any],
        name: str | None = None,
        layer: str | None = None,
        dry_run: bool = False,
    ) -> dict[str, Any]:
        """Create a point, line, box, sphere, cylinder, or polyline safely."""
        return self._mutate(
            "create_geometry",
            {"kind": kind, "geometry": geometry, "name": name, "layer": layer, "dry_run": dry_run},
        )

    def modify_objects(
        self, object_ids: list[str], transform: dict[str, Any], dry_run: bool = False
    ) -> dict[str, Any]:
        """Move, rotate, scale, rename, or relayer named objects."""
        return self._mutate(
            "modify_objects",
            {"object_ids": object_ids, "transform": transform, "dry_run": dry_run},
        )

    def delete_objects(self, object_ids: list[str], dry_run: bool = False) -> dict[str, Any]:
        """Delete explicit object IDs with an automatic Rhino undo checkpoint."""
        return self._mutate("delete_objects", {"object_ids": object_ids, "dry_run": dry_run})

    def organize_layers(
        self, actions: list[dict[str, Any]], dry_run: bool = False
    ) -> dict[str, Any]:
        """Create, rename, recolor, or remove layers in one transaction."""
        return self._mutate("organize_layers", {"actions": actions, "dry_run": dry_run})

    def batch_geometry(
        self, operations: list[dict[str, Any]], dry_run: bool = False
    ) -> dict[str, Any]:
        """Apply many geometry operations with one undo record and one redraw."""
        return self._mutate("batch_geometry", {"operations": operations, "dry_run": dry_run})

    def test_connection(self, cleanup: bool = True) -> dict[str, Any]:
        """Create a small test cube, verify it, and remove it automatically."""
        return self._mutate("test_connection", {"cleanup": cleanup})

    def capture_viewport(
        self,
        max_size: int | None = None,
        quality: int | None = None,
        image_format: str = "jpeg",
    ) -> Image:
        """Capture a compressed viewport image at a configurable size."""
        size = max(256, min(4096, max_size or self.settings.image_max_size))
        image_quality = max(20, min(95, quality or self.settings.image_quality))
        result = self.connection.request(
            "capture_viewport",
            {"max_size": size, "quality": image_quality, "format": image_format},
        )
        payload = _data(result)
        source = payload.get("source", payload)
        encoded = source.get("data") if isinstance(source, dict) else None
        if not encoded:
            raise BridgeError("Rhino did not return a viewport image")
        return Image(data=base64.b64decode(encoded), format=source.get("format", image_format))

    def execute_rhino_code(self, code: str) -> dict[str, Any]:
        """Developer profile only: execute Rhino Python code."""
        return self._mutate("execute_code", {"code": code})


class GrasshopperTools:
    def __init__(self, app: Any, settings: Settings, connection: GrasshopperConnection):
        self.app = app
        self.settings = settings
        self.connection = connection
        self._register()

    def _register(self) -> None:
        if self.settings.profile not in {"grasshopper", "developer"}:
            return
        for method in (
            self.grasshopper_status,
            self.get_grasshopper_context,
            self.get_grasshopper_objects,
            self.get_grasshopper_selected,
            self.expire_grasshopper_objects,
        ):
            self.app.tool()(method)
        if self.settings.profile == "developer":
            self.app.tool()(self.execute_grasshopper_code)

    def _request(self, command: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        try:
            return _data(self.connection.request(command, params))
        except BridgeError as exc:
            return _error(exc)

    def grasshopper_status(self) -> dict[str, Any]:
        """Check whether Grasshopper and its bridge are ready."""
        try:
            return self.connection.health()
        except BridgeError as exc:
            return _error(exc)

    def get_grasshopper_context(
        self, simplified: bool = True, page: int = 1, page_size: int | None = None
    ) -> dict[str, Any]:
        """Return a paginated Grasshopper definition; simplified by default."""
        return self._request(
            "get_context",
            {
                "simplified": simplified,
                "page": max(1, page),
                "page_size": max(1, min(500, page_size or self.settings.page_size)),
            },
        )

    def get_grasshopper_objects(
        self, object_ids: list[str], context_depth: int = 0
    ) -> dict[str, Any]:
        """Get specific Grasshopper objects and nearby connections."""
        return self._request(
            "get_objects", {"guids": object_ids, "context_depth": max(0, min(3, context_depth))}
        )

    def get_grasshopper_selected(self, simplified: bool = True) -> dict[str, Any]:
        """Get currently selected Grasshopper components."""
        return self._request("get_selected", {"simplified": simplified})

    def expire_grasshopper_objects(self, object_ids: list[str]) -> dict[str, Any]:
        """Recompute explicit Grasshopper objects."""
        return self._request("expire_objects", {"guids": object_ids})

    def execute_grasshopper_code(self, code: str) -> dict[str, Any]:
        """Developer profile only: execute code in Grasshopper."""
        return self._request("execute_code", {"code": code})
