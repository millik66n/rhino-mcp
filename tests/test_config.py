import json

import pytest
from rhino_mcp.config import Settings, load_settings, save_settings


def test_settings_round_trip(tmp_path):
    path = tmp_path / "config.json"
    expected = Settings(
        profile="grasshopper",
        dashboard_port=12077,
        image_quality=72,
        configured_clients=["codex"],
    )
    save_settings(expected, path)
    actual = load_settings(path)
    assert actual == expected
    assert json.loads(path.read_text())["profile"] == "grasshopper"


def test_settings_clamp_response_sizes():
    settings = Settings(image_quality=200, image_max_size=99, page_size=5000)
    assert settings.image_quality == 95
    assert settings.image_max_size == 256
    assert settings.page_size == 500


def test_invalid_profile_is_rejected():
    with pytest.raises(ValueError, match="profile"):
        Settings(profile="unsafe")


@pytest.mark.parametrize("field", ["rhino_port", "grasshopper_port", "dashboard_port"])
def test_invalid_ports_are_rejected(field):
    with pytest.raises(ValueError, match="ports"):
        Settings(**{field: 70000})
