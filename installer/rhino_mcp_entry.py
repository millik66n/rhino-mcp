"""Entry point for the self-contained Windows executable."""

from __future__ import annotations

from multiprocessing import freeze_support

from rhino_mcp.cli import main


if __name__ == "__main__":
    freeze_support()
    raise SystemExit(main())
