"""Gate Unity CI on both the runner outcome and its NUnit result file."""
from __future__ import annotations

import argparse
from pathlib import Path
import xml.etree.ElementTree as ET


def escape_data(value: str) -> str:
    return value.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def escape_property(value: str) -> str:
    return escape_data(value).replace(":", "%3A").replace(",", "%2C")


def check_results(path: Path, runner_outcome: str) -> int:
    runner_failed = runner_outcome != "success"
    if runner_failed:
        print(f"::error::Unity test runner did not succeed: {escape_data(runner_outcome)}")
    try:
        root = ET.parse(path).getroot()
        if root.tag != "test-run":
            raise ValueError("Expected an NUnit test-run result")
        total = int(root.attrib["total"])
        passed = int(root.attrib["passed"])
        failed = int(root.attrib["failed"])
        if min(total, passed, failed) < 0 or passed + failed > total:
            raise ValueError("Invalid NUnit result counts")
    except (OSError, ET.ParseError, ValueError, KeyError) as exc:
        print(f"::error::Cannot validate Unity test results: {escape_data(str(exc))}")
        return 1

    print(f"Results: {passed} passed, {failed} failed (total: {total})")
    failures = [case for case in root.iter("test-case") if case.get("result") == "Failed"]
    for case in failures:
        name = case.get("fullname") or case.get("name") or "<unknown>"
        failure = case.find("failure")
        message = (failure.findtext("message") or "").strip() if failure is not None else ""
        stack = (failure.findtext("stack-trace") or "").strip() if failure is not None else ""
        first_line = message.splitlines()[0] if message else "(no message)"
        print(f"::error title=Failed: {escape_property(name)}::{escape_data(first_line)}")
        print(f"::group::Failure details — {escape_data(name)}")
        # Escape test-controlled newlines so they cannot inject workflow commands.
        if message:
            print(f"Message: {escape_data(message)}")
        if stack:
            print(f"Stack trace: {escape_data(stack)}")
        print("::endgroup::")

    if failures or failed or root.get("result") != "Passed":
        print("::error::Unity reported an unsuccessful test run")
        return 1
    if total == 0 or passed == 0:
        print("::error::Unity did not execute any passing tests")
        return 1
    return 1 if runner_failed else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("results", type=Path)
    parser.add_argument("--runner-outcome", required=True)
    args = parser.parse_args()
    return check_results(args.results, args.runner_outcome)


if __name__ == "__main__":
    raise SystemExit(main())
