---
name: terra-nil-debuggee
description: Launches Terra Nil, a Unity game from this machine's Steam library, as a second debuggee for the unity MCP server. Use when a unity-devtools change needs a live game and Cities Skylines II is unavailable or too slow to launch, or when a report comes from a release Mono build (BepInEx, Doorstop, a game that is not CS2) and the fixture cannot reproduce its shape.
---

# Terra Nil as a debuggee

A release Mono player on native Linux, its debugger agent enabled by Doorstop: the shape games of the BepInEx community run under, and the one CS2 under Proton is not.

## 1. Launch

Run `.agents/skills/terra-nil-debuggee/launch.sh [port]` (default port 56000). Done when it prints the port it listens on; the game's output is in the path it names.

The game runs outside Steam, so its Steam API init fails in the log and it carries on to the menu. Launching is acting on the user's machine: ask first unless they already said to.

## 2. Attach

A release build advertises no beacon, so `status` shows none: call `attach` with the port, and again after any drop, since a reattach goes back to the beacon.

## 3. What differs from CS2

- No Entities world: the `ecs_*` tools and `em`/`world` roots have nothing to answer. `eval` on `UnityEngine.*` and the game's own types works.
- No debug symbols: a breakpoint takes a method, never a line, and frames show no source positions.
- The main thread is unnamed and several unnamed workers sit beside it; a game-named main thread is the fixture's shape, not this one.

## 4. Stop

Run `.agents/skills/terra-nil-debuggee/launch.sh stop` when done: the game is the user's, and a left-over instance holds the debugger port the next launch wants.
