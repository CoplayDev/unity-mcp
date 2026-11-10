"""License inputs, container continuity, and failure handling without real secrets."""
import base64
import io
import os
from pathlib import Path
import shutil
import subprocess
import sys

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import ci_unity_license as lic
from local_harness import DockerLauncher


@pytest.mark.parametrize("xml", [
    b'<License><Signature>signed</Signature></License>',
    b'<License><Signature xmlns="urn:signature" Algorithm="test">signed</Signature></License>',
    b'<License xmlns:s="urn:signature"><s:Signature Algorithm="test"/></License>',
])
@pytest.mark.parametrize("encoded", [False, True])
def test_raw_and_base64_signed_candidates(xml, encoded):
    value = base64.b64encode(xml).decode() if encoded else xml.decode()
    assert lic.decode_ulf(value) == xml


@pytest.mark.parametrize("value", ["", "not XML", "<License/>", "<License><Signature>"])
def test_invalid_candidates_are_rejected(value):
    with pytest.raises(ValueError, match="not signed XML"):
        lic.decode_ulf(value)


def mock_downloads(monkeypatch):
    urls = []
    def fetch(url, timeout):
        urls.append(url)
        return io.BytesIO(b"# pinned activation helper\n")
    monkeypatch.setattr(lic.urllib.request, "urlopen", fetch)
    return urls


def test_personal_credentials_do_not_require_serial_and_reuse_identity(tmp_path, monkeypatch):
    urls = mock_downloads(monkeypatch)
    directory = tmp_path / "unity-activation"
    credentials = {"UNITY_EMAIL": "test@example.invalid", "UNITY_PASSWORD": "fake-password"}
    lic.prepare(directory, credentials)
    identity = (tmp_path / "unity-machine-id").read_text()
    assert len(identity.strip()) == 32
    lic.prepare(directory, credentials)
    assert (tmp_path / "unity-machine-id").read_text() == identity
    assert not (directory / "input.ulf").exists()
    assert len(urls) == 8
    assert all(f"/{lic.GAME_CI_COMMIT}/" in url for url in urls)
    assert {p.name for p in (directory / "steps").iterdir()} == set(lic.GAME_CI_SCRIPTS)


def test_invalid_ulf_requires_fallback_credentials(tmp_path, monkeypatch, capsys):
    urls = mock_downloads(monkeypatch)
    directory = tmp_path / "unity-activation"
    with pytest.raises(ValueError, match="not signed XML"):
        lic.prepare(directory, {"UNITY_LICENSE": "private-invalid-value"})
    assert not urls
    lic.prepare(directory, {"UNITY_LICENSE": "private-invalid-value",
                            "UNITY_EMAIL": "test@example.invalid", "UNITY_PASSWORD": "secret"})
    assert "trying the configured Unity account" in capsys.readouterr().out
    assert not (directory / "input.ulf").exists()


def test_missing_or_partial_credentials_fail_before_download(tmp_path, monkeypatch):
    urls = mock_downloads(monkeypatch)
    for environment in ({}, {"UNITY_EMAIL": "test@example.invalid"}, {"UNITY_PASSWORD": "secret"}):
        with pytest.raises(ValueError, match="requires"):
            lic.prepare(tmp_path / "unity-activation", environment)
    assert not urls


def test_staged_license_is_passed_as_a_file_and_stale_input_is_removed(tmp_path, monkeypatch):
    mock_downloads(monkeypatch)
    directory = tmp_path / "unity-activation"
    lic.prepare(directory, {"UNITY_LICENSE": "<License><Signature>fake</Signature></License>"})
    args = lic.docker_args(directory, "test-image", "activate")
    assert "UNITY_LICENSE_FILE=/activation/input.ulf" in args
    assert "UNITY_LICENSE=" in args
    assert not any("<Signature>" in argument for argument in args)
    lic.prepare(directory, {"UNITY_EMAIL": "test@example.invalid", "UNITY_PASSWORD": "secret"})
    assert "UNITY_LICENSE_FILE=" in lic.docker_args(directory, "test-image", "activate")


def test_all_container_paths_share_identity_and_license_state(tmp_path):
    directory = tmp_path / "unity-activation"
    directory.mkdir()
    activate = lic.docker_args(directory, "test-image", "activate")
    returned = lic.docker_args(directory, "test-image", "return")
    bridge = DockerLauncher.docker_run_argv("test-image", tmp_path, tmp_path / "project",
                                           tmp_path / "status", "-", [], runner_temp=str(tmp_path))
    bridge = [argument.replace("\\", "/") for argument in bridge]
    activate = [argument.replace("\\", "/") for argument in activate]
    returned = [argument.replace("\\", "/") for argument in returned]
    for suffix, target in (("unity-machine-id", "/etc/machine-id:ro"),
                           ("unity-config", "/root/.config/unity3d"),
                           ("unity-local", "/root/.local/share/unity3d"),
                           ("unity-cache", "/root/.cache/unity3d")):
        mount = f"{tmp_path / suffix}:{target}".replace("\\", "/")
        assert mount in activate and mount in returned and mount in bridge
    local = DockerLauncher.docker_run_argv("test-image", tmp_path, tmp_path / "project",
                                          tmp_path / "status", "-", [])
    assert not any("unity-machine-id" in item for item in local)


def test_failed_activation_reports_failure_without_raw_output(tmp_path, monkeypatch, capsys):
    monkeypatch.setattr(lic.subprocess, "run", lambda *a, **kw: subprocess.CompletedProcess(
        a, 3, "No seat available. fake-password test@example.invalid", "private serial"))
    assert lic.run_license(tmp_path, "test-image", "activate") == 1
    output = capsys.readouterr().out
    assert "no Unity license seat" in output
    assert "fake-password" not in output and "test@example.invalid" not in output
    assert "private serial" not in output
    assert (tmp_path / "activation-attempted").exists()


def test_return_does_not_start_docker_without_activation_attempt(tmp_path, monkeypatch):
    def unexpected(*args, **kwargs):
        pytest.fail("Docker should not be called before activation")
    monkeypatch.setattr(lic.subprocess, "run", unexpected)
    assert lic.run_license(tmp_path, "test-image", "return") == 0


@pytest.mark.parametrize("source,expected", [("export GAME_CI_ACTIVATED_VIA=personal\n", 0),
                                              ("exit 7\n", 7), ("return 7\n", 7)])
def test_shell_propagates_failure_and_preserves_fallback_for_return(tmp_path, source, expected):
    bash = "C:/Program Files/Git/bin/bash.exe" if os.name == "nt" else shutil.which("bash")
    if not bash or not Path(bash).exists():
        pytest.skip("bash unavailable")
    steps = tmp_path / "steps"
    steps.mkdir()
    (steps / "activate.sh").write_text(source, encoding="utf-8")
    (steps / "return_license.sh").write_text(
        'test "$GAME_CI_ACTIVATED_VIA" = personal || exit 8\nRETURN_EXIT_CODE=0\n', encoding="utf-8")
    environment = dict(os.environ, STEPS_DIR=steps.as_posix(), ACTIVATE_LICENSE_PATH=tmp_path.as_posix())
    result = subprocess.run([bash, "-c", lic.CONTAINER_SCRIPT, "test", "activate"],
                            env=environment, capture_output=True, text=True)
    assert result.returncode == expected, result.stderr
    if not expected:
        assert (tmp_path / "activated-via").read_text() == "personal"
        result = subprocess.run([bash, "-c", lic.CONTAINER_SCRIPT, "test", "return"],
                                env=environment, capture_output=True, text=True)
        assert result.returncode == 0, result.stderr
