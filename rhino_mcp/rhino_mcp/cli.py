"""The no-editing command line experience for Rhino MCP."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import urllib.request
from pathlib import Path

from . import __version__
from .clients import (
    CLIENTS,
    configure_client,
    detect_clients,
    remove_client,
    server_spec,
)
from .config import VALID_PROFILES, config_path, load_settings, save_settings
from .diagnostics import checks_as_dict, run_doctor
from .regulations import RegulationLibrary, resolve_database_path

MARKS = {"pass": "PASS", "warn": "WAIT", "fail": "FAIL"}
RELEASE_API = "https://api.github.com/repos/millik66n/rhino-mcp/releases/latest"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="rhino-mcp",
        description="Install, configure, diagnose, and run Rhino MCP.",
    )
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    commands = parser.add_subparsers(dest="command")

    commands.add_parser("serve", help="Run the MCP server over stdio")

    setup = commands.add_parser("setup", help="Configure an AI client in one step")
    setup.add_argument("client", nargs="?", choices=CLIENTS)
    setup.add_argument("--all", action="store_true", help="Configure all installed clients")
    setup.add_argument("--profile", choices=VALID_PROFILES, default="basic")
    setup.add_argument("--non-interactive", action="store_true")

    config = commands.add_parser("config", help="Configure one client or local settings")
    config.add_argument("client", nargs="?", choices=CLIENTS)
    config.add_argument("--profile", choices=VALID_PROFILES)
    config.add_argument("--image-size", type=int)
    config.add_argument("--image-quality", type=int)
    config.add_argument("--regulations-db", type=Path)

    regulations = commands.add_parser(
        "regulations", help="Check or search the local architecture regulation library"
    )
    regulation_commands = regulations.add_subparsers(dest="regulations_command")
    regulation_commands.add_parser("status", help="Show regulation-library coverage")
    regulation_search = regulation_commands.add_parser("search", help="Search cited pages")
    regulation_search.add_argument("query")
    regulation_search.add_argument("--folder")
    regulation_search.add_argument("--limit", type=int, default=6)

    doctor = commands.add_parser("doctor", help="Run clear connection and setup checks")
    doctor.add_argument("--json", action="store_true", dest="as_json")

    status = commands.add_parser("status", help="Show the current configuration and connections")
    status.add_argument("--json", action="store_true", dest="as_json")

    commands.add_parser("update", help="Upgrade the Python package")

    uninstall = commands.add_parser("uninstall", help="Remove Rhino MCP from AI clients")
    uninstall.add_argument("--all", action="store_true", help="Also remove local settings")
    uninstall.add_argument("--yes", action="store_true", help="Skip confirmation")
    return parser


def _select_clients(args: argparse.Namespace) -> list[str]:
    if args.client:
        return [args.client]
    detected = detect_clients()
    if args.all:
        return detected or list(CLIENTS)
    if args.non_interactive or not sys.stdin.isatty():
        return detected[:1] or ["codex"]
    print("\nChoose your AI client:")
    options = detected + [client for client in CLIENTS if client not in detected]
    for index, client in enumerate(options, 1):
        suffix = " (detected)" if client in detected else ""
        print(f"  {index}. {client.title()}{suffix}")
    while True:
        choice = input("Client [1]: ").strip() or "1"
        if choice.isdigit() and 1 <= int(choice) <= len(options):
            return [options[int(choice) - 1]]
        print("Enter one of the numbers shown above.")


def command_setup(args: argparse.Namespace) -> int:
    settings = load_settings()
    settings.profile = args.profile
    targets = _select_clients(args)
    spec = server_spec()
    configured: list[str] = []
    print(f"\nRhino MCP {__version__} setup")
    for client in targets:
        path = configure_client(client, spec)
        configured.append(client)
        print(f"  PASS  {client.title()} configured ({path})")
    settings.configured_clients = sorted(set(settings.configured_clients or []) | set(configured))
    regulations = resolve_database_path(settings)
    if regulations.is_file():
        settings.regulations_db = str(regulations)
    saved = save_settings(settings)
    print(f"  PASS  {args.profile.title()} tool profile selected")
    print(f"  PASS  Settings saved ({saved})")
    if regulations.is_file():
        print(f"  PASS  Regulatory library ready ({regulations})")
    else:
        print("  WAIT  Regulatory library is not included in this build")
    print("\nNext: open Rhino. Rhino MCP starts automatically; then restart your AI client.")
    print("Check everything anytime with: rhino-mcp doctor")
    return 0


def command_config(args: argparse.Namespace) -> int:
    settings = load_settings()
    if args.client:
        path = configure_client(args.client)
        settings.configured_clients = sorted(set(settings.configured_clients or []) | {args.client})
        print(f"Configured {args.client.title()}: {path}")
    if args.profile:
        settings.profile = args.profile
    if args.image_size is not None:
        settings.image_max_size = args.image_size
    if args.image_quality is not None:
        settings.image_quality = args.image_quality
    if args.regulations_db is not None:
        if not args.regulations_db.is_file():
            raise ValueError(f"regulation database does not exist: {args.regulations_db}")
        settings.regulations_db = str(args.regulations_db.resolve())
    settings.__post_init__()
    path = save_settings(settings)
    print(f"Settings: {path}")
    print(f"Profile: {settings.profile}")
    return 0


def _print_checks(as_json: bool) -> int:
    report = checks_as_dict(run_doctor())
    if as_json:
        print(json.dumps(report, indent=2))
    else:
        print(f"Rhino MCP {__version__}\n")
        for check in report["checks"]:
            print(f"  {MARKS[check['status']]:<4}  {check['name']:<16} {check['message']}")
        if report["ok"]:
            print("\nCore setup is healthy. WAIT items become ready when their app is open.")
        else:
            print("\nOne or more required checks failed.")
    return 0 if report["ok"] else 1


def command_status(as_json: bool) -> int:
    settings = load_settings()
    if as_json:
        payload = checks_as_dict(run_doctor(settings))
        payload["settings"] = {
            "profile": settings.profile,
            "host": settings.host,
            "rhino_port": settings.rhino_port,
            "grasshopper_port": settings.grasshopper_port,
            "image_max_size": settings.image_max_size,
            "image_quality": settings.image_quality,
            "regulations_db": settings.regulations_db,
        }
        print(json.dumps(payload, indent=2))
        return 0
    print(f"Profile: {settings.profile}")
    print(f"Rhino bridge: {settings.host}:{settings.rhino_port}")
    print(f"Grasshopper bridge: {settings.host}:{settings.grasshopper_port}")
    print(f"Images: max {settings.image_max_size}px, quality {settings.image_quality}")
    regulations = RegulationLibrary(settings)
    regulation_status = regulations.status()
    regulations.close()
    print(
        "Regulations: "
        + (
            f"{regulation_status['indexed_documents']} documents / "
            f"{regulation_status['indexed_pages']} pages"
            if regulation_status.get("ok")
            else "not installed"
        )
    )
    print(f"Settings: {config_path()}")
    print()
    return _print_checks(False)


def command_regulations(args: argparse.Namespace) -> int:
    library = RegulationLibrary(load_settings())
    try:
        if args.regulations_command in {None, "status"}:
            payload = library.status()
        else:
            payload = library.search(args.query, folder=args.folder, limit=args.limit)
        print(json.dumps(payload, indent=2, ensure_ascii=False))
        return 0 if payload.get("ok") else 1
    finally:
        library.close()


def _latest_release_tag() -> str:
    tag = ""
    gh = shutil.which("gh")
    if gh:
        result = subprocess.run(
            [gh, "api", "repos/millik66n/rhino-mcp/releases/latest", "--jq", ".tag_name"],
            text=True,
            capture_output=True,
            check=False,
        )
        if result.returncode == 0:
            tag = result.stdout.strip()
    if not tag:
        request = urllib.request.Request(RELEASE_API, headers={"User-Agent": "rhino-mcp"})
        with urllib.request.urlopen(request, timeout=10) as response:
            tag = str(json.load(response)["tag_name"])
    if not re.fullmatch(r"v?\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?", tag):
        raise RuntimeError("Latest release returned an invalid version tag")
    return tag


def _latest_package_source() -> str:
    tag = _latest_release_tag()
    return f"git+https://github.com/millik66n/rhino-mcp.git@{tag}"


def command_update() -> int:
    if getattr(sys, "frozen", False):
        print(
            "This self-contained Windows installation never updates automatically.\n"
            "To change versions, download and run a different Rhino MCP installer."
        )
        return 0
    source = _latest_package_source()
    uv = shutil.which("uv")
    if uv:
        command = [uv, "tool", "install", "--force", source]
    else:
        command = [sys.executable, "-m", "pip", "install", "--upgrade", source]
    print("Running:", " ".join(command))
    result = subprocess.run(command, check=False)
    if result.returncode == 0:
        print("Python server updated. Install the matching Yak package or Windows installer.")
    return result.returncode


def command_uninstall(args: argparse.Namespace) -> int:
    if not args.yes and sys.stdin.isatty():
        answer = input("Remove Rhino MCP from Codex, Claude, and Cursor? [y/N] ").strip().lower()
        if answer not in {"y", "yes"}:
            print("Cancelled.")
            return 0
    for client in CLIENTS:
        path = remove_client(client)
        print(f"Removed {client.title()} entry from {path}")
    if args.all and config_path().exists():
        config_path().unlink()
        print(f"Removed settings {config_path()}")
    if getattr(sys, "frozen", False):
        print("The Windows uninstaller removes the Rhino and Grasshopper plug-ins.")
    else:
        print("Remove the Rhino plug-in with Yak or the Windows installer to finish.")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        if args.command == "serve":
            from .server import serve

            serve()
            return 0
        if args.command == "setup":
            return command_setup(args)
        if args.command == "config":
            return command_config(args)
        if args.command == "doctor":
            return _print_checks(args.as_json)
        if args.command == "status":
            return command_status(args.as_json)
        if args.command == "regulations":
            return command_regulations(args)
        if args.command == "update":
            return command_update()
        if args.command == "uninstall":
            return command_uninstall(args)
        parser.print_help()
        return 0
    except (OSError, RuntimeError, ValueError) as exc:
        print(f"rhino-mcp: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
