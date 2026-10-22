#!/usr/bin/env bash
# Builds the portable packages of SFE locally on Linux and smoke-tests them (on Windows, use build/build.ps1).
#
#   build/build.sh [--target linux|windows|all] [--version V] [--skip-tests] [--skip-smoke-test] [--no-tests]
#
#   --no-tests  no tests at all: both --skip-tests and --skip-smoke-test
#
#   linux    artifacts/SFE-<version>-x86_64.AppImage (default target)
#   windows  artifacts/SFE-<version>-win-x64.zip (cross-published; needs `zip`; not smoke-tested here)
#
# Needs the .NET 10 SDK, Node.js (npm builds the web client) and curl (appimagetool is downloaded once into artifacts/tools).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
ARTIFACTS="$REPO/artifacts"
PROJECT="$REPO/src/Sfe.Api/Sfe.Api.csproj"

TARGET=linux
VERSION=""
SKIP_TESTS=0
SKIP_SMOKE=0
while [ $# -gt 0 ]; do
    case "$1" in
        --target) TARGET="$2"; shift 2 ;;
        --version) VERSION="$2"; shift 2 ;;
        --skip-tests) SKIP_TESTS=1; shift ;;
        --skip-smoke-test) SKIP_SMOKE=1; shift ;;
        --no-tests) SKIP_TESTS=1; SKIP_SMOKE=1; shift ;;
        -h|--help) sed -n '2,11p' "$0"; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done
case "$TARGET" in linux|windows|all) ;; *) echo "--target must be linux, windows or all" >&2; exit 2 ;; esac
command -v npm > /dev/null || { echo "error: building the web client (src/Sfe.Web) needs Node.js (npm) on the PATH" >&2; exit 1; }

# Defaults to the version in Directory.Build.props (also the version the app reports).
[ -n "$VERSION" ] || VERSION="$(dotnet msbuild "$PROJECT" -getProperty:Version | tr -d '\r')"
# Part of the file names: a semantic version (1.2.0, 1.3.0-beta.1), nothing else.
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "invalid version '$VERSION': expected major.minor.patch[-prerelease]" >&2
    exit 2
fi
echo "SFE portable build $VERSION ($TARGET)"
mkdir -p "$ARTIFACTS"

publish() { # <rid> -> prints the output folder
    local out="$ARTIFACTS/publish/$1"
    rm -rf "$out"
    echo "==> Publish $1" >&2
    dotnet publish "$PROJECT" -c Release -r "$1" -p:SfePortable=true "-p:Version=$VERSION" -o "$out" --nologo >&2
    echo "$out"
}

if [ "$SKIP_TESTS" = 0 ]; then
    echo "==> Tests"
    dotnet test "$REPO/Sfe.slnx" -c Release --nologo
    npm --prefix "$REPO/src/Sfe.Web" test
fi

RESULTS=()

if [ "$TARGET" = linux ] || [ "$TARGET" = all ]; then
    PUBLISH="$(publish linux-x64)"
    APPIMAGE="$ARTIFACTS/SFE-$VERSION-x86_64.AppImage"
    echo "==> AppImage"
    "$REPO/build/linux/make-appimage.sh" "$PUBLISH" "$APPIMAGE" "$VERSION"
    RESULTS+=("$APPIMAGE")
    if [ "$SKIP_SMOKE" = 0 ]; then
        echo "==> Smoke test AppImage"
        "$REPO/build/linux/smoke-test.sh" "$APPIMAGE"
    fi
fi

if [ "$TARGET" = windows ] || [ "$TARGET" = all ]; then
    command -v zip > /dev/null || { echo "error: the Windows package needs 'zip'" >&2; exit 1; }
    PUBLISH="$(publish win-x64)"
    ZIP="$ARTIFACTS/SFE-$VERSION-win-x64.zip"
    echo "==> Zip win-x64"
    # The zip holds an "SFE" folder rather than loose files.
    STAGING="$ARTIFACTS/package"
    rm -rf "$STAGING" "$ZIP"
    mkdir -p "$STAGING"
    cp -r "$PUBLISH" "$STAGING/SFE"
    (cd "$STAGING" && zip -qr "$ZIP" SFE)
    RESULTS+=("$ZIP")
    echo "(the Windows package is smoke-tested by build/build.ps1 on Windows)"
fi

echo
echo "Packages:"
printf '  %s\n' "${RESULTS[@]}"
