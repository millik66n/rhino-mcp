# Rhino MCP

The low-friction Rhino and Grasshopper bridge for Codex, Claude, and Cursor.

The target experience is deliberately short:

1. Install **Rhino MCP Easy** from Rhino Package Manager.
2. Run `rhino-mcp setup` and choose Codex, Claude, or Cursor.
3. Open Rhino and start prompting.

No repository clone, Conda environment, Rhino Python script, Grasshopper file,
Python-path change, or hand-edited MCP configuration is part of normal setup.

> The source and release artifacts are ready. The Package Manager and PyPI names
> become publicly searchable after their one-time registry publisher setup.
> Until then, use the `.yak` and wheel attached to the GitHub release.

## Install

### 1. Rhino plug-in

In Rhino 8, open **PackageManager**, search for **Rhino MCP Easy**, and install it.
Restart Rhino once. The plug-in and Grasshopper add-on then start automatically.

For a release file, install the attached `rhino-mcp-easy-*.yak` package with Yak.

### 2. AI client

Run setup in an isolated environment (nothing is added to your active Python):

```sh
uvx rhino-mcp setup
```

If you want the shorter maintenance commands shown below, install the CLI as a
standalone tool with `uv tool install rhino-mcp`; it is still isolated from your
Python projects.

Before the PyPI listing is enabled, install the tagged package directly without
cloning:

```sh
uvx --from "git+https://github.com/millik66n/rhino-mcp.git@v0.2.2" rhino-mcp setup
```

Setup detects installed clients, lets you choose one, writes its MCP entry safely,
and defaults to the restricted **Basic** tool profile. You can also configure a
client directly:

```sh
rhino-mcp config codex
rhino-mcp config claude
rhino-mcp config cursor
```

Restart the selected AI client, open Rhino, and prompt. Codex users can run `/mcp`
to confirm that `rhino-mcp` is enabled.

## What appears in Rhino

Run the `RhinoMCP` command once to show or hide the dockable **Rhino MCP** panel.
It fits into Rhino's normal panel/header strip and shows:

- MCP server connected or waiting
- Rhino bridge connected or stopped
- Grasshopper available or not running
- selected AI client and tool profile
- Rhino and Grasshopper ports
- recent logs
- **Restart**, **Create test cube**, and **Copy doctor command** buttons

The connection test creates a one-unit cube, verifies it, and removes it. If
cleanup is disabled, the test cube is left in the document.

## Commands

```text
rhino-mcp setup [codex|claude|cursor]
rhino-mcp doctor [--json]
rhino-mcp status [--json]
rhino-mcp update
rhino-mcp uninstall [--all]
rhino-mcp config [codex|claude|cursor]
rhino-mcp config --profile basic|grasshopper|developer
```

`doctor` reports pass/fail/wait states for the package, settings, every AI client,
Rhino, Grasshopper, and the server launcher. Rhino and Grasshopper being closed are
shown as `WAIT`, with the exact next action, rather than as socket traces.

## Tool profiles

### Basic (default)

The safe everyday set:

- `rhino_status`
- `get_scene_summary`
- `list_layers`
- `list_objects`
- `get_scene_changes`
- `create_geometry`
- `modify_objects`
- `delete_objects`
- `organize_layers`
- `batch_geometry`
- `test_connection`
- `capture_viewport`

### Grasshopper

Adds paginated definition context, selected/object inspection, and recompute tools.
The bundled `.gha` starts its bridge automatically when Grasshopper opens; there is
no `.gh` definition or Python component to load.

### Developer

Explicitly adds arbitrary Rhino and Grasshopper Python tools. They are absent from
the Basic and Grasshopper tool schemas, so an AI client cannot discover or call
them accidentally. Safe high-level operations are still preferred.

## Safe editing behavior

- Every modifying request gets a named Rhino undo record.
- A batch performs all operations in one transaction and redraws once.
- Geometry and layer tools accept `dry_run: true` for validation and preview.
- Deletes require explicit Rhino object IDs.
- Read results are structured objects, not JSON encoded inside strings.
- Object and Grasshopper lists are filtered and paginated.
- `get_scene_changes` sends only additions, edits, and deletions since a supplied
  scene version.

Example prompt:

```text
Create a 10 × 8 × 3 box on a layer named Massing. Dry-run it first, then create it
and show me a compressed viewport capture.
```

## Reliability and performance

- 4-byte length-prefixed messages replace the old giant receive buffer.
- Requests are read in bounded 64 KiB chunks with a 64 MiB hard limit.
- Rhino TCP and Grasshopper HTTP connections are kept alive and reused.
- A failed safe read reconnects once automatically.
- Mutations are never retried blindly, preventing duplicate geometry.
- Short-lived scene metadata is cached and invalidated after edits.
- Scene changes and large documents are paginated instead of repeatedly returned
  in full.
- Grasshopper context is simplified by default.
- View captures default to compressed JPEG with configurable size and quality.
- Socket work and image encoding run away from Rhino's UI thread; only Rhino API
  calls are marshalled onto it.
- Tool descriptions and normal responses are intentionally compact to reduce model
  token usage.

Change image defaults with:

```sh
rhino-mcp config --image-size 1280 --image-quality 82
```

## Developer build

Python package:

```sh
python -m venv .venv
.venv/bin/pip install -e ".[dev]"
.venv/bin/pytest -q
python -m build
```

Rhino 8 plug-in and Package Manager archive:

```sh
./scripts/build-yak.sh Release
```

The script builds `RhinoMCP.rhp`, `RhinoMCP.Grasshopper.gha`, and a `.yak` under
`dist/`. CI builds and checks both native projects plus the wheel and source
distribution. Tagged releases attach all three installable artifacts.

## Architecture

```text
Codex / Claude / Cursor
        │ MCP stdio
        ▼
rhino-mcp Python package
        │ framed, persistent localhost TCP
        ▼
RhinoMCP.rhp ── Rhino document + dockable panel
        │
        └── RhinoMCP.Grasshopper.gha (persistent localhost HTTP)
```

Both bridges bind only to `127.0.0.1`; they are not exposed to the network.

## License

MIT. This repository is standalone and has no upstream Git remote.
