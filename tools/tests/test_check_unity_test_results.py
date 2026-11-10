"""Exercise the CI gate with real result files and runner outcomes."""
from pathlib import Path
import os
import subprocess
import sys

import pytest


GATE = Path(__file__).resolve().parents[1] / "check_unity_test_results.py"
COUNTS = 'inconclusive="0" skipped="0"'
PASSING = '<test-run result="Passed" total="2" passed="1" failed="0" inconclusive="0" skipped="1"><test-case result="Passed"/><test-case result="Skipped"/></test-run>'
# <test-run> attributes exactly as Unity wrote them on green beta run 33978935244 (all four Unity versions):
# a clean run that contains [Ignore]d tests reports result="Skipped:Ignored", not "Passed".
UNITY_CLEAN_RUN = (
    '<test-run id="2" testcasecount="1229" result="Skipped:Ignored" total="1229" passed="1162" failed="0" inconclusive="0" skipped="67" asserts="0" engine-version="3.5.0.0">'
    '<test-suite>' + '<test-case result="Passed"/>' * 1162 + '<test-case result="Skipped"/>' * 67
    + '</test-suite></test-run>'
)
# Beta run 29283113713 (6000.0.75f1), before #1294 moved ManageGraphicsTests off Assume.That.
UNITY_INCONCLUSIVE_RUN = '''<test-run id="2" testcasecount="1166" result="Skipped:Ignored" total="1166" passed="1100" failed="0" inconclusive="18" skipped="48" asserts="0" engine-version="3.5.0.0">
  <test-case fullname="MCPForUnityTests.Editor.Tools.ManageGraphicsTests.FeatureAdd_InvalidType_ReturnsError" result="Inconclusive">
    <reason><message><![CDATA[URP not available — skipping.
::error::injected]]></message></reason>
  </test-case></test-run>'''


def run_gate(tmp_path, xml, outcome="success"):
    results = tmp_path / "editmode-results.xml"
    if xml is not None:
        results.write_text(xml, encoding="utf-8")
    return subprocess.run(
        [sys.executable, str(GATE), str(results), "--runner-outcome", outcome],
        capture_output=True, text=True, encoding="utf-8",
        env={**os.environ, "PYTHONIOENCODING": "utf-8"},
    )


@pytest.mark.parametrize("xml", [PASSING, UNITY_CLEAN_RUN], ids=["passing", "unity-ignored"])
def test_successful_runner_and_completed_results_pass(tmp_path, xml):
    result = run_gate(tmp_path, xml)
    assert result.returncode == 0, result.stdout
    assert "failed, 0 inconclusive" in result.stdout


@pytest.mark.parametrize("records", ["", '<test-suite result="Passed"/>',
                                      '<test-case result="Passed"/>' * 2])
def test_summary_cannot_claim_passes_without_matching_test_cases(tmp_path, records):
    xml = (f'<test-run result="Passed" total="1" passed="1" failed="0" {COUNTS}>'
           f'{records}</test-run>')
    result = run_gate(tmp_path, xml)
    assert result.returncode == 1
    assert "passing test-case records" in result.stdout


# Unity exits 2 when any test is Inconclusive, so with githubToken "" game-ci fails the step and CI
# passes "failure"; that run logged "0 failed" and then "Exiting with code 2". A local caller may pass
# "success". Either way the gate must fail and name the inconclusive test.
@pytest.mark.parametrize("outcome", ["failure", "success"])
def test_inconclusive_tests_fail_and_are_named(tmp_path, outcome):
    result = run_gate(tmp_path, UNITY_INCONCLUSIVE_RUN, outcome)
    assert result.returncode == 1, result.stdout
    assert "18 inconclusive, 48 skipped (total: 1166)" in result.stdout
    assert "::error title=Inconclusive: MCPForUnityTests.Editor.Tools.ManageGraphicsTests" in result.stdout
    assert "URP not available" in result.stdout
    assert "use Assert.Ignore" in result.stdout
    assert "\n::error::injected" not in result.stdout


@pytest.mark.parametrize("outcome", ["failure", "cancelled", "skipped", ""])
def test_passing_xml_cannot_hide_runner_failure(tmp_path, outcome):
    result = run_gate(tmp_path, PASSING, outcome)
    assert result.returncode == 1
    assert "runner did not succeed" in result.stdout


@pytest.mark.parametrize("xml", [
    None,
    "not XML",
    f'<coverage total="3" passed="3" failed="0" {COUNTS}/>',
    '<test-run result="Passed"/>',
    f'<test-run result="Passed" total="invalid" passed="1" failed="0" {COUNTS}/>',
    f'<test-run result="Passed" total="1" passed="-1" failed="0" {COUNTS}/>',
    f'<test-run result="Passed" total="1" passed="2" failed="0" {COUNTS}/>',
    # Outcome buckets must account for every test, not just stay under total.
    f'<test-run result="Passed" total="100" passed="1" failed="0" {COUNTS}/>',
    '<test-run result="Passed" total="2" passed="1" failed="0" inconclusive="-1" skipped="2"/>',
    '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0"/>',
    '<test-run result="Passed" total="1" passed="1" failed="0" inconclusive="0"/>',
])
def test_missing_or_invalid_results_fail(tmp_path, xml):
    result = run_gate(tmp_path, xml)
    assert result.returncode == 1
    assert "Cannot validate" in result.stdout


@pytest.mark.parametrize("xml", [
    f'<test-run result="Failed" total="1" passed="1" failed="0" {COUNTS}/>',
    f'<test-run result="Failed(Child)" total="1" passed="1" failed="0" {COUNTS}/>',
    f'<test-run result="Failed:Cancelled" total="1" passed="1" failed="0" {COUNTS}/>',
    f'<test-run result="Passed" total="1" passed="0" failed="1" {COUNTS}/>',
    f'<test-run result="Cancelled" total="1" passed="1" failed="0" {COUNTS}/>',
    f'<test-run result="Warning" total="1" passed="1" failed="0" {COUNTS}/>',
    '<test-run result="Inconclusive" total="3" passed="0" failed="0" inconclusive="3" skipped="0"/>',
    f'<test-run total="1" passed="1" failed="0" {COUNTS}/>',
    f'<test-run result="Passed" total="1" passed="1" failed="0" {COUNTS}><test-case result="Failed"/></test-run>',
])
def test_suite_or_case_failure_is_not_a_pass(tmp_path, xml):
    result = run_gate(tmp_path, xml)
    assert result.returncode == 1
    assert "unsuccessful test run" in result.stdout


@pytest.mark.parametrize("result_state, inconclusive, skipped", [
    ("Passed", 0, 0), ("Skipped:Ignored", 0, 3), ("Skipped:Ignored", 2, 1),
])
def test_runs_without_a_passing_test_fail(tmp_path, result_state, inconclusive, skipped):
    total = inconclusive + skipped
    xml = (f'<test-run result="{result_state}" total="{total}" passed="0" failed="0" '
           f'inconclusive="{inconclusive}" skipped="{skipped}"/>')
    result = run_gate(tmp_path, xml)
    assert result.returncode == 1
    assert "did not execute" in result.stdout


def test_failure_details_cannot_inject_workflow_commands(tmp_path):
    xml = f'''<test-run result="Failed" total="1" passed="0" failed="1" {COUNTS}>
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
