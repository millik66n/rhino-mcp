"""Automatic MCP client configuration for Codex, Claude, and Cursor."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from . import __version__

SERVER_NAME = "rhino-mcp"
CLIENTS = ("codex", "claude", "cursor")
PINNED_SOURCE = f"git+https://github.com/millik66n/rhino-mcp.git@v{__version__}"


@dataclass(frozen=True, slots=True)
class ServerSpec:
    command: str
    args: list[str]

    def as_json(self) -> dict[str, Any]:
        return {"command": self.command, "args": self.args}


def server_spec() -> ServerSpec:
    """Choose a stable launcher, preferring uvx isolation when available."""
    override = os.environ.get("RHINO_MCP_COMMAND")
    if override:
        return ServerSpec(override, ["serve"])
    # The Windows installer bundles the complete Python application as a
    # PyInstaller executable.  Reusing that executable keeps the configured MCP
    # entry self-contained and avoids Python, uv, Git, and network dependencies.
    if getattr(sys, "frozen", False):
        return ServerSpec(str(Path(sys.executable).resolve()), ["serve"])
    executable = shutil.which("rhino-mcp")
    resolved = str(Path(executable).resolve()) if executable else ""
    if executable and not any(part in resolved for part in ("/.cache/uv/", "/tmp/")):
        return ServerSpec(str(Path(executable).absolute()), ["serve"])
    uvx = shutil.which("uvx")
    if uvx:
        return ServerSpec(uvx, ["--from", PINNED_SOURCE, "rhino-mcp", "serve"])
    return ServerSpec(sys.executable, ["-m", "rhino_mcp", "serve"])


def client_config_path(client: str) -> Path:
    client = normalize_client(client)
    home = Path.home()
    if client == "codex":
        return Path(os.environ.get("CODEX_HOME", home / ".codex")) / "config.toml"
    if client == "cursor":
        return home / ".cursor" / "mcp.json"
    if sys.platform == "darwin":
        return home / "Library" / "Application Support" / "Claude" / "claude_desktop_config.json"
    if os.name == "nt":
        appdata = Path(os.environ.get("APPDATA", home / "AppData" / "Roaming"))
        return appdata / "Claude" / "claude_desktop_config.json"
    return home / ".config" / "Claude" / "claude_desktop_config.json"


def normalize_client(client: str) -> str:
    value = client.strip().lower()
    aliases = {"chatgpt": "codex", "claude-desktop": "claude", "claude desktop": "claude"}
    value = aliases.get(value, value)
    if value not in CLIENTS:
        raise ValueError("client must be codex, claude, or cursor")
    return value


def detect_clients() -> list[str]:
    detected: list[str] = []
    if shutil.which("codex") or Path("/Applications/ChatGPT.app").exists():
        detected.append("codex")
    if shutil.which("claude") or Path("/Applications/Claude.app").exists():
        detected.append("claude")
    if shutil.which("cursor") or Path("/Applications/Cursor.app").exists():
        detected.append("cursor")
    return detected


def _load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"Cannot update {path}: it is not valid JSON") from exc
    if not isinstance(value, dict):
        raise RuntimeError(f"Cannot update {path}: its root must be a JSON object")
    return value


def _save_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        "w", encoding="utf-8", dir=path.parent, delete=False
    ) as handle:
        json.dump(value, handle, indent=2, sort_keys=True)
        handle.write("\n")
        temporary = Path(handle.name)
    os.replace(temporary, path)


def _codex_executable() -> str | None:
    executable = shutil.which("codex")
    if executable:
        return executable

    if sys.platform == "win32":
        # The Codex Windows desktop app keeps its CLI in a versioned private
        # directory that Explorer-launched installers do not inherit on PATH.
        # Search that stable app-owned root so desktop-only installations work
        # without asking the user to install a second Codex CLI.
        local_appdata = os.environ.get("LOCALAPPDATA")
        if local_appdata:
            bin_root = Path(local_appdata) / "OpenAI" / "Codex" / "bin"
            try:
                candidates = [path for path in bin_root.rglob("codex.exe") if path.is_file()]
            except OSError:
                candidates = []
            if candidates:
                try:
                    newest = max(candidates, key=lambda path: path.stat().st_mtime_ns)
                except OSError:
                    newest = candidates[0]
                return str(newest)

    macos_app_cli = Path("/Applications/ChatGPT.app/Contents/Resources/codex")
    return str(macos_app_cli) if macos_app_cli.exists() else None


def configure_client(client: str, spec: ServerSpec | None = None) -> Path:
    client = normalize_client(client)
    spec = spec or server_spec()
    path = client_config_path(client)
    if client == "codex":
        executable = _codex_executable()
        if not executable:
            raise RuntimeError("Codex is not installed or its command is not on PATH")
        subprocess.run(
            [executable, "mcp", "remove", SERVER_NAME],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        result = subprocess.run(
            [executable, "mcp", "add", SERVER_NAME, "--", spec.command, *spec.args],
            text=True,
            capture_output=True,
            check=False,
        )
        if result.returncode:
            raise RuntimeError(
                result.stderr.strip() or result.stdout.strip() or "Codex setup failed"
            )
        return path

    value = _load_json(path)
    servers = value.setdefault("mcpServers", {})
    if not isinstance(servers, dict):
        raise RuntimeError(f"Cannot update {path}: mcpServers must be an object")
    servers[SERVER_NAME] = spec.as_json()
    _save_json(path, value)
    return path


def remove_client(client: str) -> Path:
    client = normalize_client(client)
    path = client_config_path(client)
    if client == "codex":
        executable = _codex_executable()
        if executable:
            subprocess.run(
                [executable, "mcp", "remove", SERVER_NAME],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            )
        return path
    value = _load_json(path)
    servers = value.get("mcpServers")
    if isinstance(servers, dict) and SERVER_NAME in servers:
        del servers[SERVER_NAME]
        _save_json(path, value)
    return path


def client_is_configured(client: str) -> bool:
    client = normalize_client(client)
    path = client_config_path(client)
    if client == "codex":
        executable = _codex_executable()
        if not executable:
            return False
        result = subprocess.run(
            [executable, "mcp", "get", SERVER_NAME],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        return result.returncode == 0
    try:
        value = _load_json(path)
    except RuntimeError:
        return False
    return isinstance(value.get("mcpServers"), dict) and SERVER_NAME in value["mcpServers"]
