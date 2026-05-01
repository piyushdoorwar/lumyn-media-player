#!/usr/bin/env bash
# Build a Lumyn Flatpak bundle (.flatpak).
#
# Prerequisites (local):
#   flatpak flatpak-builder
#   flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
#
# On GitHub Actions the linux-flatpak workflow handles tool installation
# via flatpak/flatpak-github-actions — this script is mainly for local builds.
#
# Usage:
#   ./scripts/build-linux-flatpak.sh
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

APP_PROJECT="src/Lumyn.App/Lumyn.App.csproj"

BASE_VERSION="${BASE_VERSION:-$(tr -d '[:space:]' < VERSION)}"
BUILD_NUMBER="${BUILD_NUMBER:-${GITHUB_RUN_NUMBER:-0}}"
VERSION="${VERSION:-${BASE_VERSION}.${BUILD_NUMBER}}"

# Where flatpak-builder expects the pre-built .NET binary (type:dir source)
FLATPAK_BUILD_SRC="packaging/flatpak/build-src"
FLATPAK_REPO="${REPO_ROOT}/artifacts/flatpak-repo"
PACKAGE_OUT_DIR="artifacts/packages"
BUNDLE_FILE="${PACKAGE_OUT_DIR}/lumyn_${VERSION}_linux-x64.flatpak"

# ── 1. Build self-contained .NET binary ─────────────────────────────────────
echo "→ Building .NET binary (linux-x64, self-contained)…"
dotnet restore Lumyn.sln
dotnet build Lumyn.sln -c Release --no-restore
dotnet publish "${APP_PROJECT}" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "${FLATPAK_BUILD_SRC}"

# ── 2. Build Flatpak ─────────────────────────────────────────────────────────
echo "→ Running flatpak-builder…"
mkdir -p "${PACKAGE_OUT_DIR}"
flatpak-builder \
    --user \
    --install-deps-from=flathub \
    --force-clean \
    --repo="${FLATPAK_REPO}" \
    "${REPO_ROOT}/artifacts/flatpak-build" \
    "packaging/flatpak/io.github.piyushdoorwar.lumyn.yml"

# ── 3. Export to single .flatpak bundle ──────────────────────────────────────
echo "→ Exporting bundle…"
flatpak build-bundle \
    "${FLATPAK_REPO}" \
    "${BUNDLE_FILE}" \
    io.github.piyushdoorwar.lumyn

echo ""
echo "Flatpak artifacts:"
echo "${BUNDLE_FILE}"
