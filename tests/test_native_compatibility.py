from pathlib import Path

ROOT = Path(__file__).parents[1]
RHINO_8_BASELINE = "8.0.23304.9001"


def test_native_plugins_target_both_rhino_8_runtimes_from_the_8_0_sdk():
    for project in ("RhinoMCP.Plugin", "RhinoMCP.Grasshopper"):
        csproj = next((ROOT / "native" / project).glob("*.csproj")).read_text()

        assert "<TargetFrameworks>net48;net7.0</TargetFrameworks>" in csproj
        assert f'Version="{RHINO_8_BASELINE}"' in csproj
        assert "8.34.26223.11001" not in csproj


def test_yak_and_windows_installer_include_both_runtime_builds():
    shell_build = (ROOT / "scripts" / "build-yak.sh").read_text()
    windows_build = (ROOT / "scripts" / "build-windows-installer.ps1").read_text()

    for script in (shell_build, windows_build):
        assert "net48" in script
        assert "net7.0" in script
        assert "RhinoMCP.rhp" in script
        assert "RhinoMCP.Grasshopper.gha" in script

    assert 'net48/"*.dll' in shell_build
    assert "net48\\*.dll" in windows_build
    assert "rh8_0-win" in shell_build
    assert "rh8_0-win" in windows_build


def test_native_code_avoids_runtime_apis_missing_from_net48():
    bridge = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoBridgeService.cs").read_text()
    native_sources = "\n".join(
        path.read_text() for path in (ROOT / "native").rglob("*.cs")
    )

    assert "AcceptTcpClientAsync()" in bridge
    assert "await using (NetworkStream" not in bridge
    assert ".AsMemory(" not in bridge
    assert "Math.Clamp(" not in native_sources
