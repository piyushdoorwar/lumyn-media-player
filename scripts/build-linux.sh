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
VERSION="${VERSION:-0.0.0-dev}"

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

dotnet restore Lumyn.sln
dotnet build Lumyn.sln -c "${CONFIGURATION}" --no-restore
dotnet publish "${APP_PROJECT}" -c "${CONFIGURATION}" -r "${RID}" --self-contained true -o "${PUBLISH_DIR}" \
  -p:Version="${VERSION}" -p:InformationalVersion="${VERSION}"

rm -rf "${PACKAGE_ROOT}" "${DEB_DIR}"
mkdir -p "${PACKAGE_ROOT}/DEBIAN" "${PACKAGE_ROOT}/opt/lumyn" "${PACKAGE_ROOT}/usr/bin" "${PACKAGE_ROOT}/usr/share/applications" "${PACKAGE_ROOT}/usr/share/icons/hicolor/scalable/apps" "${PACKAGE_ROOT}/usr/share/mime/packages" "${DEB_DIR}"
cp -R "${PUBLISH_DIR}/." "${PACKAGE_ROOT}/opt/lumyn/"
cp "src/Lumyn.App/Assets/Icons/lumyn.svg" "${PACKAGE_ROOT}/usr/share/icons/hicolor/scalable/apps/lumyn.svg"

cat > "${PACKAGE_ROOT}/opt/lumyn/lumyn" <<'LAUNCHER'
#!/bin/sh
set -eu

APP_DIR="/opt/lumyn"
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
Depends: libmpv2, libfontconfig1, libgl1, libx11-6, libxcb1, libxext6, libxrandr2, libxrender1, libxi6, libxfixes3, libxcursor1, libxinerama1, libasound2t64, libpulse0
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
