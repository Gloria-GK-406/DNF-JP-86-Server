#!/bin/bash
# Capture the quest-visible behaviour of this build: the entry batch, the two accepts,
# and the rows they leave. Run it once before the refactor and once after; the two
# archives are what RQ-017/018/019 are decided on.
#
# usage: capture.sh <label>        label lands in the archive file names
set -u
LABEL="${1:?usage: capture.sh <label>}"
SW='C:/Users/10239/AppData/Local/Temp/claude/d--project-DNF/7caec783-c820-4633-b9d0-e9fbf9114f94/scratchpad'
W='/d/project/DNF/DNF-JP-86-Server/.worktrees/a21-world-entry-protocol'
BIN="$W/Server/src/Dfo.Server.Host/bin/Debug/net10.0"
OUT="$W/.rks-agent/tasks/quest-log-domain/verification"
PVF='D:/project/DNF/DNFClient-A21-protocol/Script.pvf'
ACCT=10038
HASH=de509f65e9ccaae621cb7278fc2b8e6c

# The server is started with stdin closed and every other descriptor pointed at its log.
# A background process that keeps this script's stdout open makes any pipe reading the
# script wait for the server to exit, which it never does on its own.
boot() {   # boot <logfile>; leaves the pid in $PID
  rm -f "$BIN/Data/dfo-server.db" "$BIN/Data/dfo-server.db-wal" "$BIN/Data/dfo-server.db-shm"
  cd "$BIN" || exit 1
  NETWORKING__ENABLED=true Pvf__ArchivePath="$PVF" PVF_ARCHIVE_PATH="$PVF" \
    ./Dfo.Server.Host.exe > "$1" 2>&1 < /dev/null &
  PID=$!
  cd "$SW" || exit 1
  for _ in $(seq 1 60); do
    grep -q "Application started" "$1" 2>/dev/null && break
    sleep 1
  done
  grep -m1 "Static content archive" "$1"
}

halt() {
  [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null
  wait "$PID" 2>/dev/null
  sleep 2
}

cd "$SW" || exit 1

# ---- run A: the entry batch, on a character that has accepted nothing --------------
echo "=== run A: entry batch ==="
boot "$SW/capture-a-$LABEL.log"
python wireclient.py 10011 "$ACCT" "$HASH" Nagisa 0 > "$SW/capture-create-$LABEL.txt" 2>&1
tail -1 "$SW/capture-create-$LABEL.txt"
python entryprobe.py 10011 "$ACCT" "$HASH" 0 ours-entry.txt
cp ours-entry.txt "$OUT/entry-batch-$LABEL.txt"
python cmpentry.py > "$OUT/entry-compare-$LABEL.txt" 2>&1
head -2 "$OUT/entry-compare-$LABEL.txt"
python questrows.py "$BIN/Data/dfo-server.db" > "$OUT/quest-rows-after-entry-$LABEL.txt" 2>&1
halt

# ---- run B: the two accepts, from the same cold start ------------------------------
echo
echo "=== run B: quest acceptance ==="
boot "$SW/capture-b-$LABEL.log"
python wireclient.py 10011 "$ACCT" "$HASH" Nagisa 0 > /dev/null 2>&1
python questprobe.py 10011 0 > "$OUT/quest-accept-$LABEL.txt" 2>&1
RC=$?
tail -1 "$OUT/quest-accept-$LABEL.txt"
python questrows.py "$BIN/Data/dfo-server.db" > "$OUT/quest-rows-after-accept-$LABEL.txt" 2>&1
cat "$OUT/quest-rows-after-accept-$LABEL.txt"
halt

echo
echo "archived under $OUT (label: $LABEL); questprobe exit=$RC"
exit $RC
