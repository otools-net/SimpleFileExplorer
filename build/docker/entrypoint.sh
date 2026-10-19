#!/bin/sh
# Entry point of the SFE container. The program and its web files are in /app (read-only for the app's user);
# settings, keys and the setup code live in the data folder ($SFE_DATA, default /data, a volume), which is SFE's content
# root: its appsettings.json is made there on the first start, from the one the package ships.
#
# Where the bulky or busy parts go, each optional (default: below the data folder):
#   SFE_DATABASE_DIR  the SQLite databases (sfe.db, and tenants/ for the other spaces)
#   SFE_CACHE_DIR     thumbnails, trickplay, video fingerprints, the other spaces' caches
#   SFE_LOGS_DIR      the log files
# SFE's own variables win when given too (ConnectionStrings__Sfe, Sfe__Thumbnails__CacheDirectory, Sfe__Logs__Directory).
set -e

DATA="${SFE_DATA:-/data}"

# The folder must exist and be writable by the app's user: a volume mounted where the image has no folder is root's.
writable() {
    mkdir -p "$1" 2>/dev/null || true
    if [ ! -d "$1" ] || [ ! -w "$1" ]; then
        echo "SFE: $2 $1 is not writable by the container's user (uid $(id -u)): give it that owner, or run the container as its owner (--user)." >&2
        exit 1
    fi
}

writable "$DATA" "the data folder"

if [ -n "${SFE_DATABASE_DIR:-}" ] && [ -z "${ConnectionStrings__Sfe:-}" ]; then
    writable "$SFE_DATABASE_DIR" "the database folder"
    export ConnectionStrings__Sfe="Data Source=$SFE_DATABASE_DIR/sfe.db"
fi
if [ -n "${SFE_CACHE_DIR:-}" ] && [ -z "${Sfe__Thumbnails__CacheDirectory:-}" ]; then
    writable "$SFE_CACHE_DIR" "the cache folder"
    export Sfe__Thumbnails__CacheDirectory="$SFE_CACHE_DIR"
fi
if [ -n "${SFE_LOGS_DIR:-}" ] && [ -z "${Sfe__Logs__Directory:-}" ]; then
    writable "$SFE_LOGS_DIR" "the logs folder"
    export Sfe__Logs__Directory="$SFE_LOGS_DIR"
fi

if [ ! -f "$DATA/appsettings.json" ]; then
    cp /app/appsettings.json "$DATA/appsettings.json"
    echo "SFE: created $DATA/appsettings.json (the environment's Sfe__… variables override it)" >&2
fi

exec /app/Sfe.Api --contentRoot "$DATA" "$@"
