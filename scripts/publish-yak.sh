#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
package="${1:-}"

if ! command -v yak >/dev/null 2>&1; then
  echo "yak CLI is required. Run this on a computer with Rhino 8 installed." >&2
  exit 1
fi

if [[ -z "$package" ]]; then
  package="$(find "$repo_dir/dist" -maxdepth 1 -name '*.yak' -print -quit)"
fi

if [[ -z "$package" || ! -f "$package" ]]; then
  echo "No .yak package found. Run ./scripts/build-yak.sh first." >&2
  exit 1
fi

yak push "$package"
