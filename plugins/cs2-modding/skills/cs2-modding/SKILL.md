---
name: cs2-modding
description: 'How Cities: Skylines II is built and how a mod changes it. Use when writing or debugging CS2 mod code, when deciding how to change something the game simulates, when locating the systems, components or prefab data behind a game mechanic, or when a mod runs at the wrong moment, breaks another mod, or does not survive a save.'
---

# Modding Cities: Skylines II

A mod is a .NET assembly the game loads by scanning it for a type implementing `Game.Modding.IMod`.
What it does afterwards is add systems of its own, edit the game's data, or rewrite a vanilla method at runtime.

The simulation is Unity ECS: city state lives in components on entities, and systems running in fixed update phases are what change it.
Most of what the player sees as content is a prefab — a data-driven object the game derives entities from — so changing what the game does is often changing data rather than code.
A change therefore starts as two questions: which components carry the state, and which system writes them.

The references below carry the depth, and each is written to be read beside the game's own sources.

## Which source answers what

The decompiled game answers for anything C# names.
The installed game answers for everything else: the compiled string tables, the packaged content, and the whole frontend, which ships as a JavaScript bundle.
So a grep of the decompile that comes back empty settles nothing about those — it means the question belongs to the install.

## Reaching a local source

Every entry into a local source is conditional, so you never grep a path that does not exist.

The user chose where the decompile, the mod corpus and the readable copy of the UI bundle live, so a record is the only thing that finds them: read the record `cs2-modding-setup` owns before touching one, and where a root is absent or recorded as `(none)`, route the user to that skill rather than guessing a path.
The install is different — the official toolchain exports `CSII_*` environment variables naming it and the paths beneath it, so read one rather than hardcoding a path, and treat a missing variable as the signal to ask.

## Reading a marked claim

A `VOLATILE:` marker labels a claim that moves between game versions and names where it lives, so re-derive it from that location when the code, the compiler or the running game disagrees with what you read here; unmarked prose is architecture and holds.

## Going deeper

**Technique** — how to do a thing at all.

- [Custom tools](references/technique/custom-tools/custom-tools.md) — building a tool that claims the cursor, previews what it would do, and commits on click.
- [The developer menu](references/technique/debug-menu/debug-menu.md) — what the menu already gives you, and how a mod puts its own material into it.
- [Diagnostics](references/technique/diagnostics/diagnostics.md) — why a mod is not working: which file to open first, and what each line proves.
- [ECS in this game](references/technique/ecs-in-this-game/ecs-in-this-game.md) — writing ECS code the way this codebase does, and where its practice diverges from stock Entities.
- [Localization](references/technique/localization/localization.md) — the dictionary source a mod registers, the keys it writes, and the ways those strings ship.
- [Mod compatibility](references/technique/mod-compatibility/mod-compatibility.md) — surviving, detecting and cooperating with the other mods loaded beside yours.
- [Mod lifecycle, loading and system ordering](references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) — getting code running at the right moment: the mod contract, the update phases, and where a system lands among them.
- [Navigating the decompile](references/technique/navigating-the-decompile/navigating-the-decompile.md) — finding a type, system, component, binding or string in the decompiled tree, and what an empty search is actually worth.
- [Patching](references/technique/patching/patching.md) — changing the behaviour of code you did not write, when the game offers no seam for it.
- [Performance and memory](references/technique/performance-and-memory/performance-and-memory.md) — not leaking and not stalling, in a build whose collections safety checks are compiled out.
- [Placement definitions](references/technique/placement-definitions/placement-definitions.md) — the seam between a tool and the world: what a tool emits, and what turns that into the thing placed.
- [Prefabs and assets](references/technique/prefabs-and-assets/prefabs-and-assets.md) — finding, editing, cloning, synthesising and registering the game's data-driven content from code.
- [Save serialization](references/technique/save-serialization/save-serialization.md) — making a mod's data survive a save, and keeping it readable by every later build of the mod.
- [Settings and input](references/technique/settings-and-input/settings-and-input.md) — the options page a mod adds, the file it persists to, and the input actions declared beside it.
- [Units and formatting](references/technique/units-and-formatting/units-and-formatting.md) — rendering a quantity in the player's own units, and the interface preferences the formatters branch on.

**Mechanics** — what the game models in one area, and where those numbers live.

- [Citizens and households](references/mechanics/citizens-and-households/citizens-and-households.md) — citizens as the components they carry, and the households that own the money, the home and the members.
- [Zoning, buildings and land value](references/mechanics/zoning-buildings-and-land-value/zoning-buildings-and-land-value.md) — zone cells and blocks, demand, the spawner, the level-up loop, and the land value behind rent.
- [Economy and companies](references/mechanics/economy-and-companies/economy-and-companies.md) — companies and their recipes, the production graph, taxes, fees, trade, loans, extraction economics and tourism.
