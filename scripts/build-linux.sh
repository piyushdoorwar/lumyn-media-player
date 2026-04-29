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

dotnet restore Lumyn.sln
dotnet build Lumyn.sln -c "${CONFIGURATION}" --no-restore
dotnet publish "${APP_PROJECT}" -c "${CONFIGURATION}" -r "${RID}" --self-contained true -o "${PUBLISH_DIR}"

rm -rf "${PACKAGE_ROOT}" "${DEB_DIR}"
mkdir -p "${PACKAGE_ROOT}/DEBIAN" "${PACKAGE_ROOT}/opt/lumyn" "${PACKAGE_ROOT}/usr/bin" "${PACKAGE_ROOT}/usr/share/applications" "${DEB_DIR}"
cp -R "${PUBLISH_DIR}/." "${PACKAGE_ROOT}/opt/lumyn/"
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
Terminal=false
Type=Application
Categories=AudioVideo;Player;
DESKTOP

chmod 755 "${PACKAGE_ROOT}/DEBIAN"
chmod +x "${PACKAGE_ROOT}/opt/lumyn/Lumyn"
dpkg-deb --root-owner-group --build "${PACKAGE_ROOT}" "${DEB_DIR}/lumyn_${VERSION}_amd64.deb"

echo "Linux artifacts:"
find artifacts -type f \( -name '*.deb' -o -name 'Lumyn' \) -print
