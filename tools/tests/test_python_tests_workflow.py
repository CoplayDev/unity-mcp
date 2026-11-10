"""A Codecov outage must not fail the Python test job, which gates both release pipelines."""
from pathlib import Path
import re


WORKFLOW = Path(__file__).resolve().parents[2] / ".github" / "workflows" / "python-tests.yml"


def coverage_upload_steps():
    text = WORKFLOW.read_text(encoding="utf-8")
    starts = [match.start() for match in re.finditer(r"^      - ", text, re.M)] + [len(text)]
    steps = [text[begin:end] for begin, end in zip(starts, starts[1:])]
    return [step for step in steps if "codecov/codecov-action@" in step]


def test_coverage_upload_cannot_fail_the_job():
    steps = coverage_upload_steps()
    assert len(steps) == 1
    # fail_ci_if_error: false does not stop codecov-action v4 crashing on a download error.
    assert re.search(r"^        continue-on-error: true$", steps[0], re.M), steps[0]
