# Conflicts and open questions

The disagreements, unverifiable claims and open questions the pipeline cannot settle for itself, collected for the maintainer to rule on.

An agent that cannot settle something appends an entry here and moves on.
Deciding it quietly is how a contested claim ships as fact, and the agent holding one topic's context is the reader least able to see what the decision costs elsewhere.

Not every disagreement lands here: where the decompile settles a fact, the research file records the verdict and the matter ends there.
What arrives here is the residue — a judgement about what ships, a claim no source can settle, a question whose answer changes the product.
The launch-flag entry below is the shape of it: the decompile settled which spellings work, and choosing which one to write was the judgement left over.

**Baseline.** Citations here span four kinds of source, and they do not age together: decompiled source under `src/`, regenerated per game version and read at 1.6.0f1; hand-written prose sitting in a decompile checkout, which carries no version and tracks nothing; this repository's own files, read at the commit in hand; and wiki pages, as `survey-wiki-inventory.md` recorded them on 2026-07-31.

**Entry shape.** A `###` heading naming what is contested — a line, not a paragraph — then three labelled paragraphs:

- `**Sources.**` — who says what, each carrying the citation shape `README.md` prescribes.
- `**Established.**` — what you could prove before you ran out of ground, cited. Never omit it: a ruling made without it is a guess.
- `**Needs a ruling on.**` — the decision you are handing over, what turns on it either way, and the topics whose research files the ruling has to be written into.

A new entry goes under `## Open`.
Ruling one adds a `**Ruling (<date>, <where it was made>).**` paragraph and moves the entry to `## Ruled` with its body intact, question included — the disagreement is why the shipped sentence reads as it does, and the next game version brings the same sources back.

A ruling does not deliver itself.
Whoever rules an entry writes the outcome into the research file of every topic it touches, because the authoring agent reads that file and never this one.

## Open

### A pre-launch balance page whose values are stale and whose schema is not

**Sources.** `Service building data test` (https://cs2.paradoxwikis.com/Service_building_data_test) is unlinked from the rest of the wiki and was last edited 2 August 2023, before the game shipped.
Eight of its thirteen columns also appear on the live `Service buildings` page (`survey-wiki-inventory.md:201` and `:247`): cost, XP, upkeep, service range, capacity, magnitude, consumption and the workforce count.
Of the rest, width and depth are a finer-grained form of the live page's footprint column, leaving the circular flag, garbage accumulation and complexity level with no counterpart there, while the live page carries requirements, pollution, production/efficiency and the education requirement that the test page lacks.
Its numbers are from a build nobody can reach.

**Established.** All three columns with no counterpart on the live page are live game concepts in 1.6.0f1 rather than pre-launch relics: workplace complexity is an enum at `src/Game/Game.Prefabs/WorkplaceComplexity.cs:3-8`, carried on `src/Game/Game.Prefabs/Workplace.cs:22` and consumed at `src/Game/Game.Prefabs/ZonePrefabInitializeSystem.cs:140`; the circular flag is read as `GeometryFlags.Circular` at `src/Game/Game.Areas/ValidationHelpers.cs:233`; garbage accumulation is a field at `src/Game/Game.Prefabs/ConsumptionData.cs:17`.
The schema survived the launch; only the values did not.

**Needs a ruling on.** Whether a mechanics reference may borrow the columns at all, given the decompile carries the same concepts as named types and can be cited directly.
What the page adds is that it gathers them into one table, which the decompile never does — and that convenience is the whole of its value.
The risk runs one way: a reader who follows the link for the schema lands on stale values presented exactly like current ones.
The ruling goes into the research files of the seven topics that borrow a wiki stat table: `city-services-and-coverage`, which this page's own data belongs to, plus `zoning-buildings-and-land-value`, `economy-and-companies`, `roads-and-traffic`, `transportation-and-vehicles`, `city-state-and-progression` and `citizens-and-households`.
They are listed by name rather than by a rule, because the rule would have to be resolved against a source list this directory does not hold, and an entry naming its topics is what this file's own shape asks for.
A later decomposition that renames or splits one of them leaves a stale name here, which is a visible problem; a rule nobody can resolve is not.

## Ruled

### Launch flags: `-developerMode` or `--developerMode`

**Sources.** The wiki contradicts itself across two pages: `Developer mode` (https://cs2.paradoxwikis.com/Developer_mode) writes the single-dash `-developerMode`, `Launch Parameters` (https://cs2.paradoxwikis.com/Launch_Parameters) the double-dash `--developerMode`.

**Established.** The game parses its command line with `Mono.Options` (`src/Game/Game.SceneFlow/GameManager.cs:50` for the import, `:366` for the registration of `developerMode` itself).
That parser takes the three prefixes interchangeably: its option pattern opens `^(?<flag>--|-|/)` at `src/Colossal.Core/Mono.Options/OptionSet.cs:134`, and `Parse` dispatches on the name alone (`:446`).
Both spellings work, so the contradiction is cosmetic and neither page is wrong.

**Needs a ruling on.** Which spelling shipped prose writes, given that the sources disagree and the game does not care.
No research file names a topic here: this was ruled before the stage existed, and a ruling made now would be written into every affected one.

**Ruling (2026-08-01, ticket 02).** The double-dash form throughout, in `plugins/cs2-modding/skills/cs2-modding-setup/SKILL.md`.
With both spellings valid the choice is a house convention, so a reader finding the other form on the wiki has found a variant rather than an error.

### An orientation document in one decompile checkout teaches an ordering mechanism the game does not use

**Sources.** `DecompiledCitiesSkylines2/AGENTS.md:56`, hand-written orientation prose sitting alongside the decompiled source in the maintainer's own checkout, instructs modders to inject custom systems with `[UpdateAfter]` and `[UpdateBefore]`.
The game orders systems imperatively instead, through `UpdateSystem.UpdateAt/UpdateBefore/UpdateAfter<T>(SystemUpdatePhase)`.

**Established.** A grep over `src/Game/` returns zero `[UpdateAfter]`, `[UpdateBefore]` and `[UpdateInGroup]` attributes, so they are inert here and a mod ordered with them compiles and never runs where it meant to.
The error is one sentence in one file rather than a pattern: the same checkout's `DecompiledCitiesSkylines2/docs/game.md:9` sends modders to `SystemUpdatePhase` and `UpdateSystem`, which is the right mechanism, though only one of the three phase names it offers as examples is real — `Rendering` (`src/Game/Game/SystemUpdatePhase.cs:20`), against no `Initialization` and no `Simulation`.
It also reaches no user through this plugin: `plugins/cs2-modding/skills/cs2-modding-setup/SKILL.md:70-93` provisions a decompile by running `ilspycmd` over the user's own installed assemblies and emits `src/` alone, so no shipped path delivers that prose.

**Needs a ruling on.** Whether the trunk names this wrong guidance to warn a reader off it, now that the exposure is one checkout's hand-written file rather than anything the plugin hands out.
Against naming it: shipped prose otherwise states mechanisms on their own authority and cites no source at all.
For naming it: the attribute mechanism is what a modder arriving from stock ECS reaches for by default, so the warning earns its place whether or not anyone read that particular sentence.
The ruling goes into the research file for mod lifecycle, loading and system ordering.

**Ruling (2026-08-02, ticket 07).** Shipped prose states the trap, in `mod-lifecycle-and-ordering`, as a plain negative fact about the game rather than as a correction of any document: the attributes exist, compile, and do nothing here.
That form keeps the rule the question was asked against — prose states mechanisms on its own authority and names no source — while still reaching the reader the warning is for, who arrives from stock ECS and reaches for the attributes by default, having read nothing at all.
Ticket 07's discovery pass also widened the exposure past the checkout that raised it, which is what settles the doubt about whether the warning earns its line: a published mod in the corpus ships `[UpdateAfter(typeof(WeekSystem))]` on a system that, through the band rules, runs _before_ the system it names (`mod-lifecycle-and-ordering.md`, "Ordering is imperative, and the stock ECS attributes are inert here").
The same pass found the stronger proof the prose should rest on — the game imperatively registers a stock system that carries `[UpdateInGroup]` and never creates a system group, so no consumer of those attributes exists in the world — and that, rather than the absence of the attributes from game code, is the fact worth shipping, because it is the one a reader can act on.

### The wiki has no update-phase ordering, and the plugin would be the first source to state one

**Sources.** The wiki's `Systems` page (https://cs2.paradoxwikis.com/Systems) carries the literal placeholder `<insert infographic here>` exactly where the update-phase ordering diagram belongs, and is marked Work-In-Progress (`survey-wiki-inventory.md:93`, and again at `:288`).
No other page states the order.

**Established.** The phase set is enumerated at `src/Game/Game/SystemUpdatePhase.cs` — 32 phases plus `Invalid` — but the enum's declaration order is not the execution order: `src/Game/Game.SceneFlow/GameManager.cs:2390/2398/2406-2407` drives MainLoop, then Cleanup, then LateUpdate and DebugGizmos, while `Cleanup` is declared last (`SystemUpdatePhase.cs:37`) and `LateUpdate` second (`:7`).
`UpdateSystem` answers only the order _within_ a phase: `UpdateAt/UpdateBefore/UpdateAfter<T>` register a sortable index (`src/Game/Game/UpdateSystem.cs:141-154`) that `Refresh()` sorts into per-phase ranges (`:292-363`), and `Update(phase)` walks one range and returns (`:180-183`).
The sequence itself nests rather than running flat: only `GameManager`'s four calls sit on Unity's player-loop callbacks (`GameManager.cs:61` for the `MonoBehaviour`, `:702` and `:717` for the callbacks), and every other phase is driven from inside a system that is itself registered into a phase — `ModificationSystem`, placed in MainLoop at `src/Game/Game.Common/SystemOrder.cs:60`, drives Modification1 through ModificationEnd from its own update (`src/Game/Game.Common/ModificationSystem.cs:19-26`), and `TooltipUISystem`, placed in UIUpdate at `SystemOrder.cs:910`, drives UITooltip (`src/Game/Game.UI.Tooltip/TooltipUISystem.cs:55`).
A grep for `Update(SystemUpdatePhase` across `src/Game` returns 37 matches, two of them `UpdateSystem`'s own overload declarations; the remaining 35 call sites sit in sixteen owners — `GameManager`, `SimulationSystem`, `ToolSystem`, `ToolOutputSystem`, `RenderingSystem`, `PreRenderSystem`, `CompleteRenderingSystem`, `UIUpdateSystem`, `RaycastSystem`, `PrefabSystem` and the serialization systems among them — and together they cover all 32 phases.

**Needs a ruling on.** Two things.
The evidence standard for shipping the ordering, since nothing corroborates whatever the discovery pass derives, and the derivation is a sweep of 37 call sites plus the registrations that place their owners rather than a read of one file.
And the shape of what ships: a flat phase list would misrepresent the game, because a reader who assumes flatness reasons wrongly about what has already run by the time their system updates.
The ruling goes into the research file for mod lifecycle, loading and system ordering, which is where the ordering material lands.

**Ruling (2026-08-02, ticket 07).** The ordering ships, as a nesting tree, in `mod-lifecycle-and-ordering`.

On the shape: a flat list is not a simplification of this material but a different and false claim, because everything driven from `MainLoop` — the modification phases, the tools, the UI, rendering — runs before the frame's simulation steps, which hang off a system in `LateUpdate`.
A reader holding the flat model reasons wrongly about what has already run by the time their system updates, which is the single question the ordering exists to answer.

On the evidence standard: uncorroborated is acceptable here and the derivation carries itself, because being the first source to state it is this plugin's stated value rather than a risk it took on.
What the standard buys instead is provenance in the shipped prose: one sentence saying the tree was derived from the registration table and the phase drivers rather than read from a single file, so a reader re-checking it knows what to re-run.
`VOLATILE:` covers the per-phase system names and counts, which is where the version actually moves; the tree's shape is architecture and carries no marker.
The derivation's own weak points are recorded in the research file under "Where the derivation is weakest" and stay there — a research file is where an authoring agent reads how far to reach, and shipped prose that hedged each of them would teach a reader to distrust the one source that checked.
