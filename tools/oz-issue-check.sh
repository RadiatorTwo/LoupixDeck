#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [ "$#" -lt 1 ]; then
  echo "Usage: $0 \"describe the issue to investigate\"" >&2
  exit 1
fi

PROMPT="$*"

exec oz agent run \
  --cwd "$REPO_ROOT" \
  --name "loupixdeck-issue-check" \
  --model "claude-4-5-sonnet" \
  --skill "loupixdeck-issue-checks" \
  --prompt "$PROMPT"
