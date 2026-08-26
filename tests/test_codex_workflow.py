from rhino_mcp import codex_workflow


def configure_test_paths(monkeypatch, tmp_path):
    codex = tmp_path / ".codex"
    skill = tmp_path / ".agents" / "skills" / "rhino-mcp"
    monkeypatch.setattr(codex_workflow, "codex_home", lambda: codex)
    monkeypatch.setattr(codex_workflow, "skill_dir", lambda: skill)
    return codex, skill


def test_codex_workflow_installs_required_prefix_and_preserves_guidance(monkeypatch, tmp_path):
    codex, skill = configure_test_paths(monkeypatch, tmp_path)
    codex.mkdir()
    guidance = codex / "AGENTS.md"
    guidance.write_text("# Existing guidance\n\nKeep this.\n")

    paths = codex_workflow.configure_codex_workflow()
    codex_workflow.configure_codex_workflow()

    guidance_text = guidance.read_text()
    assert guidance_text.startswith("# Existing guidance")
    assert guidance_text.count(codex_workflow.GUIDANCE_START) == 1
    assert "require the\n  exact `/RhinoMCP` prefix" in guidance_text
    assert "ensure_rhino_ready" in guidance_text
    assert "$rhino-mcp" in (skill / "SKILL.md").read_text()
    assert "/RhinoMCP $ARGUMENTS" in paths["prompt"].read_text()
    assert codex_workflow.codex_workflow_is_configured() is True


def test_codex_workflow_uses_existing_override_and_uninstalls_only_its_files(
    monkeypatch, tmp_path
):
    codex, skill = configure_test_paths(monkeypatch, tmp_path)
    codex.mkdir()
    override = codex / "AGENTS.override.md"
    override.write_text("Keep override.\n")

    codex_workflow.configure_codex_workflow()
    codex_workflow.remove_codex_workflow()

    assert override.read_text() == "Keep override.\n"
    assert not (skill / "SKILL.md").exists()
    assert not (codex / "prompts" / "RhinoMCP.md").exists()
    assert codex_workflow.codex_workflow_is_configured() is False
