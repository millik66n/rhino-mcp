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
    bootstrap = (ROOT / "installer" / "Install-RhinoMCP.ps1").read_text()

    for client in ("Codex", "Claude", "Cursor"):
        assert f"ClientPage.Add('{client}')" in installer
    assert '-Profile "grasshopper"' in installer
    assert "AfterInstall: InstallRuntime" in installer
    assert "/RhinoMCP routing installed" in bootstrap


def test_windows_installer_replaces_old_rhino_mcp_versions_only():
    installer = (ROOT / "installer" / "RhinoMCP.iss").read_text()
    bootstrap = (ROOT / "installer" / "Install-RhinoMCP.ps1").read_text()
    uninstaller = (ROOT / "installer" / "Uninstall-RhinoMCP.ps1").read_text()
    helpers = (ROOT / "installer" / "RhinoMcpPackage.ps1").read_text()
    plugin = (ROOT / "native" / "RhinoMCP.Plugin" / "RhinoMcpPlugin.cs").read_text()
    diagnostics = (ROOT / "rhino_mcp" / "rhino_mcp" / "diagnostics.py").read_text()
    workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text()

    assert 'Source: "RhinoMcpPackage.ps1"' in installer
    assert '[InstallDelete]' in installer
    assert 'Name: "{app}\\server"' in installer
    assert 'Name: "{app}\\payload"' in installer
    assert '-Version "' in installer
    assert '& $yakPath uninstall "Rhino-MCP-Easy"' in bootstrap
    assert "Remove-RhinoMcpPackageVersions" in bootstrap
    assert "Assert-RhinoMcpPackageInstalled -Version $Version" in bootstrap
    assert "Register-RhinoMcpPlugin -Version $Version" in bootstrap
    assert "Unregister-RhinoMcpPlugin" in uninstaller
    assert "Remove-RhinoMcpPackageVersions" in uninstaller
    assert '"Rhino-MCP-Easy"' in helpers
    assert '"0E59A34D-7906-45DC-B8A1-B1D8219A841E"' in helpers
    assert 'Guid("0E59A34D-7906-45DC-B8A1-B1D8219A841E")' in plugin
    assert 'RHINO_MCP_PLUGIN_ID = "0E59A34D-7906-45DC-B8A1-B1D8219A841E"' in diagnostics
    assert '"Rhino registration"' in diagnostics
    assert '"Plug-ins"' in helpers
    assert 'Name "FileName"' in helpers
    assert 'Name "LoadMode"' in helpers
    assert "Get-RhinoMcpRuntime" in helpers
    assert "Unblock-File" in helpers
    assert "Clear-RhinoMcpPluginLoadCache" in helpers
    assert "expectedParent.Equals" in helpers
    assert 'Join-Path $packageRoot "manifest.txt"' in helpers
    assert "Old Rhino MCP package versions remain" in helpers
    assert "test-windows-package-upgrade.ps1" in workflow
