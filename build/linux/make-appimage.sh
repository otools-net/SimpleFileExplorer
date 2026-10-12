#!/usr/bin/env bash
# Packs a linux-x64 portable publish (dotnet publish -r linux-x64 -p:SfePortable=true) into an AppImage.
#
#   build/linux/make-appimage.sh <publish dir> <output .AppImage> [version]
#
# appimagetool and the AppImage runtime are downloaded once (pinned releases) into $SFE_TOOLS_DIR
# (default: artifacts/tools). For an offline build, put them there or point APPIMAGETOOL / APPIMAGE_RUNTIME at
# local copies. Runs on Linux, including WSL (build/build.ps1 calls it from Windows).
set -euo pipefail

APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
RUNTIME_URL="https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64"

if [ $# -lt 2 ]; then
    echo "usage: $0 <publish dir> <output .AppImage> [version]" >&2
    exit 2
fi

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
PUBLISH="$(cd "$1" && pwd)"
OUTPUT="$2"
VERSION="${3:-}"
TOOLS="${SFE_TOOLS_DIR:-$REPO/artifacts/tools}"

[ -f "$PUBLISH/Sfe.Api" ] || { echo "error: $PUBLISH/Sfe.Api not found" >&2; exit 1; }

fetch() { # <url> <file>
    if [ ! -s "$2" ]; then
        echo "Downloading $1"
        mkdir -p "$(dirname "$2")"
        curl -fsSL --retry 3 -o "$2.part" "$1"
        mv "$2.part" "$2"
    fi
    chmod +x "$2"
}

APPIMAGETOOL="${APPIMAGETOOL:-$TOOLS/appimagetool-1.9.1-x86_64.AppImage}"
APPIMAGE_RUNTIME="${APPIMAGE_RUNTIME:-$TOOLS/runtime-20251108-x86_64}"
[ -f "$APPIMAGETOOL" ] || fetch "$APPIMAGETOOL_URL" "$APPIMAGETOOL"
[ -f "$APPIMAGE_RUNTIME" ] || fetch "$RUNTIME_URL" "$APPIMAGE_RUNTIME"
chmod +x "$APPIMAGETOOL"

# The AppDir is assembled on a Linux file system: files coming from a Windows drive (/mnt/c under WSL) would all be
# executable and the image would keep that.
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
APPDIR="$WORK/SFE.AppDir"
mkdir -p "$APPDIR/usr/lib/sfe"
cp -r "$PUBLISH/." "$APPDIR/usr/lib/sfe/"
find "$APPDIR" -type d -exec chmod 755 {} +
find "$APPDIR" -type f -exec chmod 644 {} +
chmod 755 "$APPDIR/usr/lib/sfe/Sfe.Api"

# Unix line endings whatever the checkout did: a CRLF shebang line doesn't run.
tr -d '\r' < "$HERE/AppRun" > "$APPDIR/AppRun"
tr -d '\r' < "$HERE/sfe.desktop" > "$APPDIR/sfe.desktop"
chmod 755 "$APPDIR/AppRun"
cp "$REPO/src/Sfe.Web/public/favicon.svg" "$APPDIR/sfe.svg"
ln -s sfe.svg "$APPDIR/.DirIcon"

mkdir -p "$(dirname "$OUTPUT")"
rm -f "$OUTPUT"
# Without FUSE (containers, some WSL setups) appimagetool can still run by extracting itself.
export APPIMAGE_EXTRACT_AND_RUN=1
ARCH=x86_64 VERSION="$VERSION" "$APPIMAGETOOL" --no-appstream --runtime-file "$APPIMAGE_RUNTIME" "$APPDIR" "$OUTPUT"
chmod +x "$OUTPUT"
echo "AppImage: $OUTPUT"
