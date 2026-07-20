<div align="center">

# 🔬 unity-devtools

**Give your agent a live line into a running Unity Mono development build:
reflect types, evaluate C# expressions, and read & write ECS state, with no code injection.**

Generic tooling: works with **any** dev-Mono Unity game exposing the Mono Soft Debugger (SDB)
agent, not just Cities: Skylines II.

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
> **Generic, but developed against Cities: Skylines II.** The server makes no assumptions about a
> specific game: it discovers any dev-Mono Unity process by its SDB port signature. It is
> developed and verified against Cities: Skylines II (development Mono build), the reference
> target.

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

| Tool                                      | What it does                                                                           |
| ----------------------------------------- | -------------------------------------------------------------------------------------- |
| `status`                                  | Find dev-Mono game processes and their SDB port (no attach) + session state.           |
| `suspend` / `resume`                      | Hold the game frozen across calls: a consistency window for multi-step edits.          |
| `detach`                                  | Free the exclusive debugger slot (e.g. for your IDE); reattach is automatic.           |
| `find_types`                              | Resolve a type live by fully-qualified name; optionally list its members.              |
| `eval`                                    | Evaluate a C# statement sequence against the live game, like an IDE debugger would.    |
| `ecs_query`                               | Count/list entities having ALL given components, optionally labeled via a system call. |
| `ecs_get_component` / `ecs_set_component` | Read, or field-write with read-back, one entity's component.                           |
| `ecs_get_buffer` / `ecs_buffer_edit`      | Read, append to, or remove from a `DynamicBuffer`.                                     |

There is no "attach" tool: the first tool that needs the VM attaches lazily, the session persists,
and a dropped connection (or game restart) re-discovers and reattaches on the next call.

## How it works

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

- **A Unity game running as a development Mono build** with the SDB agent live (for
  Cities: Skylines II: a dev build; a retail build exposes no SDB port and cannot be driven).
- **Windows** (process/port discovery is netstat-based for now).
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

All are optional; with nothing set, the server auto-discovers any running dev-Mono Unity game by its
SDB port signature.

| Variable            | Default       | Purpose                                     |
| ------------------- | ------------- | ------------------------------------------- |
| `UNITY_MCP_PROCESS` | _(unset)_     | Process-name prefix narrowing discovery.    |
| `UNITY_MCP_PORT`    | _(discovery)_ | Pin the SDB port instead of discovering it. |
| `UNITY_MCP_HOST`    | `127.0.0.1`   | Debugger host.                              |

**On Claude Code**, the plugin's [`.mcp.json`](.mcp.json) forwards them from your environment.
**On Codex CLI**, plugin servers get no environment block, so the server always starts with the
defaults above (auto-discovery).

## Troubleshooting

- **"no dev-Mono Unity game found"**: the game is not running, or is not a development Mono build
  (no SDB agent). Run `status` to see what discovery finds.
- **"several dev-Mono Unity candidates found"**: other processes listen in the SDB port range;
  narrow discovery with `UNITY_MCP_PROCESS` or pin `UNITY_MCP_PORT`.
- **Attach fails while your IDE debugger is connected**: the SDB slot is exclusive. Detach the IDE
  (or call `detach` before attaching the IDE); it looks like a connection refusal, not a "slot
  is taken" message.
- **Read the MCP server logs**: Claude Code records each server's connection attempts and stderr
  to per-project `.jsonl` files under the Claude CLI cache, in an `mcp-logs-unity/` folder keyed
  by the project path; the newest `.jsonl` shows why a launch failed:
  - Windows: `%LocalAppData%\claude-cli-nodejs\Cache\<project-path>\mcp-logs-unity\`
