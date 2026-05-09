#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

if ! command -v snapcraft >/dev/null 2>&1; then
  echo "snapcraft was not found. Install it with: sudo snap install snapcraft --classic" >&2
  exit 1
fi

mkdir -p snap/gui
cp "packaging/snap/snapcraft.yaml" "snap/snapcraft.yaml"
cp "src/Lumyn.App/Assets/Icons/lumyn.svg" "snap/gui/icon.svg"
trap 'rm -rf "${REPO_ROOT}/snap"' EXIT

snapcraft "$@"
