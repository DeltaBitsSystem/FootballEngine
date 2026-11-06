#!/bin/sh
# Rebuild the ProAthlete character pack end to end in isolated Blender processes.
# Usage (repository root): sh Tools/MatchViewer3D/ProAthlete/build_all.sh [first-step]
# Steps: body kit uv paint pack. FE_ASSET_ROOT selects the checkout that holds the staging folder.
set -e
HERE=$(dirname "$0")
STEP=${1:-body}
LOG=${TMPDIR:-/tmp}/proathlete-step.log
run() {
  # A failed stage must stop the chain: later stages would silently reuse stale inputs.
  if ! blender -b --python-exit-code 1 --python "$HERE/$1" > "$LOG" 2>&1; then
    grep -E "Traceback|Error|File \"|assert" -A3 "$LOG" | head -20
    echo "FAILED $1"; exit 1
  fi
  grep -E "_BUILT" "$LOG" || true
}
started=0
for s in body kit uv paint pack; do
  [ "$s" = "$STEP" ] && started=1
  [ $started = 1 ] || continue
  case $s in
    body) run build_body.py ;;
    kit) run build_kit.py ;;
    uv) run build_uv.py ;;
    paint) python3 "$HERE/paint_atlas.py" ;;
    pack) run build_pack.py ;;
  esac
done
