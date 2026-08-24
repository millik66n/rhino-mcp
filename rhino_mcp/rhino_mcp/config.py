"""Persistent user configuration for Rhino MCP."""

from __future__ import annotations

import json
import os
import tempfile
from dataclasses import asdict, dataclass, fields
from pathlib import Path
from typing import Any

VALID_PROFILES = ("basic", "grasshopper", "developer")


def config_dir() -> Path:
    override = os.environ.get("RHINO_MCP_HOME")
    return Path(override).expanduser() if override else Path.home() / ".rhino-mcp"


def config_path() -> Path:
    override = os.environ.get("RHINO_MCP_CONFIG")
    return Path(override).expanduser() if override else config_dir() / "config.json"


@dataclass(slots=True)
class Settings:
    profile: str = "basic"
    host: str = "127.0.0.1"
    rhino_port: int = 9876
    grasshopper_port: int = 9999
    connect_timeout: float = 2.0
    request_timeout: float = 45.0
    image_max_size: int = 1024
    image_quality: int = 80
    page_size: int = 100
    cache_ttl: float = 0.75
    regulations_db: str | None = None
    configured_clients: list[str] | None = None

    def __post_init__(self) -> None:
        self.profile = self.profile.lower()
        if self.profile not in VALID_PROFILES:
            raise ValueError("profile must be basic, grasshopper, or developer")
        if not (1 <= self.rhino_port <= 65535 and 1 <= self.grasshopper_port <= 65535):
            raise ValueError("ports must be between 1 and 65535")
        self.image_quality = max(20, min(95, int(self.image_quality)))
        self.image_max_size = max(256, min(4096, int(self.image_max_size)))
        self.page_size = max(1, min(500, int(self.page_size)))
        if self.configured_clients is None:
            self.configured_clients = []


def load_settings(path: Path | None = None) -> Settings:
    target = path or config_path()
    if not target.exists():
        return Settings()
    raw = json.loads(target.read_text(encoding="utf-8"))
    allowed = {field.name for field in fields(Settings)}
    return Settings(**{key: value for key, value in raw.items() if key in allowed})


def save_settings(settings: Settings, path: Path | None = None) -> Path:
    target = path or config_path()
    target.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(asdict(settings), indent=2, sort_keys=True) + "\n"
    with tempfile.NamedTemporaryFile(
        "w", encoding="utf-8", dir=target.parent, delete=False
    ) as handle:
        handle.write(payload)
        temporary = Path(handle.name)
    os.replace(temporary, target)
    return target


def update_settings(**changes: Any) -> Settings:
    settings = load_settings()
    for key, value in changes.items():
        if not hasattr(settings, key):
            raise KeyError(key)
        setattr(settings, key, value)
    settings.__post_init__()
    save_settings(settings)
    return settings
