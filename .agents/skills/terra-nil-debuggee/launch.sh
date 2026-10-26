#!/usr/bin/env bash
# Launches Terra Nil with its Mono debugger agent listening, or stops it.
#
#   launch.sh [port]   start the game, debugger on 127.0.0.1:<port> (default 56000); prints the port
#                      once it listens, and leaves the game running.
#   launch.sh stop     stop the game this script started.
#
# The agent is enabled by Doorstop, preloaded from a copy this script downloads into
# .scratch/terra-nil/ when missing (pinned by hash): the game folder is never written to. The
# game's output goes to .scratch/terra-nil/game.log. TERRA_NIL_DIR overrides the Steam location.
set -euo pipefail

repo="$(cd "$(dirname "$0")/../../.." && pwd)"
scratch="$repo/.scratch/terra-nil"
game="${TERRA_NIL_DIR:-$HOME/.local/share/Steam/steamapps/common/Terra Nil}"
pidfile="$scratch/game.pid"

doorstop_version=4.6.0
doorstop_sha256=63a8a2aeb368c401e10ceb161b1e0a67c6e8307ca06ebee1303a23907b2dd798
doorstop_dir="$scratch/doorstop-$doorstop_version"

# True when the pidfile names a live Terra Nil: a stale pidfile outlives a crash or a menu quit,
# and after a reboot its number can belong to any process of the user's.
started_game_runs() {
  [[ -f "$pidfile" ]] && tr '\0' ' ' 2>/dev/null < "/proc/$(cat "$pidfile")/cmdline" |
    grep -q 'Terra Nil'
}

if [[ "${1:-}" == stop ]]; then
  if started_game_runs && kill "$(cat "$pidfile")"; then
    echo "stopped Terra Nil (pid $(cat "$pidfile"))"
  else
    echo "no Terra Nil started by this script is running"
  fi
  rm -f "$pidfile"
  exit 0
fi

port="${1:-56000}"

if [[ ! -x "$game/Terra Nil.x86_64" ]]; then
  echo "Terra Nil not found at '$game' (install it through Steam, or set TERRA_NIL_DIR)" >&2
  exit 1
fi

if started_game_runs; then
  echo "Terra Nil already running (pid $(cat "$pidfile")); stop it first: $0 stop" >&2
  exit 1
fi

mkdir -p "$scratch"

if [[ ! -f "$doorstop_dir/libdoorstop.so" ]]; then
  zip="$scratch/doorstop_linux_release_$doorstop_version.zip"
  release="https://github.com/NeighTools/UnityDoorstop/releases/download/v$doorstop_version"
  curl -fsSL -o "$zip" "$release/doorstop_linux_release_$doorstop_version.zip"
  echo "$doorstop_sha256  $zip" | sha256sum -c --quiet
  mkdir -p "$doorstop_dir"
  unzip -o -q -j "$zip" 'x64/libdoorstop.so' -d "$doorstop_dir"
  rm -f "$zip"
fi

export DOORSTOP_ENABLED=1
# Doorstop wants a managed entry point; none exists, which it logs and carries on.
export DOORSTOP_TARGET_ASSEMBLY="$doorstop_dir/NoEntrypoint.dll"
export DOORSTOP_IGNORE_DISABLED_ENV=0
export DOORSTOP_MONO_DEBUG_ENABLED=1
export DOORSTOP_MONO_DEBUG_ADDRESS="127.0.0.1:$port"
export DOORSTOP_MONO_DEBUG_SUSPEND=0
export LD_LIBRARY_PATH="$doorstop_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export LD_PRELOAD="libdoorstop.so${LD_PRELOAD:+:$LD_PRELOAD}"

cd "$game"
setsid "./Terra Nil.x86_64" > "$scratch/game.log" 2>&1 < /dev/null &
echo $! > "$pidfile"

for _ in $(seq 1 60); do
  if ! kill -0 "$(cat "$pidfile")" 2>/dev/null; then
    echo "Terra Nil exited before opening port $port; see $scratch/game.log" >&2
    rm -f "$pidfile"
    exit 1
  fi

  if ss -ltn | grep -q ":$port "; then
    echo "Terra Nil listening for the debugger on 127.0.0.1:$port" \
      "(pid $(cat "$pidfile"), log $scratch/game.log)"
    exit 0
  fi
  sleep 1
done

echo "Terra Nil did not open port $port within 60s; see $scratch/game.log" >&2
exit 1
