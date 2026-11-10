"""The Unity test workflow must not float on game-ci's v4 tag or its CLI's latest release."""
from pathlib import Path
import re


WORKFLOW = Path(__file__).resolve().parents[2] / ".github" / "workflows" / "unity-tests.yml"


def runner_steps():
    text = WORKFLOW.read_text(encoding="utf-8")
    starts = [match.start() for match in re.finditer(r"^      - ", text, re.M)] + [len(text)]
    steps = [text[begin:end] for begin, end in zip(starts, starts[1:])]
    return [step for step in steps if "game-ci/unity-test-runner@" in step]


def test_every_runner_step_pins_the_action_commit_and_cli_release():
    steps = runner_steps()
    assert len(steps) == 2
    for step in steps:
        # Both failures flow into their XML gate, which checks the raw outcome.
        assert re.search(r'^        continue-on-error: true$', step, re.M), step
        ref = re.search(r"uses: game-ci/unity-test-runner@(\S+)", step).group(1)
        assert re.fullmatch(r"[0-9a-f]{40}", ref), ref
        assert re.search(r"^          cliVersion: v\d+\.\d+\.\d+$", step, re.M), step
        # The read-only job cannot create a check run; the local gate reads the XML instead.
        assert re.search(r'^          githubToken: ""$', step, re.M), step
