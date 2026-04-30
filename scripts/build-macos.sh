#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

APP_PROJECT="src/Lumyn.App/Lumyn.App.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"
RID="${RID:-osx-arm64}"
PUBLISH_DIR="artifacts/publish/${RID}"
PACKAGE_DIR="artifacts/pkg/lumyn-macos/${RID}"
APP_ROOT="${PACKAGE_DIR}/Lumyn.app"
CONTENTS_DIR="${APP_ROOT}/Contents"
MACOS_DIR="${CONTENTS_DIR}/MacOS"
RESOURCES_DIR="${CONTENTS_DIR}/Resources"
PACKAGE_OUT_DIR="artifacts/packages"
BASE_VERSION="${BASE_VERSION:-$(tr -d '[:space:]' < VERSION)}"
BUILD_NUMBER="${BUILD_NUMBER:-${GITHUB_RUN_NUMBER:-0}}"
VERSION="${VERSION:-${BASE_VERSION}.${BUILD_NUMBER}}"

case "${RID}" in
  osx-arm64) MAC_ARCH="arm64" ;;
  osx-x64) MAC_ARCH="x64" ;;
  *)
    echo "Unsupported macOS RID: ${RID}" >&2
    exit 1
    ;;
esac

ZIP_FILE="${PACKAGE_OUT_DIR}/lumyn_${VERSION}_macos-${MAC_ARCH}.zip"

find_libmpv() {
  if [[ -n "${MPV_LIB_PATH:-}" ]]; then
    if [[ -f "${MPV_LIB_PATH}" ]]; then
      readlink "${MPV_LIB_PATH}" || printf '%s\n' "${MPV_LIB_PATH}"
      return
    fi
    echo "MPV_LIB_PATH does not point to a file: ${MPV_LIB_PATH}" >&2
    exit 1
  fi

  local brew_prefix=""
  if command -v brew >/dev/null 2>&1; then
    brew_prefix="$(brew --prefix 2>/dev/null || true)"
  fi

  for candidate in \
    "${brew_prefix}/opt/mpv/lib/libmpv.2.dylib" \
    "${brew_prefix}/opt/mpv/lib/libmpv.dylib" \
    "${brew_prefix}/lib/libmpv.2.dylib" \
    "${brew_prefix}/lib/libmpv.dylib" \
    /opt/homebrew/opt/mpv/lib/libmpv.2.dylib \
    /opt/homebrew/opt/mpv/lib/libmpv.dylib \
    /usr/local/opt/mpv/lib/libmpv.2.dylib \
    /usr/local/opt/mpv/lib/libmpv.dylib; do
    if [[ -f "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return
    fi
  done

  echo "libmpv.dylib was not found. Install mpv with Homebrew or set MPV_LIB_PATH." >&2
  exit 1
}

is_system_library() {
  local lib="$1"
  [[ "${lib}" == /usr/lib/* || "${lib}" == /System/Library/* ]]
}

resolve_dependency() {
  local dep="$1"
  local from_dir="$2"

  case "${dep}" in
    @loader_path/*)
      printf '%s\n' "${from_dir}/${dep#@loader_path/}"
      ;;
    @executable_path/*)
      printf '%s\n' "${MACOS_DIR}/${dep#@executable_path/}"
      ;;
    @rpath/*)
      local name="${dep#@rpath/}"
      local brew_prefix=""
      if command -v brew >/dev/null 2>&1; then
        brew_prefix="$(brew --prefix 2>/dev/null || true)"
      fi
      for candidate in \
        "${from_dir}/${name}" \
        "${brew_prefix}/lib/${name}" \
        "${brew_prefix}/opt/mpv/lib/${name}" \
        /opt/homebrew/lib/"${name}" \
        /usr/local/lib/"${name}"; do
        if [[ -f "${candidate}" ]]; then
          printf '%s\n' "${candidate}"
          return
        fi
      done
      printf '%s\n' "${dep}"
      ;;
    *)
      printf '%s\n' "${dep}"
      ;;
  esac
}

copy_dylib_closure() {
  local root="$1"
  local queue=("${root}")
  local seen=" "
  local current resolved base from_dir dep resolved_dep

  mkdir -p "${MACOS_DIR}"

  while ((${#queue[@]})); do
    current="${queue[0]}"
    queue=("${queue[@]:1}")
    resolved="$(cd "$(dirname "${current}")" && pwd -P)/$(basename "${current}")"

    if [[ "${seen}" == *" ${resolved} "* ]]; then
      continue
    fi
    seen="${seen}${resolved} "

    if ! is_system_library "${resolved}" && [[ -f "${resolved}" ]]; then
      base="$(basename "${resolved}")"
      if [[ ! -f "${MACOS_DIR}/${base}" ]]; then
        cp -pL "${resolved}" "${MACOS_DIR}/${base}"
        chmod 755 "${MACOS_DIR}/${base}" || true
      fi
    fi

    from_dir="$(dirname "${resolved}")"
    while IFS= read -r dep; do
      resolved_dep="$(resolve_dependency "${dep}" "${from_dir}")"
      [[ -f "${resolved_dep}" ]] || continue
      if ! is_system_library "${resolved_dep}"; then
        queue+=("${resolved_dep}")
      fi
    done < <(otool -L "${resolved}" | tail -n +2 | awk '{ print $1 }')
  done
}

fix_install_names() {
  local file dep dep_base
  local copied=()
  while IFS= read -r line; do
    copied+=("${line}")
  done < <(find "${MACOS_DIR}" -maxdepth 1 -type f \( -name '*.dylib' -o -name '*.so' \) -print)

  for file in "${copied[@]}"; do
    install_name_tool -id "@loader_path/$(basename "${file}")" "${file}" || true
  done

  for file in "${copied[@]}"; do
    while IFS= read -r dep; do
      dep_base="$(basename "${dep}")"
      if [[ -f "${MACOS_DIR}/${dep_base}" && "${dep}" != "@loader_path/${dep_base}" ]]; then
        install_name_tool -change "${dep}" "@loader_path/${dep_base}" "${file}" || true
      fi
    done < <(otool -L "${file}" | tail -n +2 | awk '{ print $1 }')
  done
}

dotnet restore Lumyn.sln
dotnet build Lumyn.sln -c "${CONFIGURATION}" --no-restore
dotnet publish "${APP_PROJECT}" -c "${CONFIGURATION}" -r "${RID}" --self-contained true -o "${PUBLISH_DIR}"

MPV_LIB="$(find_libmpv)"

rm -rf "${PACKAGE_DIR}"
mkdir -p "${MACOS_DIR}" "${RESOURCES_DIR}" "${PACKAGE_OUT_DIR}"
cp -R "${PUBLISH_DIR}/." "${MACOS_DIR}/"
copy_dylib_closure "${MPV_LIB}"
fix_install_names
cp "src/Lumyn.App/Assets/Icons/lumyn.svg" "${RESOURCES_DIR}/lumyn.svg"
mkdir -p "${RESOURCES_DIR}/licenses"
cp "LICENSE" "${RESOURCES_DIR}/licenses/Lumyn-LICENSE.txt"
cp "packaging/macos/THIRD-PARTY-NOTICES.txt" "${RESOURCES_DIR}/licenses/THIRD-PARTY-NOTICES.txt"

cat > "${CONTENTS_DIR}/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Lumyn</string>
  <key>CFBundleDisplayName</key>
  <string>Lumyn</string>
  <key>CFBundleIdentifier</key>
  <string>io.github.piyushdoorwar.lumyn</string>
  <key>CFBundleVersion</key>
  <string>${VERSION}</string>
  <key>CFBundleShortVersionString</key>
  <string>${VERSION}</string>
  <key>CFBundleExecutable</key>
  <string>Lumyn</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

printf 'APPL????' > "${CONTENTS_DIR}/PkgInfo"
chmod +x "${MACOS_DIR}/Lumyn"

rm -f "${ZIP_FILE}"
ditto -c -k --sequesterRsrc --keepParent "${APP_ROOT}" "${ZIP_FILE}"

echo "macOS artifacts:"
echo "${ZIP_FILE}"
