"""release.yml must start the release-notes sync itself.

It creates the release with GITHUB_TOKEN, and events made with that token start no workflows
(only workflow_dispatch and repository_dispatch are exempt), so sync-releases.yml never saw
`release: published` and README/releases.md stopped at v10.0.0.
"""
from pathlib import Path
import re


WORKFLOWS = Path(__file__).resolve().parents[2] / ".github" / "workflows"


def release_jobs():
    text = (WORKFLOWS / "release.yml").read_text(encoding="utf-8")
    body = text.split("\njobs:\n", 1)[1]
    starts = [match.start() for match in re.finditer(r"^  \w+:$", body, re.M)] + [len(body)]
    return [body[begin:end] for begin, end in zip(starts, starts[1:])]


def test_release_dispatches_the_notes_sync_which_lands_on_beta_through_a_pr():
    jobs = [job for job in release_jobs() if "gh workflow run sync-releases.yml" in job]
    assert len(jobs) == 1, "release.yml must dispatch sync-releases.yml"
    assert re.search(r"gh workflow run sync-releases\.yml\b.*--ref beta\b", jobs[0]), jobs[0]
    assert re.search(r"^      actions: write$", jobs[0], re.M), jobs[0]

    sync = (WORKFLOWS / "sync-releases.yml").read_text(encoding="utf-8")
    assert re.search(r"^  workflow_dispatch:", sync, re.M), sync
    # The beta ruleset requires a pull request, so a direct push is rejected.
    assert "gh pr create" in sync and "--base beta" in sync, sync
    assert not re.search(r"git push\b.*\bbeta\b", sync), sync
