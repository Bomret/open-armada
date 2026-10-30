#!/bin/sh
# Runs a play script under Movie Maker, from the repository root, and reuses the stored result when its inputs
# are unchanged:
#   sh tools/play-run.sh <stem> (--layout <file> | --map <map>) --fps <n> [--fresh]
# The input hash covers the content and path of every tracked or untracked, not ignored file under src/ and
# scenes/, the project files, the play script, the layout's content, the map name, the fps, the Godot version
# and the ARMADA_DIR path. A result is stored in play-out/.cache/<hash>/ only for exit status 0 or 1. Each call
# past the argument checks appends one line to play-out/runs.log. Bad arguments exit 2 and leave no trace.
# Calls for one stem run one at a time through the lock play-out/.lock-<stem>, which holds the owner's pid: a
# call waits on a live owner, says so once on stderr, and takes over the lock of a dead one under the mutex
# play-out/.lock-<stem>.takeover. An unwritable play-out/ exits 2. PLAY_RUN_GODOT replaces the Godot binary (default godot-mono).
set -u

usage() {
  echo "play-run.sh: $1" >&2
  echo "usage: sh tools/play-run.sh <stem> (--layout <file> | --map <map>) --fps <n> [--fresh]" >&2
  exit 2
}

[ $# -ge 1 ] || usage "missing stem"
stem=$1
shift
case "$stem" in
  "" | */* | *..* | -*) usage "bad stem: $stem" ;;
esac

layout=
map=
fps=
fresh=
while [ $# -gt 0 ]; do
  case "$1" in
    --layout | --map | --fps)
      [ $# -ge 2 ] || usage "$1 needs a value"
      case "$1" in
        --layout) layout=$2 ;;
        --map) map=$2 ;;
        --fps) fps=$2 ;;
      esac
      shift 2
      ;;
    --fresh)
      fresh=1
      shift
      ;;
    *) usage "unknown argument: $1" ;;
  esac
done

if [ -n "$layout" ] && [ -n "$map" ]; then
  usage "give --layout or --map, not both"
fi
[ -n "$layout" ] || [ -n "$map" ] || usage "give --layout or --map"
case "$fps" in
  "" | 0* | *[!0-9]*) usage "--fps needs a positive whole number" ;;
esac
script=play/$stem.play
[ -f "$script" ] || usage "no play script $script"
if [ -n "$layout" ] && [ ! -f "$layout" ]; then
  usage "no layout $layout"
fi
[ -n "${ARMADA_DIR:-}" ] || usage "set ARMADA_DIR to an Armada install"
godot=${PLAY_RUN_GODOT:-godot-mono}
version=$("$godot" --version 2>/dev/null) || usage "cannot read the version of $godot"

# One call per stem at a time, locked before the hash so a waiting call hashes the inputs it runs on. The lock
# holds the owner's pid; a lock whose pid is dead is taken over.
lock=play-out/.lock-$stem
waited=
mkdir -p play-out 2>/dev/null
until mkdir "$lock" 2>/dev/null; do
  if [ ! -d play-out ] || [ ! -w play-out ]; then
    echo "play-run.sh: cannot write play-out/" >&2
    exit 2
  fi
  pid=$(cat "$lock/pid" 2>/dev/null)
  if [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null; then
    # Only the holder of the takeover mutex removes a lock, after reading its pid again while holding it. No
    # other call can create the lock path while the stale lock still stands there.
    if mkdir "$lock.takeover" 2>/dev/null; then
      pid=$(cat "$lock/pid" 2>/dev/null)
      if [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null; then rm -rf "$lock"; fi
      rmdir "$lock.takeover"
      continue
    fi
  fi
  if [ -z "$waited" ]; then
    echo "play-run.sh: waiting on $lock (remove it if no run is live)" >&2
    waited=1
  fi
  sleep 1
done
trap 'rm -rf "$lock"' EXIT
trap 'exit 130' INT TERM HUP
echo $$ >"$lock/pid"

export LC_ALL=C
hash=$(
  {
    list=$(mktemp)
    git ls-files --cached --others --exclude-standard -- src scenes | sort -u | while IFS= read -r f; do
      [ -f "$f" ] && printf '%s\n' "$f"
    done >"$list"
    [ -s "$list" ] && git hash-object --stdin-paths <"$list" | paste -d ' ' "$list" -
    rm -f "$list"
    for f in project.godot OpenArmada.csproj global.json; do
      if [ -f "$f" ]; then echo "$f $(git hash-object -- "$f")"; else echo "$f missing"; fi
    done
    echo "script $(git hash-object -- "$script")"
    if [ -n "$layout" ]; then echo "layout $(git hash-object -- "$layout")"; else echo "map $map"; fi
    echo "fps $fps"
    echo "godot $version"
    echo "armada $ARMADA_DIR"
  } | git hash-object --stdin
)

out=play-out/$stem
cache=play-out/.cache
entry=$cache/$hash
if [ -n "$layout" ]; then target="layout=$layout"; else target="map=$map"; fi

rm -rf "$out"
mkdir -p "$out"
if [ -z "$fresh" ] && [ -f "$entry/status" ]; then
  how=reused
  cp -R "$entry/out/." "$out/"
  echo "play-run.sh: reused $hash"
  cat "$entry/summary"
  status=$(cat "$entry/status")
else
  how=run
  log=$(mktemp)
  if [ -n "$layout" ]; then
    "$godot" --path . --write-movie "$out/frame.png" --fixed-fps "$fps" -- --game "$ARMADA_DIR" --layout "$layout" --play "$script" >"$log" 2>&1
  else
    "$godot" --path . --write-movie "$out/frame.png" --fixed-fps "$fps" -- --game "$ARMADA_DIR" --map "$map" --play "$script" >"$log" 2>&1
  fi
  status=$?
  cat "$log"
  if [ "$status" -le 1 ]; then
    mkdir -p "$cache"
    tmp=$(mktemp -d "$cache/tmp.XXXXXX")
    mkdir "$tmp/out"
    for f in "$out"/*; do
      [ -e "$f" ] || continue
      case "${f##*/}" in
        frame[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9].png) ;;
        *) cp -R "$f" "$tmp/out/" ;;
      esac
    done
    grep -E 'WARN|ERROR' "$log" >"$tmp/summary"
    echo "$status" >"$tmp/status"
    # Move a replaced entry aside first so the rename never lands inside an existing entry.
    [ -d "$entry" ] && mv "$entry" "$tmp.old"
    mv "$tmp" "$entry" && rm -rf "$tmp.old"
  fi
  rm -f "$log"
fi

head=$(git rev-parse HEAD 2>/dev/null || echo none)
if [ -n "$(git status --porcelain)" ]; then dirty=dirty; else dirty=clean; fi
printf '%s\t%s\t%s\t%s\t%s\t%s\tfps=%s\texit=%s\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$hash" "$head" "$dirty" \
  "$stem" "$target" "$fps" "$status" "$how" >>play-out/runs.log
exit "$status"
