<div align="center">

# 📚 cs2-modding

**Teach your agent the traps of Cities: Skylines II modding, and a loop that checks its work in the
running game.**

Four skills, plus the `cs2-dev` MCP server, which restarts the game, waits on it, reports its
errors, and runs a mod's in-game scenarios.

[![platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey)](#requirements)
[![license](https://img.shields.io/badge/license-MIT-blue)](../../LICENSE)

[The skills](#the-skills) · [The server](#the-server) · [How it stays current](#how-it-stays-current) ·
[What it leaves out](#what-it-deliberately-leaves-out) · [Install](#install)

</div>

---

An agent asked to write a Cities: Skylines II mod has no reliable ground to stand on. The wiki's
most load-bearing modding page is verified against a game several versions old. The decompiled game
is tens of thousands of files with no map. The community's hard-won techniques sit undocumented
across a dozen repositories.

Your agent can read the decompiled game itself once it knows where to look, so this plugin doesn't
retell it. It gives the agent a working loop instead: find how the game already does the thing,
change it by the least invasive means (a patch comes last), build and restart through the server,
and check the result in the running game. And it lists the traps, the game facts an agent with the
decompile open still gets wrong. The standard ECS ordering attributes are one: they compile here and
do nothing.

## The skills

Your agent picks these up from their descriptions, so you don't have to name them.

| Skill | What it covers |
| --- | --- |
| **cs2-modding** | The working loop, which source answers what, the build-and-restart cycle, and the traps of loading, scheduling, entities, game data and diagnosing. |
| **cs2-modding-ui** | The game's React frontend and the C#-to-JavaScript binding layer: where each answer lives, the UI traps, and the UI build and dev loop. |
| **cs2-modding-setup** | Provisioning: decompiling your own installed game, the developer launch options, patching the game so a debugger can attach, a curated catalog of mods worth reading, and a readable copy of the game's UI bundle. |
| **cs2-mod-project** | The official toolchain: creating a project, the build pipeline and its failure modes, testing locally, and publishing. |

## The server

`cs2-dev` gives your agent the game's lifecycle as MCP tools, so it doesn't have to work out the
process handling in each session.

| Tool | What it does |
| --- | --- |
| `restart` | Stops the game if it's running, keeps the ended run's logs and crash reports, launches it through Steam, and returns at the main menu, or once a save or a new city has loaded. |
| `stop` | Asks the game to close, waits for it to exit, and forces it if it hangs. |
| `wait_load` | Returns once the last logged load has finished; from a city, that is the old load until the new one is logged. |
| `test` | Runs a mod's in-game scenarios unattended and returns each one's result and lines. A run fails on any error naming the mod, inside a scenario or not, and on a run with no scenario. |
| `status` | Says whether the game is stopped, booting, at the main menu, loading or in a city, with its version and build. |
| `logs` | Returns each kind of error or message once, with its count and the file and line it first appeared on. It can also read the run before the last restart or test. |
| `ignore_error` | Hides errors matching a regular expression from the error notices until the server restarts. |

Each tool result ends with a one-line notice when the game logged new errors, went away, or wrote a
crash report. On Claude Code, a monitor brings your agent the same notice while it's using other
tools, and spaces its notices out during an error storm. It starts once your agent has used the
cs2-modding skill in a session.

> [!WARNING]
> **Your agent can stop or kill a running game without asking you.** `restart`, `stop` and `test`
> ask the game to close and force it after a minute. None has a confirmation step. Anything unsaved
> in a city you're playing is lost. On Codex the plugin approves its own tools. On Claude Code your
> permission settings still apply.

## How it stays current

- **It only states what your agent would get wrong.** Your agent reads everything else in the
  decompile of your own installed game, so a game update can't leave the plugin describing code
  that's gone.
- **Each trap names the game type behind it.** After a game update, a script lists the traps whose
  code changed, and only those get re-read.
- **New traps come from use.** When your agent falls into one the skills miss, it tells you once
  your task is done and offers to open an issue for it. The issue holds nothing from your project and goes up
  only if you say yes. You can also turn these reports off.

## What it deliberately leaves out

- **Asset, map and editor authoring.** Meshes, textures, import setup, map creation: a GUI and
  DCC-tool discipline an agent can't drive. The scope is code mods; loading assets *from code* is
  covered.
- **Mod scaffolding.** No scaffolds, no templates, no helper classes: the official toolchain
  generates projects, and the skills teach mechanisms your agent writes itself.
- **Playing with mods.** Consuming or troubleshooting other people's mods as a player is out of
  scope; the plugin is for writing them.

## Requirements

- **Cities: Skylines II with the official modding toolchain.** On Windows, the game installs it
  (Options → Modding). On Linux the game runs under Proton, and that installer can't run there.
  You set the toolchain up by hand instead, and the `cs2-mod-project` skill walks your agent
  through it. The wiki's
  [Modding Toolchain on Linux](https://cs2.paradoxwikis.com/Modding_Toolchain_on_Linux) guide has
  the same steps for you.
- **Node 22.4+** runs the server. The skills work without it.
- The setup skill walks you through decompiling your own installed game — nothing copyrighted is
  distributed; the decompile is yours, from the game you own.
- Optional but a great fit: the sibling **[unity-devtools](../unity-devtools/README.md)** plugin
  lets your agent check its work in your running game, and
  **[coherent-gameface](../coherent-gameface/README.md)** drives the game's UI live.

## Install

Add the marketplace, then install the plugin (see the
[repository README](../../README.md#install) for the marketplace overview).

**Claude Code:**

```
/plugin marketplace add CitiesSkylinesModding/agents-plugins
/plugin install cs2-modding@csmodding
```

**Codex CLI:**

```sh
codex plugin marketplace add CitiesSkylinesModding/agents-plugins
codex plugin add cs2-modding@csmodding
```

There's nothing to configure, except on Linux with the game in a Steam library other than the
default one: set `CSII_USERDATAPATH` to its user data folder.
