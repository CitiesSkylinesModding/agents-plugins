<div align="center">

# 🔬 unity-devtools

**Give your agent a live line into a running Unity Mono development build:
reflect types, evaluate C# expressions, and read & write ECS state, with no code injection.**

Generic tooling: works with **any** dev-Mono Unity game exposing the Mono Soft Debugger (SDB)
agent.

[![nuget](https://img.shields.io/nuget/v/UnityDevtools.Mcp?label=nuget)](https://www.nuget.org/packages/UnityDevtools.Mcp)
[![dotnet](https://img.shields.io/badge/.NET-10-blueviolet)](#requirements)
[![platform](https://img.shields.io/badge/platform-Windows-lightgrey)](#requirements)
[![license](https://img.shields.io/badge/license-MIT-blue)](../../LICENSE)

[Install](#install) · [See it in action](#what-it-looks-like-in-practice) ·
[Tools](#tools) · [Configuration](#configuration)

</div>

---

The plugin ships **unity-devtools-mcp**, a C# MCP server that attaches to the game's **Mono Soft
Debugger** agent, the same channel an IDE debugger uses, and drives the game from the outside:
no mod, no DLL, no game modification. Through live mirror invokes it resolves types, calls
methods, and queries, reads, and writes Unity **ECS** entities, components, and dynamic
buffers, in the running simulation.

It also ships the **`unity-driving` skill**: the operating manual for the tools, field-verified
procedures for the session lifecycle, suspend windows, entity identity, and the traps the tool
schemas cannot tell.

> [!NOTE]
> **Generic, developed against one reference game.** The server makes no assumptions about a
> specific game: it listens for the PlayerConnection beacon any Unity player multicasts and reads
> the debugger endpoint straight off it. It is developed and verified against one development Mono
> build as the reference target.

> [!WARNING]
> **Writes are live.** Component and buffer writes, and method invokes, mutate the running
> simulation immediately and persist; there is no undo. Test against a throwaway save.

## Table of contents

- [What it looks like in practice](#what-it-looks-like-in-practice)
- [Tools](#tools)
- [How it works](#how-it-works)
- [Requirements](#requirements)
- [Install](#install)
- [Configuration](#configuration)
- [Troubleshooting](#troubleshooting)

---

## What it looks like in practice

Short, abridged transcripts, replayed from real live sessions against Cities: Skylines II.

### "Rename that school, live"

> **You:** Rename one of my high schools after you, go ahead.
>
> **Agent:** `ecs_query` on `Game.Buildings.School` with
> `label: Game.UI.NameSystem:GetRenderedLabelName` lists the six schools with their in-game names:
> "Winchelsea", "Leechburg", "Fowey"… Then one `eval` calls the game's own name system:
> `world.GetExistingSystemManaged(typeof(Game.UI.NameSystem))` with `.SetCustomName(entity, name)`.
> Reading the label back returns `"Claude High School"`, and it is on the map.

### "We need money"

> **You:** Top the city treasury up to a round 100 million.
>
> **Agent:** `find_types` on `Game.City.PlayerMoney` shows a tidy component: `m_Money: Int32`
> plus `Add`/`Subtract`. `ecs_query` finds exactly one entity carrying it, the city singleton.
> One `ecs_set_component` on `m_Money` does it, and the result carries the proof in a single
> call: `before: m_Money=61343907, after: m_Money=100000000`.

### "Fast-forward, and skip to the evening"

> **You:** Unpause at max speed and jump the clock six hours; I want to see the evening commute.
>
> **Agent:** `eval` grabs the simulation system via
> `world.GetExistingSystemManaged(typeof(Game.Simulation.SimulationSystem))`: its `selectedSpeed`
> reads `0`, the city is paused. Assigning `selectedSpeed = 4` unpauses it, and a beat later
> `smoothSpeed` reads `1.83`: that is the eased, actual speed caught mid-climb, so the simulation is
> genuinely running, not just flagged. Then `Game.Simulation.TimeSystem.DebugAdvanceTime` jumps the
> clock six hours: `normalizedTime` moves from `0.42` (mid-morning) to `0.67` (early evening).
> Evening rush incoming.

## Tools

Bare names for the generic Unity tools, an `ecs_*` prefix for the ECS layer:

| Tool | What it does |
| --- | --- |
| `status` | What the game advertises on the beacon, the endpoint an attach would use, session state (no attach). |
| `attach` | Attach to a debugger port on this machine, for a game the beacon does not describe. |
| `suspend` / `resume` | Hold the game frozen across calls: a consistency window for multi-step edits. |
| `detach` | Free the exclusive debugger slot (e.g. for your IDE); reattach is automatic. |
| `find_types` | Resolve a type live by name, or search every loaded type by regex; optionally list its members. |
| `eval` | Evaluate a C# statement sequence against the live game, like an IDE debugger would. |
| `ecs_query` | Count/list entities having ALL given components, optionally labeled via a system call. |
| `ecs_get_component` / `ecs_set_component` | Read, or field-write with read-back, one entity's component. |
| `ecs_get_buffer` / `ecs_buffer_edit` | Read, append to, or remove from a `DynamicBuffer`. |

No attach step is needed: the first tool that needs the VM attaches on its own, the session
persists, and a dropped connection (or game restart) resolves the endpoint from the beacon again on
the next call. The `attach` tool is there for the cases the beacon cannot cover.

## How it works

- **Beacon discovery**: a Unity player multicasts a PlayerConnection target-info beacon, and the
  server derives the debugger endpoint from what it advertises. Nothing to configure, no port to
  guess, and a game restart just moves the beacon.
- **Mono Soft Debugger protocol**, the wire protocol behind "Attach to Unity" in your IDE. The
  server embeds Unity's own `Mono.Debugger.Soft` client and talks to the game's SDB agent
  directly.
- **Client-side C# evaluation**: SDB has no expression-evaluation command, so `eval` parses your
  C# with Roslyn (parse-only) and interprets it as a sequence of mirror primitives (member reads,
  property getters, invokes, indexers), the way IDE debuggers evaluate watch expressions.
- **The game keeps running** between calls; each operation opens a brief suspend window around
  itself (invokes need a suspended VM). The `suspend`/`resume` tools hold a window across calls
  when several reads/writes must see one consistent state; the game is fully frozen meanwhile.
- **Always resumed**: every path (failure, detach, server shutdown, even a killed connection)
  resumes the game; a closed socket auto-resumes the VM as the last-resort safety net.
- **One debugger at a time**: while the session is attached, an IDE debugger cannot attach to the
  game, and vice versa; `detach` frees the slot.

## Requirements

- **A Unity game running as a development Mono build**, launched with `player-connection-debug=1`
  so its SDB agent is live; a retail build exposes no SDB port and cannot be driven.
- **Windows.** Discovery itself is platform-agnostic, but the plugin is verified on Windows only
  and its server-lifetime watchdogs are Windows-only.
- **The .NET 10 SDK** to launch the server. No build step: the plugin launches the
  [`UnityDevtools.Mcp`](https://www.nuget.org/packages/UnityDevtools.Mcp)
  NuGet dotnet tool through `dotnet dnx`, version-pinned to the plugin (downloaded on first
  launch, cached after).

## Install

Add the marketplace, then install the plugin (see the
[repository README](../../README.md#install) for the marketplace overview).

**Claude Code:**

```
/plugin marketplace add CitiesSkylinesModding/agents-plugins
/plugin install unity-devtools@csmodding
```

Once enabled, Claude Code autoloads the `unity` MCP server from the plugin's
[`.mcp.json`](.mcp.json).

**Codex CLI:**

```sh
codex plugin marketplace add CitiesSkylinesModding/agents-plugins
codex plugin add unity-devtools@csmodding
```

Once enabled, Codex autoloads the `unity` MCP server from
[`.codex-plugin/mcp.json`](.codex-plugin/mcp.json).

Either way, run `/mcp` to confirm it connected, then ask the agent to call `status` to check the
MCP is working properly (with the game running).

## Configuration

There is none: no environment variables, no settings file, and the same behaviour on Claude Code and
Codex CLI. The server finds the game by listening for its PlayerConnection beacon.

For a game the beacon cannot describe — multicast filtered by a firewall or a VPN interface, or a
debug server started by an external loader on a port of its own — the `attach` tool takes a debugger
port on this machine. That port applies to that attach alone; a later reattach goes back to the
beacon, so pass it again for as long as it is still needed.

## Troubleshooting

- **"no Unity game is advertising itself"**: the game is not running, is not a development Mono
  build (no SDB agent), or its multicast is not reaching the server. Run `status`: with the game
  visibly running it is the third, and `attach` with the game's debugger port is the recovery.
  `beaconFault` says which part of the beacon listen was lost and why, and
  `beaconListening: false` says none of them are left; either way a firewall or a VPN interface
  filtering multicast is the other candidate, and both point at the same recovery.
- **`status` reports no beacon while it also reports a held suspension**: none of the above. A
  suspended game stops broadcasting, so a window held past ten seconds expires its own beacon.
  `resume` and ask again.
- **A beacon with `debuggerEnabled: false`**: the game is running, but was launched without
  `player-connection-debug=1`. Relaunch it with that option.
- **"sent no Mono debugger greeting"**: something is listening on that port and it is not a Mono
  debugger agent, so the port is wrong. A refused connection is the other half of that pair:
  nothing accepted you there at all.
- **Attach fails while your IDE debugger is connected**: the SDB slot is exclusive. Detach the IDE
  (or call `detach` before attaching the IDE); it looks like a connection refusal, not a "slot
  is taken" message.
- **Read the MCP server logs**: Claude Code records each server's connection attempts and stderr
  to per-project `.jsonl` files under the Claude CLI cache, in an `mcp-logs-unity/` folder keyed
  by the project path; the newest `.jsonl` shows why a launch failed:
  - Windows: `%LocalAppData%\claude-cli-nodejs\Cache\<project-path>\mcp-logs-unity\`
