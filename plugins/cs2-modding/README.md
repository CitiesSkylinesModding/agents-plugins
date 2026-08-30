<div align="center">

# 📚 cs2-modding

**Teach your agent how Cities: Skylines II is built and how a mod changes it —
verified against the game itself, not against the wiki.**

Knowledge only: four skills and their references. No MCP server, no runtime, no scaffolds.

[![platform](https://img.shields.io/badge/platform-Windows-lightgrey)](#what-it-deliberately-leaves-out)
[![license](https://img.shields.io/badge/license-MIT-blue)](../../LICENSE)

[The skills](#the-skills) · [How it stays honest](#how-it-stays-honest) ·
[What it leaves out](#what-it-deliberately-leaves-out) · [Install](#install)

</div>

---

An agent asked to write a Cities: Skylines II mod has no reliable ground to stand on. The wiki's
most load-bearing modding page is verified against a game several versions old. The decompiled game
is tens of thousands of files with no map. The community's hard-won techniques sit undocumented
across a dozen repositories. Nothing anywhere connects "here is how the game works" to "therefore,
to change X, modify Y".

This plugin is that bridge. It teaches the game's real architecture: system ordering is imperative
here, and the standard ECS ordering attributes do nothing. It teaches the modding techniques that
actually work: patching is a last resort in this game, and the references teach what to do instead.
And it teaches the game mechanics behind what a mod wants to change: which components carry the
state, and which system writes them.

## The skills

Your agent picks these up from their descriptions, so you don't have to name them.

| Skill | What it covers |
| --- | --- |
| **cs2-modding** | The knowledge trunk: the game's architecture, the mod lifecycle, and two families of references — technique (ECS, tools, prefabs, saves, patching, diagnostics, …) and mechanics (citizens, economy, traffic, services, …). |
| **cs2-modding-ui** | The UI discipline: the C#-to-JavaScript binding layer, the game's React frontend and how a mod injects into it, and the UI build. |
| **cs2-modding-setup** | Provisioning: decompiling your own installed game, the developer launch options, patching the game so a debugger can attach, a curated catalog of mods worth reading, and a readable copy of the game's UI bundle. |
| **cs2-mod-project** | The official toolchain: creating a project, the build pipeline and its failure modes, testing locally, and publishing. |

## How it stays honest

- **Every claim was checked against the game itself** (the decompiled code, the installed game's
  own files, or the running game) or ships marked as unconfirmed. Where sources disagree, the game
  wins and the prose says so.
- **Every reference states the game version it was verified against**, so its age is never a guess.
- **Claims that move between game versions are marked** (`VOLATILE:`), naming where to re-check
  them; claims the pipeline couldn't confirm are marked too (`UNVERIFIED:`). Unmarked prose is
  architecture and holds.
- **Balance numbers don't ship.** A hardcoded value would rot with the next patch and can be
  overwritten by another mod at load, so the references name the component and field that hold each
  number — your agent reads what your game actually says.
- **References point at the code rather than restating it**, so what you act on is checkable at its
  source.

## What it deliberately leaves out

- **Asset, map and editor authoring.** Meshes, textures, import setup, map creation: a GUI and
  DCC-tool discipline an agent can't drive. The scope is code mods; loading assets *from code* is
  covered.
- **Mod authoring on Linux.** The official toolchain is Windows-only, and so is this plugin.
- **Code artifacts.** No scaffolds, no templates, no helper classes: the official toolchain
  generates projects, and the references teach mechanisms your agent writes itself.
- **Playing with mods.** Consuming or troubleshooting other people's mods as a player is out of
  scope; the plugin is for writing them.

## Requirements

- **Cities: Skylines II installed on Windows**, with the official modding toolchain (in the game:
  Options → Modding).
- The setup skill walks you through decompiling your own installed game — nothing copyrighted is
  distributed; the decompile is yours, from the game you own.
- Optional but a great fit: the sibling **[unity-devtools](../unity-devtools/README.md)** plugin
  lets the agent verify claims against your running game, and
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

There's no MCP server and no configuration: once installed, the skills are available immediately.
