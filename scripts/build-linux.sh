#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

APP_PROJECT="src/Lumyn.App/Lumyn.App.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"
RID="${RID:-linux-x64}"
PUBLISH_DIR="artifacts/publish/${RID}"
PACKAGE_ROOT="artifacts/pkg/lumyn-deb"
DEB_DIR="artifacts/packages"
BASE_VERSION="${BASE_VERSION:-$(tr -d '[:space:]' < VERSION)}"
BUILD_NUMBER="${BUILD_NUMBER:-${GITHUB_RUN_NUMBER:-0}}"
VERSION="${VERSION:-${BASE_VERSION}.${BUILD_NUMBER}}"

case "${RID}" in
  linux-x64) DEB_ARCH="amd64" ;;
  linux-arm64) DEB_ARCH="arm64" ;;
  *)
    echo "Unsupported RID for .deb packaging: ${RID}" >&2
    exit 1
    ;;
esac

DEB_FILE="${DEB_DIR}/lumyn_${VERSION}_${DEB_ARCH}.deb"
TMP_DEB_FILE="${DEB_DIR}/.lumyn_${VERSION}_${DEB_ARCH}.deb.tmp"

find_libmpv() {
  if [[ -n "${MPV_LIB_PATH:-}" ]]; then
    if [[ -f "${MPV_LIB_PATH}" ]]; then
      readlink -f "${MPV_LIB_PATH}"
      return
    fi
    echo "MPV_LIB_PATH does not point to a file: ${MPV_LIB_PATH}" >&2
    exit 1
  fi

  if command -v ldconfig >/dev/null 2>&1; then
    local found
    found="$(ldconfig -p | awk '/libmpv\.so\.2 / { print $NF; exit }')"
    if [[ -n "${found}" && -f "${found}" ]]; then
      readlink -f "${found}"
      return
    fi
  fi

  for candidate in \
    /usr/lib/*/libmpv.so.2 \
    /usr/local/lib*/libmpv.so.2 \
    /lib/*/libmpv.so.2; do
    if [[ -f "${candidate}" ]]; then
      readlink -f "${candidate}"
      return
    fi
  done

  echo "libmpv.so.2 was not found on this build machine." >&2
  echo "Install libmpv-dev/libmpv2 for packaging, or set MPV_LIB_PATH=/path/to/libmpv.so.2." >&2
  exit 1
}

copy_shared_library() {
  local source="$1"
  local target_dir="$2"
  local resolved
  local base

  resolved="$(readlink -f "${source}")"
  base="$(basename "${resolved}")"

  if [[ ! -f "${target_dir}/${base}" ]]; then
    cp -a "${resolved}" "${target_dir}/${base}"
    chmod 755 "${target_dir}/${base}" || true
  fi

  if [[ "$(basename "${source}")" != "${base}" && ! -e "${target_dir}/$(basename "${source}")" ]]; then
    ln -s "${base}" "${target_dir}/$(basename "${source}")"
  fi
}

should_bundle_library() {
  local lib="$1"
  local base
  base="$(basename "${lib}")"

  case "${base}" in
    ld-linux*.so*|linux-vdso.so*|libc.so*|libm.so*|libdl.so*|libpthread.so*|librt.so*|libresolv.so*|libnsl.so*|libutil.so*)
      return 1
      ;;
  esac

  return 0
}

bundle_library_closure() {
  local root_lib="$1"
  local target_dir="$2"
  local queue=("${root_lib}")
  local seen=" "
  local current
  local resolved
  local dep

  mkdir -p "${target_dir}"

  while ((${#queue[@]})); do
    current="${queue[0]}"
    queue=("${queue[@]:1}")
    resolved="$(readlink -f "${current}")"

    if [[ "${seen}" == *" ${resolved} "* ]]; then
      continue
    fi
    seen="${seen}${resolved} "

    if should_bundle_library "${resolved}"; then
      copy_shared_library "${current}" "${target_dir}"
    fi

    while IFS= read -r dep; do
      [[ -n "${dep}" && -f "${dep}" ]] || continue
      if should_bundle_library "${dep}"; then
        queue+=("${dep}")
      fi
    done < <(ldd "${resolved}" | awk '
      /=> \// { print $3 }
      /^[[:space:]]*\// { print $1 }
    ')
  done
}

bundle_published_native_dependencies() {
  local app_dir="$1"
  local target_dir="$2"
  local file
  local dep

  while IFS= read -r -d '' file; do
    if ldd "${file}" >/dev/null 2>&1; then
      while IFS= read -r dep; do
        [[ -n "${dep}" && -f "${dep}" ]] || continue
        if should_bundle_library "${dep}"; then
          bundle_library_closure "${dep}" "${target_dir}"
        fi
      done < <(ldd "${file}" | awk '
        /=> \// { print $3 }
        /^[[:space:]]*\// { print $1 }
      ')
    fi
  done < <(find "${app_dir}" -maxdepth 1 -type f -print0)
}

dotnet restore Lumyn.sln
dotnet build Lumyn.sln -c "${CONFIGURATION}" --no-restore
dotnet publish "${APP_PROJECT}" -c "${CONFIGURATION}" -r "${RID}" --self-contained true -o "${PUBLISH_DIR}"

MPV_LIB="$(find_libmpv)"

rm -rf "${PACKAGE_ROOT}" "${DEB_DIR}"
mkdir -p "${PACKAGE_ROOT}/DEBIAN" "${PACKAGE_ROOT}/opt/lumyn" "${PACKAGE_ROOT}/usr/bin" "${PACKAGE_ROOT}/usr/share/applications" "${PACKAGE_ROOT}/usr/share/icons/hicolor/scalable/apps" "${PACKAGE_ROOT}/usr/share/mime/packages" "${DEB_DIR}"
cp -R "${PUBLISH_DIR}/." "${PACKAGE_ROOT}/opt/lumyn/"
bundle_library_closure "${MPV_LIB}" "${PACKAGE_ROOT}/opt/lumyn/lib"
bundle_published_native_dependencies "${PACKAGE_ROOT}/opt/lumyn" "${PACKAGE_ROOT}/opt/lumyn/lib"
ln -sf "$(basename "$(readlink -f "${MPV_LIB}")")" "${PACKAGE_ROOT}/opt/lumyn/lib/libmpv.so.2"
cp "src/Lumyn.App/Assets/Icons/lumyn.svg" "${PACKAGE_ROOT}/usr/share/icons/hicolor/scalable/apps/lumyn.svg"

cat > "${PACKAGE_ROOT}/opt/lumyn/lumyn" <<'LAUNCHER'
#!/bin/sh
set -eu

APP_DIR="/opt/lumyn"
export LD_LIBRARY_PATH="${APP_DIR}/lib${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
exec "${APP_DIR}/Lumyn" "$@"
LAUNCHER

ln -s /opt/lumyn/lumyn "${PACKAGE_ROOT}/usr/bin/lumyn"

cat > "${PACKAGE_ROOT}/DEBIAN/control" <<CONTROL
Package: lumyn
Version: ${VERSION}
Section: video
Priority: optional
Architecture: ${DEB_ARCH}
Maintainer: Lumyn Maintainers
Description: Lumyn desktop media player
 A clean Avalonia and mpv desktop media player.
CONTROL

cat > "${PACKAGE_ROOT}/usr/share/applications/lumyn.desktop" <<DESKTOP
[Desktop Entry]
Name=Lumyn
Comment=Play local media files
Exec=/opt/lumyn/lumyn %U
Icon=lumyn
Terminal=false
Type=Application
StartupWMClass=Lumyn
Categories=AudioVideo;Player;
MimeType=video/mp4;video/x-matroska;video/webm;video/x-msvideo;video/quicktime;video/mpeg;video/x-flv;video/3gpp;video/x-ms-wmv;video/ogg;video/mp2t;video/divx;video/x-ogm+ogg;audio/mpeg;audio/flac;audio/ogg;audio/x-wav;audio/mp4;audio/x-m4a;audio/aac;audio/x-ms-wma;audio/opus;audio/x-matroska;
DESKTOP

cat > "${PACKAGE_ROOT}/usr/share/mime/packages/lumyn.xml" <<'MIMEXML'
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="video/x-matroska">
    <comment>Matroska video</comment>
    <glob pattern="*.mkv"/>
    <glob pattern="*.mk3d"/>
  </mime-type>
  <mime-type type="video/divx">
    <comment>DivX video</comment>
    <glob pattern="*.divx"/>
  </mime-type>
  <mime-type type="video/x-ogm+ogg">
    <comment>OGM video</comment>
    <glob pattern="*.ogm"/>
  </mime-type>
  <mime-type type="audio/x-matroska">
    <comment>Matroska audio</comment>
    <glob pattern="*.mka"/>
  </mime-type>
</mime-info>
MIMEXML

cat > "${PACKAGE_ROOT}/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications || true
fi
if command -v update-mime-database >/dev/null 2>&1; then
  update-mime-database /usr/share/mime || true
fi
POSTINST

cat > "${PACKAGE_ROOT}/DEBIAN/postrm" <<'POSTRM'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications || true
fi
if command -v update-mime-database >/dev/null 2>&1; then
  update-mime-database /usr/share/mime || true
fi
POSTRM

chmod 755 "${PACKAGE_ROOT}/DEBIAN"
chmod +x "${PACKAGE_ROOT}/DEBIAN/postinst"
chmod +x "${PACKAGE_ROOT}/DEBIAN/postrm"
chmod +x "${PACKAGE_ROOT}/opt/lumyn/Lumyn"
chmod +x "${PACKAGE_ROOT}/opt/lumyn/lumyn"
rm -f "${TMP_DEB_FILE}" "${DEB_FILE}"
dpkg-deb --root-owner-group -Zgzip --build "${PACKAGE_ROOT}" "${TMP_DEB_FILE}"

if ! ar t "${TMP_DEB_FILE}" | grep -q '^data\.tar'; then
  echo "Package validation failed: ${TMP_DEB_FILE} does not contain data.tar" >&2
  exit 1
fi

mv "${TMP_DEB_FILE}" "${DEB_FILE}"

echo "Linux artifacts:"
find artifacts -type f \( -name '*.deb' -o -name 'Lumyn' \) -print
