import json
import sys

from rhino_mcp import clients


def test_frozen_windows_bundle_uses_itself_as_the_server(monkeypatch, tmp_path):
    executable = tmp_path / "rhino-mcp.exe"
    monkeypatch.setattr(sys, "frozen", True, raising=False)
    monkeypatch.setattr(sys, "executable", str(executable))

    assert clients.server_spec() == clients.ServerSpec(str(executable.resolve()), ["serve"])


def test_json_client_configuration_preserves_existing_keys(tmp_path, monkeypatch):
    path = tmp_path / "mcp.json"
    path.write_text(json.dumps({"theme": "dark", "mcpServers": {"other": {"command": "x"}}}))
    monkeypatch.setattr(clients, "client_config_path", lambda _: path)
    spec = clients.ServerSpec("/usr/local/bin/uvx", ["--from", "rhino-mcp", "rhino-mcp", "serve"])

    clients.configure_client("cursor", spec)

    value = json.loads(path.read_text())
    assert value["theme"] == "dark"
    assert "other" in value["mcpServers"]
    assert value["mcpServers"]["rhino-mcp"] == spec.as_json()


def test_remove_json_client_only_removes_rhino(tmp_path, monkeypatch):
    path = tmp_path / "mcp.json"
    path.write_text(json.dumps({"mcpServers": {"rhino-mcp": {}, "keep": {}}}))
    monkeypatch.setattr(clients, "client_config_path", lambda _: path)

    clients.remove_client("claude")

    assert json.loads(path.read_text())["mcpServers"] == {"keep": {}}


def test_codex_uses_official_cli(tmp_path, monkeypatch):
    calls = []
    monkeypatch.setattr(clients, "_codex_executable", lambda: "/fake/codex")
    monkeypatch.setattr(clients, "client_config_path", lambda _: tmp_path / "config.toml")

    class Result:
        returncode = 0
        stdout = ""
        stderr = ""

    def run(command, **kwargs):
        calls.append(command)
        return Result()

    monkeypatch.setattr(clients.subprocess, "run", run)
    clients.configure_client("codex", clients.ServerSpec("uvx", ["rhino-mcp", "serve"]))

    assert calls[-1] == [
        "/fake/codex",
        "mcp",
        "add",
        "rhino-mcp",
        "--",
        "uvx",
        "rhino-mcp",
        "serve",
    ]


def test_codex_finds_windows_desktop_app_cli(tmp_path, monkeypatch):
    local_appdata = tmp_path / "AppData" / "Local"
    desktop_cli = local_appdata / "OpenAI" / "Codex" / "bin" / "version-id" / "codex.exe"
    desktop_cli.parent.mkdir(parents=True)
    desktop_cli.touch()

    monkeypatch.setenv("LOCALAPPDATA", str(local_appdata))
    monkeypatch.setattr(clients.sys, "platform", "win32")
    monkeypatch.setattr(clients.shutil, "which", lambda _: None)

    assert clients._codex_executable() == str(desktop_cli)
