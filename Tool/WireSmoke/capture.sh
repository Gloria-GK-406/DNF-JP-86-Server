#!/bin/bash
# Walk this server's entry and quest-acceptance path over a real socket, and record what
# came back.
#
# What it is for: the unit suite never touches the wire, so a change that keeps every test
# green can still send the client different bytes. This boots the host twice from a wiped
# database, drives it with a line-level client, and archives the frames and the rows. Run it
# before a change and after, and diff the two archives; run it alone and compare the entry
# batch against the reference session.
#
#   capture.sh <label> [outdir]
#
# <label> lands in the archive file names. [outdir] defaults to ./capture-<label> under the
# current directory. Working files go to a temp directory and are removed, so the archive
# holds results and nothing else.
set -u

LABEL="${1:?usage: capture.sh <label> [outdir]}"
OUT="$(cd "$(dirname "${2:-.}")" 2>/dev/null && pwd)/$(basename "${2:-capture-$LABEL}")"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
BIN="$REPO/Server/src/Dfo.Server.Host/bin/Debug/net10.0"
PVF="${PVF_ARCHIVE_PATH:-D:/project/DNF/DNFClient-A21-protocol/Script.pvf}"
ACCT="${SMOKE_ACCOUNT:-10038}"
HASH="${SMOKE_PWHASH:-de509f65e9ccaae621cb7278fc2b8e6c}"
NAME="${SMOKE_CHARACTER:-Nagisa}"
JOB="${SMOKE_JOB:-0}"

WORK="$(mktemp -d 2>/dev/null || echo "${TMPDIR:-/tmp}/wiresmoke-$$")"
mkdir -p "$WORK" "$OUT" || exit 1
# Declared before the trap: an early exit runs `halt`, and `set -u` would trip on it.
PID=""
trap 'halt; rm -rf "$WORK"' EXIT

[ -x "$BIN/Dfo.Server.Host.exe" ] || {
  echo "no host build at $BIN -- run: dotnet build $REPO/Server/86JP.Server.slnx" >&2
  exit 2
}
[ -f "$PVF" ] || { echo "no PVF archive at $PVF (set PVF_ARCHIVE_PATH)" >&2; exit 2; }

# The host is started with stdin closed and everything else pointed at its log. A background
# process that keeps this script's stdout open makes any pipe reading the script wait for the
# server to exit, which it never does on its own.
boot() {   # boot <logfile>
  rm -f "$BIN/Data/dfo-server.db" "$BIN/Data/dfo-server.db-wal" "$BIN/Data/dfo-server.db-shm"
  cd "$BIN" || exit 1
  NETWORKING__ENABLED=true Pvf__ArchivePath="$PVF" PVF_ARCHIVE_PATH="$PVF" \
    ./Dfo.Server.Host.exe > "$1" 2>&1 < /dev/null &
  PID=$!
  cd "$WORK" || exit 1
  for _ in $(seq 1 60); do
    grep -q "Application started" "$1" 2>/dev/null && break
    sleep 1
  done
  grep -q "Application started" "$1" || { echo "host did not start; see $1" >&2; exit 3; }
  grep -m1 "Static content archive" "$1"
}

halt() {
  [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null
  wait "$PID" 2>/dev/null
  PID=""
}

cd "$WORK" || exit 1
# questprobe imports entryprobe, so the tool directory has to be importable -- and must not
# collect a __pycache__ for the trouble, since it lives in the repository.
export PYTHONPATH="$HERE"
export PYTHONDONTWRITEBYTECODE=1
py() { python "$HERE/$1" "${@:2}"; }

# ---- run A: the entry batch, on a character that has accepted nothing ------------------
echo "=== run A: entry batch ==="
boot "$WORK/host-a.log"
py wireclient.py 10011 "$ACCT" "$HASH" "$NAME" "$JOB" > "$WORK/create.txt" 2>&1
tail -1 "$WORK/create.txt"
py entryprobe.py 10011 "$ACCT" "$HASH" 0 "$OUT/entry-batch-$LABEL.txt"
py cmpentry2.py "$OUT/entry-batch-$LABEL.txt" "$OUT/entry-compare-$LABEL.txt"
tail -3 "$OUT/entry-compare-$LABEL.txt"
py questrows.py "$BIN/Data/dfo-server.db" > "$OUT/quest-rows-after-entry-$LABEL.txt" 2>&1
halt

# ---- run B: the two accepts, from the same cold start ----------------------------------
echo
echo "=== run B: quest acceptance ==="
boot "$WORK/host-b.log"
py wireclient.py 10011 "$ACCT" "$HASH" "$NAME" "$JOB" > /dev/null 2>&1
py questprobe.py 10011 0 > "$OUT/quest-accept-$LABEL.txt" 2>&1
RC=$?
tail -1 "$OUT/quest-accept-$LABEL.txt"
py questrows.py "$BIN/Data/dfo-server.db" > "$OUT/quest-rows-after-accept-$LABEL.txt" 2>&1
cat "$OUT/quest-rows-after-accept-$LABEL.txt"
halt

echo
echo "archived under $OUT (label: $LABEL); questprobe exit=$RC"
exit $RC
