#!/bin/bash
# usage: PROBE_WORK=<dir> runall.sh <round> [projects...]   (default: all five)
TOOLS=$(cd "$(dirname "$0")" && pwd)
ROUND=$1; shift
PROJECTS=${*:-"Application Persistence Protocol Host Networking"}
for p in $PROJECTS; do
  echo "== $p"
  bash "$TOOLS/run.sh" "$ROUND" "$p"
done
