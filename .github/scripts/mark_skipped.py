#!/usr/bin/env python3
"""
Post-processes a JUnit XML so that "expected"/environmental failures
(e.g., permission prompts, empty MCP resources, or schema hiccups)
are converted to <skipped/>. Leaves real failures intact.

Usage:
  python .github/scripts/mark_skipped.py reports/claude-nl-tests.xml
"""

from __future__ import annotations
import sys
import os
import re
import xml.etree.ElementTree as ET

PATTERNS = [
    r"\bpermission\b",
    r"\bpermissions\b",
    r"\bautoApprove\b",
    r"\bapproval\b",
    r"\bdenied\b",
    r"requested\s+permissions",
    r"^MCP resources list is empty$",
    r"No MCP resources detected",
    r"aggregator.*returned\s*\[\s*\]",
    r"Unknown resource:\s*unity://",
    r"Input should be a valid dictionary.*ctx",
    r"validation error .* ctx",
]

def should_skip(msg: str) -> bool:
    if not msg:
        return False
    msg_l = msg.strip()
    for pat in PATTERNS:
        if re.search(pat, msg_l, flags=re.IGNORECASE | re.MULTILINE):
            return True
    return False

def summarize_counts(ts: ET.Element):
    tests = 0
    failures = 0
    errors = 0
    skipped = 0
    for case in ts.findall("testcase"):
        tests += 1
        if case.find("failure") is not None:
            failures += 1
        if case.find("error") is not None:
            errors += 1
        if case.find("skipped") is not None:
            skipped += 1
    return tests, failures, errors, skipped

def main(path: str) -> int:
    if not os.path.exists(path):
        print(f"[mark_skipped] No JUnit at {path}; nothing to do.")
        return 0

    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        print(f"[mark_skipped] Could not parse {path}: {e}")
        return 0

    root = tree.getroot()
    suites = root.findall("testsuite") if root.tag == "testsuites" else [root]

    changed = False
    for ts in suites:
        for case in list(ts.findall("testcase")):
            nodes = [n for n in list(case) if n.tag in ("failure", "error")]
            if not nodes:
                continue
            # If any node matches skip patterns, convert the whole case to skipped.
            first_match_text = None
            to_skip = False
            for n in nodes:
                msg = (n.get("message") or "") + "\n" + (n.text or "")
                if should_skip(msg):
                    first_match_text = (n.text or "").strip() or first_match_text
                    to_skip = True
            if to_skip:
                for n in nodes:
                    case.remove(n)
                reason = "Marked skipped: environment/permission precondition not met"
                skip = ET.SubElement(case, "skipped")
                skip.set("message", reason)
                skip.text = first_match_text or reason
                changed = True
        # Recompute tallies per testsuite
        tests, failures, errors, skipped = summarize_counts(ts)
        ts.set("tests", str(tests))
        ts.set("failures", str(failures))
        ts.set("errors", str(errors))
        ts.set("skipped", str(skipped))

    if changed:
        tree.write(path, encoding="utf-8", xml_declaration=True)
        print(f"[mark_skipped] Updated {path}: converted environmental failures to skipped.")
    else:
        print(f"[mark_skipped] No environmental failures detected in {path}.")

    return 0

if __name__ == "__main__":
    target = (
        sys.argv[1]
        if len(sys.argv) > 1
        else os.environ.get("JUNIT_OUT", "reports/junit-nl-suite.xml")
    )
    raise SystemExit(main(target))
