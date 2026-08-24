from pathlib import Path

ROOT = Path(__file__).parents[1]


def test_windows_installer_has_one_download_and_no_python_bootstrap():
    installer = (ROOT / "installer" / "RhinoMCP.iss").read_text()
    bootstrap = (ROOT / "installer" / "Install-RhinoMCP.ps1").read_text()
    build = (ROOT / "scripts" / "build-windows-installer.ps1").read_text()

    assert "RhinoMCP-Windows-Setup-{#AppVersion}" in installer
    assert "rhino-mcp.exe" in bootstrap
    assert "PyInstaller" in build
    assert '--collect-data "rhino_mcp"' in build
    assert "astral.sh" not in bootstrap
    assert "uv tool" not in bootstrap
    assert ".whl" not in bootstrap


def test_windows_installer_configures_each_supported_client_and_grasshopper():
    installer = (ROOT / "installer" / "RhinoMCP.iss").read_text()

    for client in ("Codex", "Claude", "Cursor"):
        assert f"ClientPage.Add('{client}')" in installer
    assert '-Profile "grasshopper"' in installer
    assert "AfterInstall: InstallRuntime" in installer
