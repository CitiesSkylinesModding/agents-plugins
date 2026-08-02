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

### Usage contexts scope the conflict a mod author sees and not the one that disables their action

**Sources.** `Mod Key Binding` (https://cs2.paradoxwikis.com/Mod_Key_Binding, fetched live 2026-08-03) states "Conflict detection uses usage strings" and "Custom usage strings prevent conflicts based on matching action sets", listing four defaults — Default, Overlay, Tool, CancelableTool.
Six of twenty corpus repositories declare custom or narrowed usages and clearly believe them to be the scoping mechanism: `Traffic/Code/ModSettings.Keybindings.cs:23-31` puts nine actions on `"Traffic.Tool.LaneConnector"` and `"Traffic.Tool.Priorities"`, `ExtraDetailingTools/MOD/Settings.cs:11-14` on `"EDT.InTransformTool"`, `CS2-MoveIt/Code/MoveIt/Settings/Settings.cs:16-35` puts all twenty of its actions on `"MoveIt_Input"`, and `Anarchy/Anarchy/Settings/AnarchyModSettings.cs:23-25`, `CS2-NetworkTools/NetworkTools.Mod/Settings/Settings.cs:17-27` and `NodeController/NodeController/Setting.Keybindings.cs:9-15` narrow theirs to `Usages.kToolUsage`.

**Established.** The decompile splits the wiki's claim in two, and the halves point opposite ways.
Usage-aware: `ProxyBinding.hasConflicts` and `ProxyBinding.conflicts` call `ProxyBinding.ConflictsWith(x, y, checkUsage: true)`, which requires `Usages.TestAny(x.usages, y.usages)` on top of a matching device and control path (`src/Game/Game.Input/ProxyBinding.cs:447/492`, `:680-698`).
Those two feed the warning triangle on the options row (`src/Game/Game.UI.Menu/InputBindingField.cs:54-64`) and the per-map conflict notification (`src/Game/Game.Input/InputManager.cs:1389-1435`).
Usage-blind: the pass that actually disables an action goes through `InputManager.HasConflicts`, which calls `ConflictsWith(x, y, checkUsage: false)` (`InputManager.cs:718-755`, the call at `:746`), and `InputConflictResolution.ResolveConflicts` turns a hit into `ProxyAction.ApplyState(false, ...)` and a real `InputAction.Disable()` (`src/Game/Game.Input/InputConflictResolution.cs:138-198`, `src/Game/Game.Input/ProxyAction.cs:577-599`).
What does scope the disable is enablement: `ResolveConflicts` only pairs actions whose `preResolvedEnable` is currently true (`InputConflictResolution.cs:148-182`), and that is decided by the action's map, its activators and its input barriers (`ProxyAction.UpdateState`, `ProxyAction.cs:524-575`).
A grep of `src/Game/` outside `Game.Input` and `Game.Settings` returns no other consumer of `Usages` at 1.6.0f1, and the default set is five members rather than the wiki's four (`BuiltInUsages.DefaultSet = 0x41E`, `src/Game/Game.Input/BuiltInUsages.cs:20`).
What could not be established is whether a usage has any effect this pipeline cannot see. `Usages` values for built-in actions are deserialized from the input asset rather than from source (`src/Game/Game.Input/CompositeInstance.cs:128/261`), the frontend receives each binding's conflict list as JSON (`InputBindingField.cs:76-81`) and may act on it, and this pipeline cannot run the game.

**Needs a ruling on.** What the `settings-and-input` reference teaches a reader to expect from a usage string.
Three options and each costs something.
Teach the wiki's model — declare usages so your binding only conflicts inside its own context: matches what seven of twenty mods do and what every reader will find written elsewhere, and it is contradicted by the one code path that decides whether their action fires.
Teach the proven model — a usage narrows the warning the player sees and nothing else, and a mod binding that shares a control path with a currently-enabled vanilla binding is disabled whatever its usages say: correct on the evidence, and it tells a reader that a technique the whole corpus uses does not do what they think, on the strength of one `checkUsage: false` argument.
Teach both halves as two mechanisms with the same name — usages for the diagnostic, enablement for the runtime — and hand the reader `shouldBeEnabled` gated on the mod's own state as the thing that actually scopes an action: honest and actionable, at the price of being the only place in this plugin that tells a reader a wiki page and a corpus practice are both wrong about the same fact.
What turns on it is a reader's whole model of why their hotkey stopped working, which is the single most common runtime complaint this area produces, and the notification the game pushes names their mod by name.
The ruling goes into the research file for `settings-and-input`, and touches `custom-tools` only if that reference also states why a tool's inherited apply and cancel actions are exempt from this.

**Ruling (2026-08-03, ticket 14).** The third option, in `settings-and-input`: both halves ship as two mechanisms that happen to share a name, and `shouldBeEnabled` gated on the mod's own state is what the reference hands the reader as the thing that scopes an action at runtime.

A usage narrows the conflict the player is _shown_ — the warning triangle on the options row and the per-map notification — and at 1.6.0f1 that is the whole of its effect anywhere this pipeline can look.
The pass that disables an action ignores usages entirely, pairs any two currently-enabled actions that share a control path, and always resolves against the mod.
Neither half may ship alone, which is what makes this the third option rather than a softened first or second: the wiki's model alone leaves a reader debugging a dead hotkey against a mechanism that never touched it, and the proven half alone tells them a practice seven of twenty mods follow does nothing, without handing them the one that works.

The correction ships in this plugin's own voice rather than hedged, because the two halves are separately verifiable and were separately verified — `checkUsage: true` at the two `ProxyBinding` call sites, `checkUsage: false` at the single `InputManager.HasConflicts` one, and `State.enabled` reading `preResolvedEnable && !m_HasConflict`.
The reference states the split as a fact about the code. It does not argue with the wiki page, and it does not characterise what other mods do — the corpus is input here as everywhere, and "seven mods believe otherwise" is not a sentence shipped prose can carry.

The limit the entry recorded travels into the prose rather than being argued away.
Built-in actions' usages are deserialized from an input asset that is not decompiled source, and the frontend receives each binding's conflict list as JSON and may act on it.
So what ships is a claim about what disables an action in C#, which is the half a reader can act on, and it carries the volatility marker: the next version's sweep re-checks whether that argument is still `false`.

### One mod in twenty stamps an empty prefab on its own entities, and the reason it gives cannot be verified

**Sources.** `Traffic/Code/Helpers/FakePrefab.cs:9-10` is a `PrefabBase` subclass whose own comment states its purpose: "used purely for vanilla validation workaround with custom entites interacting with vanilla ones".
`survey-mods-techniques.md:473` promotes it to the corpus's second most instructive hack, describing it as "an empty `PrefabBase` whose only job is to satisfy vanilla validation", created in `IPreDeserialize.PreDeserialize` and stamped onto mod-created entities as their `PrefabRef`.
It is unique in the corpus: a grep for `: PrefabBase` returns twelve subclasses and the other eleven are real prefabs with real content.

**Established.** One requirement is provable and narrower than the mod's phrasing. `PrimaryPrefabReferencesSystem.m_PrefabRefQuery` matches every entity carrying `PrefabRef` and not `Temp` or `Deleted` (`src/Game/Game.Serialization/PrimaryPrefabReferencesSystem.cs:392`), and `FixPrefabRefJob` remaps each non-null reference through `PrefabReferences.Check(ref Entity)`, which indexes `m_PrefabData[prefab]` with no guard (`:37-49`, `src/Game/Game.Serialization/PrefabReferences.cs:61-83`).
So an entity that carries a `PrefabRef` must point at a registered prefab entity by load time, which is exactly why the mod registers through `PrefabSystem.AddPrefab` inside `PreDeserialize` (`Traffic/Code/Systems/ModDefaultsSystem.cs:34-39`, registered at `Traffic/Code/Mod.cs:101`).
What could not be proved is the converse — that an entity with **no** `PrefabRef` breaks anything. The nearest unconditional site is `ValidationSystem`'s `ValidateEntitiesJob`, which for a chunk carrying `Game.Objects.Object` indexes the `PrefabRef` chunk array positionally against the entity count (`src/Game/Game.Tools/ValidationSystem.cs:802/809`), and Traffic's connection entities carry neither `Game.Objects.Object` nor `Game.Tools.Temp`, so that is not the site its comment names. No other unconditional site was found in a sweep of `Game.Tools/`, `Game.Serialization/` and `Game.Prefabs/`.
The mod itself treats the reference as mandatory in a second place, re-adding one on load when it is missing and logging that it was (`Traffic/Code/Systems/Serialization/TrafficDataMigrationSystem.ValidateLoadedDataJob.cs:420-429`), which is evidence of the author's belief rather than of the mechanism.

**Needs a ruling on.** What the `placement-definitions` reference teaches, given that the technique is real and the reason for it is one mod author's unverified claim.
Three options and each costs something. Teach the broad rule the mod and the survey state — give every entity your mod creates a `PrefabRef` pointing at an empty registered prefab: matches the only worked example, and asks every reader to carry a prefab they may not need, on an authority the pipeline could not confirm. Teach only the narrow provable rule — if your entity carries a `PrefabRef`, that reference must resolve to a registered prefab by load time: correct and complete as far as the evidence goes, and it silently drops the case the technique was invented for, so a reader hitting that case gets no help. Teach the narrow rule and name the broad practice beside it as what one mod does and why it believes it has to: honest, and it is the only form that costs shipped prose a hedge, which every other reference in this plugin avoids.
What turns on it is a mod's entity archetype, which is the hardest thing to change once a save format depends on it — a reader who adds the prefab late has to migrate, and a reader who adds it needlessly has carried a dead component into every save.
The ruling goes into the research file for `placement-definitions`, and touches `prefabs-and-assets` only if that reference also states a rule about when a mod-created entity needs a `PrefabRef`.

**Ruling (2026-08-02, ticket 13).** The third option, in `placement-definitions`: the narrow provable rule ships as a rule, and the broad practice is named beside it as a practice.
An entity carrying a `PrefabRef` must point at a prefab entity registered through `PrefabSystem.AddPrefab` by the time the load pass runs, because the remap indexes `PrefabData` unguarded and the pre-deserialize hook is the last place to register it — that half is settled and states flat.
The empty-prefab technique itself ships as what one mod does and why it believes it has to, carrying the limit of the evidence in the sentence: the vanilla site that faults on an entity with no `PrefabRef` at all was searched for across the tools, serialization and prefab code and not found.
Neither half may ship alone, which is what makes this the third option rather than a softened first or second: the narrow rule by itself drops the case the technique was invented for, and the practice by itself would put one author's belief into this plugin's own voice.

The hedge the question objected to is accepted rather than argued away, and it is the only one in the shipped tree.
What the reader is deciding is an entity archetype, which the entry itself identifies as the hardest thing to change once a save format depends on it — a late addition is a migration and a needless one is a dead component in every save.
A reader making that call is owed the difference between what was proven and what was believed, and a reference that stated the broad rule flat would be spending its own authority on a mod comment.

### The game rewrites tool definitions in a phase no mod uses, and the corpus's alternative needs a workaround the game's does not

**Sources.** The game's own definition rewriter, `CourseSplitSystem`, runs at `SystemUpdatePhase.PostTool` (`src/Game/Game.Common/SystemOrder.cs:723`) and writes through `ToolReadyBarrier`, which is registered `UpdateAfter` in that same phase (`:697`) and therefore plays back before the modification phases begin.
Every definition-touching mod system in the corpus instead sits in `Modification1` or `Modification3`: `Anarchy/Anarchy/AnarchyMod.cs:146` and `:147`, `BetterBulldozer/BetterBulldozer/BetterBulldozerMod.cs:122`, `Tree_Controller/Tree_Controller/TreeControllerMod.cs:119`, `CS2-Platter/Platter/PlatterMod.cs:218`, `Traffic/Code/Mod.cs:85` and `:86`.
`ecs-in-this-game.md:335` records `ToolReadyBarrier` at zero corpus uses across all 20 repositories, and this pass confirmed it.

**Established.** The two windows are not equivalent, and the difference is a real cost rather than a preference.
A `Modification1` rewriter cannot use that phase's barrier: `ModificationBarrier1` plays back at the end of the phase (`SystemOrder.cs:86`), by which time `GenerateObjectsSystem` and its siblings have already read the definitions. So all four corpus rewriters write synchronously instead — `EntityManager.SetComponentData` directly (`Tree_Controller/Tree_Controller/Systems/TreeObjectDefinitionSystem.cs:117/150`), or an `Allocator.Temp` command buffer played back inside `OnUpdate` (`Anarchy/Anarchy/Systems/ObjectElevation/ElevateObjectDefinitionSystem.cs:77/120-121`, `CS2-Platter/Platter/Systems/Tool/P_GenerateZonesSystem.cs:46/63-65`).
A `PostTool` rewriter has no such constraint, because `ToolReadyBarrier` plays back before any consumer runs. That is why the vanilla system can schedule a parallel job and hand the barrier a producer handle (`src/Game/Game.Tools/CourseSplitSystem.cs:4124/4138`) while every corpus rewriter completes on the main thread.
What is not established is whether `PostTool` works for a mod at all. Nothing bars it — `ToolReadyBarrier` is a public `SafeCommandBufferSystem` like the others — but with zero corpus uses there is no evidence that a mod system registered there sees the definitions, and this pipeline has no way to run the game.

**Needs a ruling on.** Which window the reference teaches as the default for rewriting a definition.
Teaching `Modification1` ships what four mods do and what the pipeline can vouch for, and it ships the synchronous-playback rule as a hard requirement, which is the single fact most likely to save a reader a day. Teaching `PostTool` ships the architecture the game actually uses, lets the rewrite be a scheduled job instead of a main-thread loop, and rests on nothing anyone has run. Teaching both makes the reference the first source to state that the vanilla window exists, which is this plugin's stated value, at the price of pointing readers at an untested path.
The evidence standard is the substance of the question rather than a side issue: `mod-lifecycle-and-ordering`'s ordering tree was ruled shippable while uncorroborated (this file, ticket 07), and the same argument applies here — but that derivation was a sweep of readable call sites, whereas this one would be a claim about runtime behaviour nobody has observed.
The ruling goes into the research file for `placement-definitions`, and touches `custom-tools` only if that reference also names a phase for a tool's helper systems.

**Ruling (2026-08-02, ticket 13).** The third option, in `placement-definitions`, with the first as the default: both windows ship and `Modification1` is what the reference teaches.
The front band of `Modification1` is where a definition rewriter goes, and the synchronous-playback rule ships as a hard requirement rather than as advice — `ModificationBarrier1` plays back at the end of the phase, after the consumers have read, so a rewrite queued into it lands too late, and the write goes through `EntityManager` or through an `Allocator.Temp` buffer the system plays back itself.
`PostTool` with `ToolReadyBarrier` is named beside it as the window the game's own rewriter uses, with the property that makes it worth knowing — the barrier plays back before the modification phases, so the rewrite can be a scheduled job rather than a main-thread loop — and with its status in the same breath: no mod uses it, this pipeline cannot run the game, so what ships is the architecture rather than a tested path.

The evidence standard the entry raised is what separates this from ticket 07's ruling rather than what aligns it.
That derivation was a sweep of readable call sites and could carry a shipped default; this one would be a claim about runtime behaviour nobody has observed, which is enough to name a window and not enough to send readers to it first.
So the `PostTool` claim takes the volatility marker, and the next version's sweep re-checks both whether the window is still open and whether anyone has since used it.
The `Modification1` default and the barrier timing behind it are architecture and take no marker.

### Six mods insert themselves at the front of the tool list, and the reference cannot teach that to all of them

**Sources.** `ToolSystem.ActivatePrefabTool` walks `ToolSystem.tools` in order and stops at the first tool whose `TrySetPrefab` returns `true` (`src/Game/Game.Tools/ToolSystem.cs:263-278`), so list position is prefab-claim priority and index 0 has first refusal on everything the toolbar hands out.
`ToolBaseSystem.OnCreate` appends every tool to the end of that list (`ToolBaseSystem.cs:315`), and the vanilla eleven are registered first (`src/Game/Game.Common/SystemOrder.cs:699-709`), so a mod tool starts last.
Six repositories therefore remove and reinsert themselves: `LineTool-CS2/Code/Systems/LineToolSystem.cs:579-607`, `AreaBucket/Systems/AreaBucketToolSystem.cs:198-199` and `AreaBucket/Systems/AreaReplacementToolSystem/AreaReplacementToolSystem.cs:72-73`, `CS2-NetworkTools/NetworkTools.Mod/Systems/Tools/Base/BaseToolSystem.cs:381-382` and `Systems/Tools/PrefabCache/PrefabCacheToolSystem.cs:46-47`, `Tree_Controller/Tree_Controller/Tools/TreeControllerTool.cs:363-364`, `BetterBulldozer/BetterBulldozer/Systems/BetterBulldozerUISystem.cs:365-368`.

**Established.** The collision is already real and already unsolved in the wild, and the corpus says so in its own source.
`LineTool-CS2/Code/Systems/LineToolSystem.cs:599` reads `toolList[0].toolID` and, finding the literal string `"Tree Controller Tool"`, inserts itself at index 1 instead of 0 — a hard-coded deference to one named competitor, which works only because that competitor exists and only for that one competitor.
The two mods reordering at `OnGameLoadingComplete` rather than `OnCreate` (Tree Controller and Better Bulldozer) win against everyone reordering at `OnCreate`, and neither says that is why.
`ExtraDetailingTools/MOD/Systems/Tools/GrassToolSystem.cs:111-113` shows the restrained form nobody else uses: it reads `tools.IndexOf(objectToolSystem)` and takes exactly that slot, so it precedes the one tool it needs to precede and nothing else.
What makes index 0 survivable at all is the `TrySetPrefab` gate: `LineTool` claims a prefab only while `m_ToolSystem.activeTool == this` (`:468-482`) and `Recolor` claims nothing ever (`Recolor/Recolor/Systems/Tools/ColorPainterToolSystem.Main.cs:130-133`), so both cost the tools behind them nothing.
A tool at index 0 that answers `true` broadly takes the toolbar away from every tool after it, and no mechanism in the game arbitrates.

**Needs a ruling on.** Whether the `custom-tools` reference teaches front-of-list insertion at all, and in what form.
Three options and each costs something. Teach it plainly, as six of twenty mods do it: every agent-written tool then reaches for index 0, and the first two such mods a user installs fight over the toolbar with no diagnostic. Teach it only bound to the cooperative gate — insert at the front, and make `TrySetPrefab` return `true` only while already active — which is what the two mods actually sitting there do, and which makes the position harmless but also makes it useless for the case it exists for, a tool that wants to claim a prefab kind the vanilla tools already claim. Teach the restrained form as the default and index 0 as the exception: correct, and it asks a reader to know which vanilla tool they must precede, which the reference would then have to tell them.
The timing question rides along: reordering at `OnGameLoadingComplete` beats reordering at `OnCreate`, so whichever form ships also picks a hook, and picking the later hook is picking to win the race against mods that picked the earlier one.
The ruling goes into the research file for `custom-tools`, and touches nothing else — no other topic owns `ToolSystem.tools`.

**Ruling (2026-08-02, ticket 12).** The third option, in `custom-tools`: the restrained form is the default the reference teaches, and index 0 ships as an exception bound to the cooperative gate.
A tool takes the slot of the one tool it must precede, read back with `tools.IndexOf(...)`, rather than the front of the list.

The cost the question attached to that option dissolves on the evidence the same pass produced: it objected that a reader has to know which vanilla tool they must precede, and the vanilla list order is a readable eleven-name registration table (`SystemOrder.cs:699-709`) the reference bakes anyway.
Index 0 stays teachable for the case it exists for — claiming a prefab kind a vanilla tool already claims — with the gate stated as its condition rather than as advice: return `true` from `TrySetPrefab` only while already active, which is what both mods actually sitting there do, and which is why they cost the tools behind them nothing.

The ruling also settles the timing question rather than answering it, and that is the reason to prefer it over the other two.
A position stated relative to a tool does not need to win a race: a mod inserting at index 0 after you does not stop you preceding the object tool, so the reference ships `OnCreate` and says why no later hook is needed.
Teaching `OnGameLoadingComplete` would have been teaching an agent to beat other mods to the front, and the first two tools written from that instruction fight each other.

### A job interface the toolchain supports, the game never uses, and one mod in twenty proves works

**Sources.** `survey-mods-techniques.md:154` states that "source-generated `IJobEntity` doesn't play well with the modding toolchain, so everyone hand-writes `IJobChunk` with `ArchetypeChunk` + `ComponentTypeHandle<T>`." That was the twelve-repository pass and it carries no verdict.
The decompiled game declares 771 `IJobChunk` structs and zero `IJobEntity` structs (`ecs-in-this-game.md`, "The job interface").

**Established.** The survey's stated reason is wrong at 1.6.0f1, and the practice it describes is real anyway.
The official toolchain wires all twelve Entities source generators as Roslyn analyzers, `JobEntityGenerator.dll` among them, and hard-errors if the directory is missing (`C:\Users\Morgan\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Modding\Mod.props:63-74`, `Mod.targets:85-87`).
One corpus mod uses `IJobEntity` end to end and ships: `InfoLoom/InfoLoom/Systems/DemographicsData/Demographics.cs:22/51/77/390`, a `partial struct … : IJobEntity` inside a `partial class … : GameSystemBase`, scheduled with the generated `job.Schedule(query, Dependency)`.
So it compiles, it runs, and it is used once in twenty repositories against 172 `IJobChunk` declarations.
The countervailing facts are equally solid: a `IJobEntity` body cannot see the chunk, so it cannot do the per-chunk early exit the game's simulation systems rest on (`src/Game/Game.Simulation/AgingSystem.cs:68-71`), cannot read a shared component, and — the practical one — cannot be produced by copying a decompiled vanilla job, which is where the corpus's dominant technique starts.

**Needs a ruling on.** Whether the shipped technique reference teaches `IJobEntity` at all, and if so in what position.
Three options and each costs something. Teach `IJobChunk` only: the reference is then silent about an interface that works, and an agent that reaches for it from stock-ECS habit gets no guidance on the `partial` requirement or the loss of chunk access. Teach both with `IJobChunk` as the default: honest, but doubles the job material in a reference whose readers mostly need one shape. Teach `IJobEntity` as the modern default: contradicts every line of game code an agent will read next, and stops the fork-a-vanilla-system technique from being a paste, since the body being forked is always `IJobChunk` and a rewrite has to replace the chunk-level early exit, the chunk-scoped accessors and `unfilteredChunkIndex` with per-entity equivalents that do not exist.
What turns on it is not correctness but whether the reference reads as a description of _this codebase_ or as a description of Unity ECS, which is the distinction the whole plugin exists on.
The ruling goes into the research file for `ecs-in-this-game`, and touches `performance-and-memory` only if the chunk-level early exit is argued there as well.

**Ruling (2026-08-02, ticket 10).** The third option, in `ecs-in-this-game`: the per-entity job interface is the default the reference teaches for new mod code, on the stated ground that it is the more modern replacement for the chunk interface.

The reference states the discrepancy plainly rather than papering over it — the game itself is `IJobChunk` throughout, so every line of vanilla code the reader opens next is written the other way, and that gap is a fact about the codebase's age rather than a reason to follow it.
Both interfaces therefore ship. `IJobChunk` is taught well enough to read vanilla source and to fork it, because the fork technique starts from a decompiled body and that body is always `IJobChunk`; what it loses to the per-entity form is the chunk-level early exit, the chunk-scoped accessors and `unfilteredChunkIndex` as a parallel command buffer's sort key, and the reference names all three so a reader converting a fork knows what has no per-entity equivalent.
The `partial` requirement on both the job struct and its enclosing system ships with it, since that is what the generator needs and its absence is the first thing an agent hits.

The entry's own "Needs a ruling on" paragraph overstated one cost before this was ruled, and was corrected in place: the per-entity default does not disable the fork technique, it stops the fork from being a paste.

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
