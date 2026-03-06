#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
missing=()
for id in NL-0 NL-1 NL-2 NL-3 NL-4 T-A T-B T-C T-D T-E T-F T-G T-H T-I T-J; do
  [[ -s "reports/${id}_results.xml" ]] || missing+=("$id")
done
if (( ${#missing[@]} )); then
  echo "Missing fragments: ${missing[*]}"
  exit 2
fi
echo "All NL/T fragments present."
