"""Install the scoped Codex routing files used by Rhino MCP."""

from __future__ import annotations

import os
import tempfile
from contextlib import suppress
from importlib import resources
from pathlib import Path

GUIDANCE_START = "<!-- RHINO_MCP_GUIDANCE_START -->"
GUIDANCE_END = "<!-- RHINO_MCP_GUIDANCE_END -->"
GUIDANCE = f"""{GUIDANCE_START}
## Rhino MCP prompt routing

- For a request that uses Rhino or Grasshopper through Rhino MCP, require the
  exact `/RhinoMCP` prefix. If that prefix is missing, do not call Rhino MCP
  tools; ask the user to resend the request beginning with `/RhinoMCP`.
- `$rhino-mcp` and `/prompts:RhinoMCP` are installed shortcuts and count as an
  explicitly prefixed Rhino MCP request.
- For an explicitly routed request, call `ensure_rhino_ready` first with
  `open_dashboard=true`. Continue only after it reports that the bridge is
  connected.
{GUIDANCE_END}
"""


def codex_home() -> Path:
    return Path(os.environ.get("CODEX_HOME", Path.home() / ".codex"))


def skill_dir() -> Path:
    return Path.home() / ".agents" / "skills" / "rhino-mcp"


def guidance_path() -> Path:
    home = codex_home()
    override = home / "AGENTS.override.md"
    return override if override.is_file() and override.stat().st_size else home / "AGENTS.md"


def workflow_paths() -> dict[str, Path]:
    return {
        "guidance": guidance_path(),
        "skill": skill_dir() / "SKILL.md",
        "skill_metadata": skill_dir() / "agents" / "openai.yaml",
        "prompt": codex_home() / "prompts" / "RhinoMCP.md",
    }


def _resource_text(*parts: str) -> str:
    target = resources.files("rhino_mcp").joinpath("data", *parts)
    return target.read_text(encoding="utf-8")


def _atomic_write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        "w", encoding="utf-8", dir=path.parent, delete=False
    ) as handle:
        handle.write(text)
        temporary = Path(handle.name)
    os.replace(temporary, path)


def _without_guidance(text: str) -> str:
    start = text.find(GUIDANCE_START)
    if start < 0:
        return text
    end = text.find(GUIDANCE_END, start)
    if end < 0:
        return text
    end += len(GUIDANCE_END)
    return (text[:start].rstrip() + "\n\n" + text[end:].lstrip()).strip()


def configure_codex_workflow() -> dict[str, Path]:
    """Install the skill, compatibility prompt, and scoped routing guidance."""
    paths = workflow_paths()
    _atomic_write(
        paths["skill"],
        _resource_text("codex-skill", "rhino-mcp", "SKILL.md"),
    )
    _atomic_write(
        paths["skill_metadata"],
        _resource_text("codex-skill", "rhino-mcp", "agents", "openai.yaml"),
    )
    _atomic_write(paths["prompt"], _resource_text("codex-prompts", "RhinoMCP.md"))

    target = paths["guidance"]
    existing = target.read_text(encoding="utf-8") if target.exists() else ""
    base = _without_guidance(existing)
    combined = f"{base.rstrip()}\n\n{GUIDANCE}" if base.strip() else GUIDANCE
    _atomic_write(target, combined)
    return paths


def remove_codex_workflow() -> dict[str, Path]:
    """Remove only Rhino MCP-owned files and the marked guidance block."""
    paths = workflow_paths()
    for key in ("skill", "skill_metadata", "prompt"):
        path = paths[key]
        if path.exists():
            path.unlink()

    for directory in (paths["skill_metadata"].parent, paths["skill"].parent):
        with suppress(OSError):
            directory.rmdir()

    for target in (codex_home() / "AGENTS.md", codex_home() / "AGENTS.override.md"):
        if not target.exists():
            continue
        original = target.read_text(encoding="utf-8")
        cleaned = _without_guidance(original)
        if cleaned == original:
            continue
        if cleaned:
            _atomic_write(target, cleaned.rstrip() + "\n")
        else:
            target.unlink()
    return paths


def codex_workflow_is_configured() -> bool:
    paths = workflow_paths()
    required = (paths["skill"], paths["skill_metadata"], paths["prompt"], paths["guidance"])
    if not all(path.is_file() for path in required):
        return False
    try:
        return GUIDANCE_START in paths["guidance"].read_text(encoding="utf-8")
    except OSError:
        return False
