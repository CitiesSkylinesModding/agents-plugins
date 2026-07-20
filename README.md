<div align="center">

# 🕹️ Agents Plugins

**The CS Modding marketplace (`csmodding`) of agent plugins for Claude Code and OpenAI Codex CLI.**

Give your coding agent **eyes and hands inside a running game**: its HTML UI, and its C#/ECS
internals.
Generic tooling, developed and verified against Cities: Skylines II.

[![CI](https://github.com/CitiesSkylinesModding/agents-plugins/actions/workflows/ci.yml/badge.svg)](https://github.com/CitiesSkylinesModding/agents-plugins/actions/workflows/ci.yml)
[![npm](https://img.shields.io/npm/v/%40csmodding%2Fgameface-devtools-mcp?label=npm)](https://www.npmjs.com/package/@csmodding/gameface-devtools-mcp)
[![license](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

[Plugins](#plugins) · [Install](#install) · [Development](#development)

</div>

---

## Plugins

| Plugin                                                          | Give your agent…                                                                                                                                                                  | Runs on               | Docs                                                                                                                                                                               |
| --------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 🖼️ **[coherent-gameface](plugins/coherent-gameface/README.md)** | Eyes and hands in any **Coherent Gameface** (Cohtml) game UI over CDP: evaluate JS, screenshot, drive the DOM, console, JS breakpoints. Plus an engine skill and a driving skill. | Node 22.4+            | [README](plugins/coherent-gameface/README.md) · [Tools reference](plugins/coherent-gameface/mcp/README.md) · [npm](https://www.npmjs.com/package/@csmodding/gameface-devtools-mcp) |
| 🔬 **[unity-devtools](plugins/unity-devtools/README.md)**       | A live line into a **Unity Mono dev build** over the Mono Soft Debugger: reflect types, evaluate C# expressions, read & write ECS state. No code injection. Plus a driving skill. | .NET 10 SDK (Windows) | [README](plugins/unity-devtools/README.md) · [Tools reference](plugins/unity-devtools/README.md#tools) · [NuGet](https://www.nuget.org/packages/UnityDevtools.Mcp)                 |

Both plugins are **generic**: they target any game or application embedding the technology, with
Cities: Skylines II as the reference target where everything is field-verified.

> [!TIP]
> **Not using Claude Code or Codex CLI?** The Gameface MCP server is also published on npm as
> [`@csmodding/gameface-devtools-mcp`](https://www.npmjs.com/package/@csmodding/gameface-devtools-mcp)
> and works with any MCP client (Cursor, Gemini CLI, VS Code, …).

## Install

Add the marketplace once, then install the plugin(s) you want. Each plugin autoloads its MCP
server from a committed artifact: no installation step, works offline, version-locked to the plugin.

### Claude Code

```
/plugin marketplace add CitiesSkylinesModding/agents-plugins
/plugin install coherent-gameface@csmodding
/plugin install unity-devtools@csmodding
```

Or from your terminal, `claude plugin marketplace add …` / `claude plugin install …` with the same
arguments.

### Codex CLI

```sh
codex plugin marketplace add CitiesSkylinesModding/agents-plugins
codex plugin add coherent-gameface@csmodding
codex plugin add unity-devtools@csmodding
```

Then run `/mcp` to confirm the servers connected. Each plugin's README covers its requirements,
configuration and troubleshooting.

## Development

Uses `mise` + `bun` (never `npx`). The repository is a marketplace: plugin sources live under
`plugins/<name>/`, and everything a plugin ships lives inside its directory. The root is a bun
workspace carrying the lint/format tooling (oxlint, oxfmt) and the lefthook git hooks; the
Gameface MCP server is the `plugins/coherent-gameface/mcp/` workspace package, and the Unity MCP
server is a .NET solution (`agents-plugins.slnx`).

```sh
bun install         # install all workspace deps (also installs the git hooks)
mise check          # verify (read-only): type-check, lint, format
mise fix            # auto-fix lint + format in place
mise build          # rebuild every shipped artifact (commit the results)
```

Run `mise tasks` for the full list, and see [`AGENTS.md`](AGENTS.md) (plus each plugin's own
`AGENTS.md`) for the architecture notes.
