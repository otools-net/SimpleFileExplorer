#!/usr/bin/env bash
# Starts a built AppImage against a small temporary library and checks that it really works: the setup of the first
# administrator (with a setup code), web client, browsing, an index scan into SQLite, search, and the data folder outside
# the (read-only) image.
#
#   build/linux/smoke-test.sh <SFE .AppImage> [port]
#
# Uses FUSE when it is available, else the AppImage's extract-and-run mode.
set -euo pipefail

if [ $# -lt 1 ]; then
    echo "usage: $0 <SFE .AppImage> [port]" >&2
    exit 2
fi

APPIMAGE_FILE="$(readlink -f "$1")"
PORT="${2:-5091}"
BASE="http://127.0.0.1:$PORT"

WORK="$(mktemp -d)"
PID=""
SETUP_CODE="smoke-test-setup-code"
# Keeps the sign-in cookie; requests changing something carry the header the API asks for.
api() { curl -fsS -b "$WORK/cookies" -c "$WORK/cookies" -H 'X-SFE-Request: 1' "$@"; }
cleanup() {
    if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then
        # The AppImage runtime forks the app: stop the whole process group.
        kill -TERM -- "-$PID" 2>/dev/null || true
        wait "$PID" 2>/dev/null || true
    fi
    # The app may still be shutting down after the runtime has gone: wait for the group before removing its files.
    if [ -n "$PID" ]; then
        for _ in $(seq 1 30); do
            kill -0 -- "-$PID" 2>/dev/null || break
            sleep 0.5
        done
        kill -KILL -- "-$PID" 2>/dev/null || true
    fi
    rm -rf "$WORK"
}
trap cleanup EXIT

fail() {
    echo "SMOKE TEST FAILED: $*" >&2
    echo "---- application log ----" >&2
    cat "$WORK/app.log" >&2 || true
    exit 1
}

LIBRARY="$WORK/library"
mkdir -p "$LIBRARY/Holidays 2024" "$LIBRARY/Música"
printf 'smoke' > "$LIBRARY/Holidays 2024/smoketest-beach.jpg"
printf 'smoke' > "$LIBRARY/Música/smoketest-song.mp3"
printf 'smoke' > "$LIBRARY/readme.txt"

if ! { [ -e /dev/fuse ] && { command -v fusermount3 || command -v fusermount; } > /dev/null; }; then
    export APPIMAGE_EXTRACT_AND_RUN=1
fi

echo "Starting $APPIMAGE_FILE on $BASE"
# Started from another folder on purpose: nothing may depend on the working directory.
(cd "$WORK" && SFE_HOME="$WORK/home" exec setsid "$APPIMAGE_FILE" --urls "$BASE" --Sfe:Root="$LIBRARY" --Sfe:Setup:Code="$SETUP_CODE" --Sfe:OpenBrowser=false) > "$WORK/app.log" 2>&1 &
PID=$!

for _ in $(seq 1 60); do
    kill -0 "$PID" 2>/dev/null || fail "the application exited during startup"
    curl -fsS "$BASE/api/setup" > "$WORK/setup.json" 2>/dev/null && break
    sleep 1
done
[ -s "$WORK/setup.json" ] || fail "no answer from $BASE/api/setup after 60 s"
grep -q '"required":true' "$WORK/setup.json" && grep -q '"codeConfigured":true' "$WORK/setup.json" \
    || fail "a new install should ask for its administrator with the setup code: $(cat "$WORK/setup.json")"
BEFORE="$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/config")"
[ "$BEFORE" = 503 ] || fail "before the setup the API should answer 503, not $BEFORE"
echo "  ok  setup required"

api -X POST -H 'Content-Type: application/json' \
    -d "{\"code\":\"$SETUP_CODE\",\"userName\":\"admin\",\"password\":\"smoke-test-password\"}" "$BASE/api/setup" > /dev/null \
    || fail "the setup failed"
api "$BASE/api/config" > "$WORK/config.json" || fail "/api/config fails after the setup"
grep -q '"userName":"admin"' "$WORK/config.json" || fail "the setup did not sign the administrator in"
echo "  ok  setup and /api/config"
# The package is named after the version the application reports (SFE-<version>-x86_64.AppImage).
REPORTED="$(sed -n 's/.*"version":"\([^"]*\)".*/\1/p' "$WORK/config.json")"
NAME="$(basename "$APPIMAGE_FILE")"
if [[ "$NAME" =~ ^SFE-(.+)-x86_64\.AppImage$ ]] && [ "$REPORTED" != "${BASH_REMATCH[1]}" ]; then
    fail "the application reports version '$REPORTED', the package is named '${BASH_REMATCH[1]}'"
fi
echo "  ok  version $REPORTED"

INDEX="$(curl -fsS "$BASE/")" || fail "the web client (/) is not served"
echo "$INDEX" | grep -q '<sfe-root' || fail "the web client (/) is not the Angular app"
SCRIPT="$(echo "$INDEX" | grep -o 'main-[A-Za-z0-9]*\.js' | head -n 1)"
[ -n "$SCRIPT" ] && curl -fsS "$BASE/$SCRIPT" > /dev/null || fail "static files (${SCRIPT:-main-*.js}) are not served"
echo "  ok  web client"

api "$BASE/api/browse?path=" | grep -q 'Holidays 2024' || fail "browsing the root doesn't list 'Holidays 2024'"
api "$BASE/api/browse?path=M%C3%BAsica" | grep -q 'smoketest-song' || fail "browsing a Unicode folder fails"
echo "  ok  browse"

api -X POST -H 'Content-Type: application/json' -d '{"path":""}' "$BASE/api/scan" > /dev/null \
    || fail "cannot start a scan"
for _ in $(seq 1 60); do
    STATUS="$(api "$BASE/api/scan")"
    case "$STATUS" in
        *'"state":"completed"'*) break ;;
        *'"state":"failed"'*|*'"state":"cancelled"'*) fail "scan ended badly: $STATUS" ;;
    esac
    sleep 1
done
case "$STATUS" in *'"state":"completed"'*) ;; *) fail "scan did not complete: $STATUS" ;; esac
echo "  ok  scan"

api "$BASE/api/search?q=smoketest" | grep -q 'smoketest-beach' || fail "search doesn't find the indexed file"
echo "  ok  search"

[ -f "$WORK/home/appsettings.json" ] || fail "appsettings.json was not created in the data folder"
[ -f "$WORK/home/data/sfe.db" ] || fail "the SQLite index is not in the data folder"
[ -d "$WORK/home/data/keys" ] || fail "the keys of the sign-in cookies are not in the data folder"
echo "  ok  data folder ($WORK/home)"

echo "Smoke test passed: $APPIMAGE_FILE"
