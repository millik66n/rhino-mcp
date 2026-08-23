import re
from pathlib import Path

from rhino_mcp import __version__


def test_python_and_yak_versions_stay_in_sync():
    root = Path(__file__).parents[1]
    manifest = (root / "native" / "package" / "manifest.yml").read_text()
    match = re.search(r"^version:\s*(\S+)$", manifest, re.MULTILINE)
    assert match
    assert match.group(1) == __version__

    pyproject = (root / "pyproject.toml").read_text()
    assert re.search(r'^version = "' + re.escape(__version__) + r'"$', pyproject, re.MULTILINE)

    for project in ("RhinoMCP.Plugin", "RhinoMCP.Grasshopper"):
        csproj = next((root / "native" / project).glob("*.csproj")).read_text()
        assert f"<Version>{__version__}</Version>" in csproj

    installer = (root / "installer" / "RhinoMCP.iss").read_text()
    assert f'#define AppVersion "{__version__}"' in installer
