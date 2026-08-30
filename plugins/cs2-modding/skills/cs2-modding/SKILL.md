---
name: cs2-modding
description: 'How Cities: Skylines II is built and how a mod changes it. Use when writing or debugging CS2 mod code, when deciding how to change something the game simulates, when locating the systems, components or prefab data behind a game mechanic, or when a mod runs at the wrong moment, breaks another mod, or does not survive a save.'
---

# Modding Cities: Skylines II

A mod is a .NET assembly the game loads by scanning it for a type implementing `Game.Modding.IMod`.
What it does afterwards is add systems of its own, edit the game's data, or — rarely — rewrite a vanilla method at runtime.

The simulation is Unity ECS: city state lives in components on entities, and systems running in fixed update phases are what change it.
Most of what the player sees as content is a prefab — a data-driven object the game derives entities from — so changing what the game does is often changing data rather than code.
A change therefore starts as two questions: which components carry the state, and which system writes them.

Verified against game version 1.6.0f1.
Every reference below states its own baseline.

## Five facts to hold before writing a line

**IMPORTANT: follow this skill's references on anything they own — or at the very least grep them before acting on a familiar shape, because this game diverges from the standard shapes exactly where a prior feels safest, and the guess tends to compile, run and fail silently.**

**Mods load late, through a two-method contract.**
`Game.Modding.IMod` declares `OnLoad(UpdateSystem)` and `OnDispose()` and nothing else, and detection requires the interface directly on a top-level class.
`OnLoad` runs once per mod per process, after the world exists and every vanilla system is registered — on the boot path before prefabs load and before anything has ticked, though enabled mid-session it lands past all of that — so a mod can only append, and injecting ahead of the game is unavailable by construction.
The mod object is allocated without running any constructor, so its instance field initialisers never run — static fields are unaffected — while a mod's systems are created normally and do get theirs.
A throw during load — `OnLoad`, or any system's `OnCreate` — kills the whole mod quietly, with `Modding.log` as the only record; a throw from a later hook or from `OnUpdate` pops the game's error dialog instead, and only some hooks disable the throwing system.
[Mod lifecycle, loading and system ordering](references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) carries the contract; [diagnostics](references/technique/diagnostics/diagnostics.md) the diagnosis order.

**Ordering is imperative, and the stock ECS attributes do nothing.**
`[UpdateInGroup]`, `[UpdateBefore]` and `[UpdateAfter]` compile and are read by nothing: the game builds a bare world, never runs the group sorter, and orders every system through the `UpdateSystem` object `OnLoad` receives.
The update phases nest as a tree driven from inside systems' own updates rather than running flat, a registration lands in one of three bands within one phase or anchored beside a named system, and a mod always registers after all of vanilla.
An update interval that is not a power of two fails the whole mod at registration, and only the phases `SimulationSystem` drives consult the interval at all — an override on a system elsewhere silently throttles nothing.
[Mod lifecycle, loading and system ordering](references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) carries the phase tree and how to choose.

**Zero-field tags are the change protocol.**
The game signals change by adding tag components, stripped again at the end of the frame: `Created`, `Updated` and their siblings.
Change anything visible without adding `BatchesUpdated` and the renderer keeps drawing the old batch, with no error anywhere.
`Temp` marks the tool preview and nearly every vanilla query excludes it, so a query that forgets sees the player's uncommitted hover as a thing that exists.
Deletion is the `Deleted` tag plus a frame of grace — a query that does not exclude `Deleted` processes dying entities — and `DestroyEntity` is reserved for entities nothing else can be holding.
[ECS in this game](references/technique/ecs-in-this-game/ecs-in-this-game.md) carries the tags and everything else a system's body is made of.

**The decompile misleads in three ways before it informs.**
`[CompilerGenerated]` sits on a large fraction of hand-written game classes, so it is never a reason to skip a file.
Local variable names carry no meaning — `num2`, or a type name with a suffix — so intent is never read off one.
`[assembly: AssemblyVersion("0.0.0.0")]` is a decoy; the real version is the `VersionInternal` attribute in `src/Game/Properties/AssemblyInfo.cs`.
[Navigating the decompile](references/technique/navigating-the-decompile/navigating-the-decompile.md) carries the full artifact catalogue.

**The tree is searchable by two rules, and an empty search proves nothing on its own.**
The layout is `src/<Assembly>/<FullNamespace>/<TypeName>.cs` — a handful of files sit one level up, so search `src/**` — a short list of assemblies is the reading universe, and `Game` dwarfs the rest.
`src/Game/Game.Common/SystemOrder.cs` holds every phase registration the game makes, so "when does this run" is one grep in one file.
A grep that comes back empty is evidence about the search: constants are inlined away at every use.
[Navigating the decompile](references/technique/navigating-the-decompile/navigating-the-decompile.md) carries the recipes and the checks that come before writing "nothing".

## Patching is the exception here, not the default

The order to try is: insert a system, disable-and-fork a vanilla one, rewrite a vanilla query from outside, cache a reflection accessor — and then patch.
[Patching](references/technique/patching/patching.md) carries the discipline and the evidence; [placement definitions](references/technique/placement-definitions/placement-definitions.md) the interception pattern that replaces the most tempting patches.

## Which source answers what

The decompiled game answers for anything C# names.
The installed game answers for everything else: the compiled string tables, the packaged content, and the whole frontend, which ships as a JavaScript bundle.
So a grep of the decompile that comes back empty settles nothing about those — it means the question belongs to the install.

## Reaching a local source

Every entry into a local source is conditional, so you never grep a path that does not exist.

The user chose where the decompile, the mod corpus and the readable copy of the UI bundle live, so a record is the only thing that finds them: read the record `cs2-modding-setup` owns before touching one, and where a root is absent or recorded as `(none)`, route the user to that skill rather than guessing a path.
The install is different — the official toolchain exports `CSII_*` environment variables naming it and the paths beneath it, so read one rather than hardcoding a path, and treat a missing variable as the signal to ask.
The toolchain's build targets and the Unity project's package cache sit outside the install root as well, and [`cs2-mod-project`'s build-pipeline reference](../cs2-mod-project/references/build-pipeline.md) names the environment variables that locate them.

The wiki takes the same guarded posture, on capability rather than on a path: try a web-fetch tool first, expect a plain HTTP fetch to return the site's JavaScript bot challenge instead of content, and ask the user as the last resort.

## Verifying against the running game

With the sibling `unity-devtools` plugin installed and the game patched for debugging — `cs2-modding-setup` provisions the patch — a claim about live state settles against the running game instead of being guessed.
The frontend renders in Coherent Gameface, and the sibling `coherent-gameface` plugin is the domain reference for the engine underneath it.
Neither is required, and every skill here works unchanged without them.

## Reading a marked claim

A `VOLATILE:` marker labels a claim that was established and moves between game versions, naming what moves and where it lives.
An `UNVERIFIED:` marker labels a claim the authoring pipeline reached and could not confirm, naming what would settle it.
Unmarked prose is architecture and holds.

Re-derive a `VOLATILE:` claim from the location it names when one of four things happens: the API as described cannot do what you need, the code does not compile against it, it contradicts something you already know about this game, or the user reports it does not work.
Otherwise read a `VOLATILE:` claim as true at its stated baseline — re-deriving on sight spends your context re-proving claims that were right — and treat an `UNVERIFIED:` one as a lead to confirm before building on it, since it was never established.

## From a simulation dimension to the code

The game's own taxonomy of what it simulates is its info views, and each maps onto one mechanics reference below.
(VOLATILE: the view names in the first column — the game's `infoviews.infoviews` binding, published by `src/Game/Game.UI.InGame/InfoviewsUISystem.cs`; the `Infoviews` string table also carries keys no view uses.)

| Info views | Mechanics reference |
| --- | --- |
| Population, Happiness, Citizen Wealth, Workplace Availability | [citizens and households](references/mechanics/citizens-and-households/citizens-and-households.md) |
| Residential, Commercial, Industrial, Office, Building Level, Land Value | [zoning, buildings and land value](references/mechanics/zoning-buildings-and-land-value/zoning-buildings-and-land-value.md) |
| Company Profitability, Tourism | [economy and companies](references/mechanics/economy-and-companies/economy-and-companies.md) |
| Electricity, Water & Sewage | [utilities and flow networks](references/mechanics/utilities-and-flow-networks/utilities-and-flow-networks.md) |
| Healthcare & Deathcare, Garbage Management, Fire & Rescue, Disaster Control, Police, Administration, Education, Post, Telecom, Leisure | [city services and coverage](references/mechanics/city-services-and-coverage/city-services-and-coverage.md) |
| Roads, Traffic, Bicycles | [roads and traffic](references/mechanics/roads-and-traffic/roads-and-traffic.md) |
| Transportation, Outside Connections | [transportation and vehicles](references/mechanics/transportation-and-vehicles/transportation-and-vehicles.md) |
| Air Pollution, Ground Pollution, Noise Pollution, Water Pollution, Natural Resources | [environment and pollution](references/mechanics/environment-and-pollution/environment-and-pollution.md) |

Two load-bearing layers have no view, because a view only draws what is spatial: the city-level singleton layer — statistics, milestones, policies, map tiles, notifications — is [city state and progression](references/mechanics/city-state-and-progression/city-state-and-progression.md), and time itself is [simulation time and units](references/mechanics/simulation-time-and-units/simulation-time-and-units.md).

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
- [Utilities and flow networks](references/mechanics/utilities-and-flow-networks/utilities-and-flow-networks.md) — electricity, water and sewage as solved max-flow graphs: what carries each utility, the solve cadence, consumption, production and trade.
- [City services and coverage](references/mechanics/city-services-and-coverage/city-services-and-coverage.md) — service buildings as dispatchers: pathfind coverage on road edges, the request/dispatch protocol, budget, fees, efficiency, workforce, districts, and each service's failure surface.
- [Roads and traffic](references/mechanics/roads-and-traffic/roads-and-traffic.md) — the network model of nodes, edges, lanes and compositions, road classes, pathfinding weights and costs, congestion, junctions, parking, accidents, intercity traffic, and the queue that throttles the game clock.
- [Transportation and vehicles](references/mechanics/transportation-and-vehicles/transportation-and-vehicles.md) — transit lines, stops, waypoints and routes, depots, fleets and dispatch, passenger against cargo, outside connections as objects, and the vehicle itself in components.
- [Environment and pollution](references/mechanics/environment-and-pollution/environment-and-pollution.md) — the cell-map layers under the city: the five pollution kinds and their cross-contamination, groundwater and surface water, natural resources and their depletion, wind, climate, weather, seasons, day-night effects, and disasters as event entities.
- [City state and progression](references/mechanics/city-state-and-progression/city-state-and-progression.md) — the city entity and its singletons, statistics and their collection modes, XP, milestones and the dev tree, unlock requirements and the fixpoint unlock loop, map tiles and permits, policies at four scopes and the modifier composition, and notifications as icons.
- [Simulation time and units](references/mechanics/simulation-time-and-units/simulation-time-and-units.md) — the frame index as the only clock, the 262144-frame day that is also a month, the epoch and the calendar, what an update interval is worth in game time, the day-night boundaries and seasons, and the unit a raw C# value crossing to the frontend is denominated in.

The UI a mod ships, the toolchain that builds the mod, and the provisioning of every local source above are other skills: `cs2-modding-ui` for the binding layer and the frontend, `cs2-mod-project` for the project, build and publishing, `cs2-modding-setup` for setup and refresh.
