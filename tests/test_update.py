from rhino_mcp import cli


def test_latest_release_uses_authenticated_github_cli(monkeypatch):
    class Result:
        returncode = 0
        stdout = "v9.9.9\n"

    monkeypatch.setattr(cli.shutil, "which", lambda name: "/usr/bin/gh" if name == "gh" else None)
    monkeypatch.setattr(cli.subprocess, "run", lambda *_, **__: Result())
    assert cli._latest_release_tag() == "v9.9.9"


def test_update_installs_latest_release(monkeypatch):
    source = "git+https://github.com/millik66n/rhino-mcp.git@v9.9.9"
    commands = []

    class Result:
        returncode = 0

    monkeypatch.setattr(cli, "_latest_package_source", lambda: source)
    monkeypatch.setattr(cli.shutil, "which", lambda name: "/usr/bin/uv" if name == "uv" else None)

    def run(command, **_):
        commands.append(command)
        return Result()

    monkeypatch.setattr(cli.subprocess, "run", run)

    assert cli.command_update() == 0
    assert commands == [["/usr/bin/uv", "tool", "install", "--force", source]]


def test_frozen_windows_bundle_does_not_self_update(monkeypatch, capsys):
    monkeypatch.setattr(cli.sys, "frozen", True, raising=False)
    monkeypatch.setattr(
        cli,
        "_latest_package_source",
        lambda: (_ for _ in ()).throw(AssertionError("must not contact GitHub")),
    )

    assert cli.command_update() == 0
    assert "never updates automatically" in capsys.readouterr().out
