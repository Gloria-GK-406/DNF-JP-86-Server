#!/bin/bash
# usage: PROBE_WORK=<dir> run.sh <round> <Project> [@filterfile | extra dotnet test args...]
# Runs one test project with the probe recorder pointed at $PROBE_WORK/<round>/<Project>, then classifies.
# Expects patch.py apply and a build of the test project first.
set -u
TOOLS=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "$TOOLS/../../../../.." && pwd)
HERE=${PROBE_WORK:?set PROBE_WORK to a directory outside the repository}
ROUND=$1; PROJ=$2; shift 2
EXTRA=("$@")
if [ "${1:-}" != "" ] && [ "${1:0:1}" = "@" ]; then
  EXTRA=(--filter "$(cat "${1:1}")")
fi
OUT=$HERE/$ROUND/$PROJ
rm -rf "$OUT"; mkdir -p "$OUT"
cd "$REPO/Server" || exit 2
GAMEPLAY_PROBE_OUT="$OUT" dotnet test "tests/Dfo.Server.$PROJ.Tests/Dfo.Server.$PROJ.Tests.csproj" -c Release --no-build \
  --logger "console;verbosity=detailed" ${EXTRA[@]+"${EXTRA[@]}"} > "$HERE/$ROUND/$PROJ.log" 2>&1
echo "exit $?"
tail -6 "$HERE/$ROUND/$PROJ.log" | head -5
python3 "$TOOLS/classify.py" "$OUT" --json "$HERE/$ROUND/$PROJ.json" --detail > "$HERE/$ROUND/$PROJ-class.txt"
head -5 "$HERE/$ROUND/$PROJ-class.txt"
python3 "$TOOLS/plan.py" "$HERE/$ROUND/$PROJ.json" "Dfo.Server.$PROJ.Tests" --json "$HERE/$ROUND/$PROJ-plan.json" | tail -1
