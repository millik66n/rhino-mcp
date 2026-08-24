# Rhino MCP

The low-friction Rhino and Grasshopper bridge for Codex, Claude, and Cursor.

The Windows experience is deliberately short:

1. Download and run **RhinoMCP-Windows-Setup**.
2. Choose Codex, Claude, or Cursor.
3. Open Rhino, open Grasshopper, and start prompting.

No Rhino Package Manager, repository clone, Python installation, `uv`, Conda
environment, Rhino Python script, Grasshopper file, Python-path change, terminal
command, or hand-edited MCP configuration is part of normal setup.

## Install

Requires Rhino 8.0 or newer and the AI client you want to use. Rhino 8.1 is
supported without updating Rhino. Close Rhino, download the
single `RhinoMCP-Windows-Setup-*.exe` file from the
[latest release](https://github.com/millik66n/rhino-mcp/releases/latest), and
double-click it. The installer asks for the AI client and then automatically:

- installs the Rhino and Grasshopper bridges using Rhino's bundled installer;
- installs a self-contained MCP server with its complete runtime;
- writes the Codex, Claude, or Cursor MCP entry safely;
- selects the safe Grasshopper tool profile;
- runs the doctor checks; and
- adds a normal entry to Windows **Installed apps** for clean removal.

The installer does not need Rhino to be online, does not download dependencies,
and Rhino MCP never updates itself.
Installing a different version always requires deliberately running another
installer.

## Architecture regulation checks

Rhino MCP includes a compact, offline search index built from the supplied
[Google Drive regulation library](https://drive.google.com/drive/folders/13y5jvSC_KyE5Hm0N9fdVqXXnCXFp5FLz).
The original files are preserved outside this public source repository; the bundled
index stores searchable page text, source metadata, and links back to the originals.
It is a fixed snapshot and never syncs or updates itself.
The snapshot contains 289 source files, with 272 searchable documents and 7,896
indexed pages. Azerbaijani, Russian, and English OCR recovered 553 scanned PDF pages
and 66 image/TIFF sheets without extraction errors.

For architecture, building, accessibility, fire-safety, structural, sanitary,
energy, drainage, shelter, site-planning, or related Grasshopper requests, every MCP
client receives an always-on workflow instruction to:

1. establish the jurisdiction, occupancy/project type, design stage, and constraints;
2. search the regulatory library before proposing regulated dimensions or editing the
   model;
3. cite the exact document title, Drive source ID, and page for each requirement;
4. separate verified source requirements from recommendations and inference; and
5. flag missing evidence, conflicts, uncertain applicability, and the need for a
   licensed local review.

Document text is treated as untrusted reference data, not as executable instructions.
The AI must not invent code values or describe its review as a permit, approval, or
legal compliance certificate.

The always-available regulation tools are:

- `regulation_library_status`
- `search_regulations`
- `get_regulation_page`
- `architecture_regulation_checklist`

The search expands common English architecture terms into Azerbaijani and Russian so
the model can find the multilingual source material. Check the installed snapshot or
try a search with:

```text
rhino-mcp regulations status
rhino-mcp regulations search "fire evacuation stairs"
```

The source collection and its effective legal status have not been independently
certified. A responsible architect or engineer must confirm jurisdiction,
applicability, amendments, conflicts, and current requirements before construction.

For managed or silent deployment, the same download supports:

```powershell
.\RhinoMCP-Windows-Setup-0.4.0.exe /CLIENT=codex /SILENT
```

Valid client values are `codex`, `claude`, and `cursor`. Restart the selected AI
client after setup. Codex users can run `/mcp` to confirm that `rhino-mcp` is
enabled.

## What appears in Rhino

The dockable **Rhino MCP** panel opens automatically every time Rhino launches.
It remains visible until the user hides it, stays hidden for the rest of that
session, and appears again on the next Rhino launch. Run the `RhinoMCP` command
at any time to show or hide it manually.

The panel fits into Rhino's normal panel/header strip. Its headline shows
**Connected — ready to prompt Rhino**, **Rhino MCP is running — waiting for the
AI client**, or an actionable stopped/not-configured message. It also shows:

- MCP server connected or waiting
- Rhino bridge connected or stopped
- Grasshopper available or not running
- regulatory library loaded or not installed
- selected AI client and tool profile
- Rhino and Grasshopper ports
- recent logs
- **Restart**, **Create test cube**, and **Copy doctor command** buttons

The installer includes both Rhino 8 runtime builds. Rhino automatically uses
`net7.0` in its normal runtime or `net48` if Rhino is configured for the legacy
.NET Framework runtime; the user does not need to choose or configure this.

The connection test creates a one-unit cube, verifies it, and removes it. If
cleanup is disabled, the test cube is left in the document.

## Advanced commands

The Rhino panel's **Copy doctor command** button copies the correct full path for
installer users. Source and Python-package installations can use:

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
`dist/`.

Complete Windows installer (run in PowerShell on Windows):

```powershell
python -m pip install -e ".[dev]" "pyinstaller==6.22.2"
choco install innosetup --yes
.\scripts\build-windows-installer.ps1
```

CI compiles and smoke-tests the bundled Windows executable and the single-file
installer. Tagged releases attach the installer, its SHA-256 checksum, and the
manual developer artifacts.

## Architecture

```text
Codex / Claude / Cursor
        │ MCP stdio
        ▼
bundled rhino-mcp.exe
        ├── offline regulation search index
        │
        │ framed, persistent localhost TCP
        ▼
RhinoMCP.rhp ── Rhino document + dockable panel
        │
        └── RhinoMCP.Grasshopper.gha (persistent localhost HTTP)
```

Both bridges bind only to `127.0.0.1`; they are not exposed to the network.

## License

MIT. This repository is standalone and has no upstream Git remote.
