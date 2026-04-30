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
VERSION="${VERSION:-0.1.0}"
DEB_FILE="${DEB_DIR}/lumyn_${VERSION}_amd64.deb"
TMP_DEB_FILE="${DEB_DIR}/.lumyn_${VERSION}_amd64.deb.tmp"

dotnet restore Lumyn.sln
dotnet build Lumyn.sln -c "${CONFIGURATION}" --no-restore
dotnet publish "${APP_PROJECT}" -c "${CONFIGURATION}" -r "${RID}" --self-contained true -o "${PUBLISH_DIR}"

rm -rf "${PACKAGE_ROOT}" "${DEB_DIR}"
mkdir -p "${PACKAGE_ROOT}/DEBIAN" "${PACKAGE_ROOT}/opt/lumyn" "${PACKAGE_ROOT}/usr/bin" "${PACKAGE_ROOT}/usr/share/applications" "${PACKAGE_ROOT}/usr/share/icons/hicolor/scalable/apps" "${DEB_DIR}"
cp -R "${PUBLISH_DIR}/." "${PACKAGE_ROOT}/opt/lumyn/"
cp "src/Lumyn.App/Assets/Icons/lumyn.svg" "${PACKAGE_ROOT}/usr/share/icons/hicolor/scalable/apps/lumyn.svg"
ln -s /opt/lumyn/Lumyn "${PACKAGE_ROOT}/usr/bin/lumyn"

cat > "${PACKAGE_ROOT}/DEBIAN/control" <<CONTROL
Package: lumyn
Version: ${VERSION}
Section: video
Priority: optional
Architecture: amd64
Maintainer: Lumyn Maintainers
Depends: vlc | libvlc5
Description: Lumyn desktop media player
 A clean Avalonia and LibVLCSharp desktop media player.
CONTROL

cat > "${PACKAGE_ROOT}/usr/share/applications/lumyn.desktop" <<DESKTOP
[Desktop Entry]
Name=Lumyn
Comment=Play local media files
Exec=/opt/lumyn/Lumyn
Icon=lumyn
Terminal=false
Type=Application
StartupWMClass=Lumyn
Categories=AudioVideo;Player;
DESKTOP

chmod 755 "${PACKAGE_ROOT}/DEBIAN"
chmod +x "${PACKAGE_ROOT}/opt/lumyn/Lumyn"
rm -f "${TMP_DEB_FILE}" "${DEB_FILE}"
dpkg-deb --root-owner-group -Zgzip --build "${PACKAGE_ROOT}" "${TMP_DEB_FILE}"

if ! ar t "${TMP_DEB_FILE}" | grep -q '^data\.tar'; then
  echo "Package validation failed: ${TMP_DEB_FILE} does not contain data.tar" >&2
  exit 1
fi

mv "${TMP_DEB_FILE}" "${DEB_FILE}"

echo "Linux artifacts:"
find artifacts -type f \( -name '*.deb' -o -name 'Lumyn' \) -print
