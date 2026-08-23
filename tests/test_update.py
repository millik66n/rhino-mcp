from rhino_mcp import cli


def test_update_installs_latest_release_wheel(monkeypatch):
    wheel = "https://example.test/rhino_mcp-9.9.9-py3-none-any.whl"
    commands = []

    class Result:
        returncode = 0

    monkeypatch.setattr(cli, "_latest_wheel_url", lambda: wheel)
    monkeypatch.setattr(cli.shutil, "which", lambda name: "/usr/bin/uv" if name == "uv" else None)

    def run(command, **_):
        commands.append(command)
        return Result()

    monkeypatch.setattr(cli.subprocess, "run", run)

    assert cli.command_update() == 0
    assert commands == [["/usr/bin/uv", "tool", "install", "--force", wheel]]
