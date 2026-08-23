import re
from pathlib import Path

from rhino_mcp import __version__


def test_python_and_yak_versions_stay_in_sync():
    root = Path(__file__).parents[1]
    manifest = (root / "native" / "package" / "manifest.yml").read_text()
    match = re.search(r"^version:\s*(\S+)$", manifest, re.MULTILINE)
    assert match
    assert match.group(1) == __version__
