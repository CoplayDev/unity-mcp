#!/usr/bin/env bash
set -euo pipefail

git checkout main
git fetch -ap upstream
git fetch -ap
git rebase upstream/main
git push
