"""Exercise the CI gate with real result files and runner outcomes."""
from pathlib import Path
import os
import subprocess
import sys

import pytest


GATE = Path(__file__).resolve().parents[1] / "check_unity_test_results.py"
PASSING = '<test-run result="Passed" total="2" passed="1" failed="0" skipped="1"><test-case result="Passed"/><test-case result="Skipped"/></test-run>'


def run_gate(tmp_path, xml, outcome="success"):
    results = tmp_path / "editmode-results.xml"
    if xml is not None:
        results.write_text(xml, encoding="utf-8")
    return subprocess.run(
        [sys.executable, str(GATE), str(results), "--runner-outcome", outcome],
        capture_output=True, text=True, encoding="utf-8",
        env={**os.environ, "PYTHONIOENCODING": "utf-8"},
    )


def test_successful_runner_and_completed_results_pass(tmp_path):
    result = run_gate(tmp_path, PASSING)
    assert result.returncode == 0
    assert "1 passed" in result.stdout


@pytest.mark.parametrize("outcome", ["failure", "cancelled", "skipped", ""])
def test_passing_xml_cannot_hide_runner_failure(tmp_path, outcome):
    result = run_gate(tmp_path, PASSING, outcome)
    assert result.returncode == 1
    assert "runner did not succeed" in result.stdout


@pytest.mark.parametrize("xml", [
    None,
    "not XML",
    '<coverage total="3" passed="3" failed="0"/>',
    '<test-run result="Passed"/>',
    '<test-run result="Passed" total="invalid" passed="1" failed="0"/>',
    '<test-run result="Passed" total="1" passed="-1" failed="0"/>',
    '<test-run result="Passed" total="1" passed="2" failed="0"/>',
])
def test_missing_or_invalid_results_fail(tmp_path, xml):
    result = run_gate(tmp_path, xml)
    assert result.returncode == 1
    assert "Cannot validate" in result.stdout


@pytest.mark.parametrize("xml", [
    '<test-run result="Failed" total="1" passed="1" failed="0"/>',
    '<test-run result="Passed" total="1" passed="0" failed="1"/>',
    '<test-run result="Cancelled" total="1" passed="1" failed="0"/>',
    '<test-run total="1" passed="1" failed="0"/>',
    '<test-run result="Passed" total="1" passed="1" failed="0"><test-case result="Failed"/></test-run>',
])
def test_suite_or_case_failure_is_not_a_pass(tmp_path, xml):
    result = run_gate(tmp_path, xml)
    assert result.returncode == 1
    assert "unsuccessful test run" in result.stdout


@pytest.mark.parametrize("total", [0, 3])
def test_empty_or_all_skipped_runs_fail(tmp_path, total):
    result = run_gate(tmp_path, f'<test-run result="Passed" total="{total}" passed="0" failed="0"/>')
    assert result.returncode == 1
    assert "did not execute" in result.stdout


def test_failure_details_cannot_inject_workflow_commands(tmp_path):
    xml = '''<test-run result="Failed" total="1" passed="0" failed="1">
      <test-case result="Failed" fullname="Suite:Name,Percent%">
        <failure><message>First line
::warning::injected</message><stack-trace>trace
::error::injected</stack-trace></failure>
      </test-case></test-run>'''
    result = run_gate(tmp_path, xml)
    assert result.returncode == 1
    assert "Suite%3AName%2CPercent%25" in result.stdout
    assert "\n::warning::injected" not in result.stdout
    assert "\n::error::injected" not in result.stdout
    assert "%0A::warning::injected" in result.stdout
