# Conflicts and open questions

The disagreements, unverifiable claims and open questions the pipeline cannot settle for itself, collected for the maintainer to rule on.

An agent that cannot settle something appends an entry here and moves on.
Deciding it quietly is how a contested claim ships as fact, and the agent holding one topic's context is the reader least able to see what the decision costs elsewhere.

Not every disagreement lands here: where the decompile settles a fact, the research file records the verdict and the matter ends there.
What arrives here is the residue — a judgement about what ships, a claim no source can settle, a question whose answer changes the product.
The launch-flag entry below is the shape of it: the decompile settled which spellings work, and choosing which one to write was the judgement left over.

**Baseline.** Citations here span five kinds of source, and they do not age together: decompiled source under `src/`, regenerated per game version and read at 1.6.0f1; the game's own shipped files read out of a 1.6.0f1 install, including the UI bundle, whose citations are to a copy reformatted with prettier at its defaults at `DecompiledCitiesSkylines2/src-ui/source.js`, 135,021 lines; hand-written prose sitting in a decompile checkout, which carries no version and tracks nothing; this repository's own files, read at the commit in hand; and wiki pages, as `survey-wiki-inventory.md` recorded them on 2026-07-31.

**Entry shape.** A `###` heading naming what is contested — a line, not a paragraph — then three labelled paragraphs:

- `**Sources.**` — who says what, each carrying the citation shape `README.md` prescribes.
- `**Established.**` — what you could prove before you ran out of ground, cited. Never omit it: a ruling made without it is a guess.
- `**Needs a ruling on.**` — the decision you are handing over, what turns on it either way, and the topics whose research files the ruling has to be written into.

A new entry goes under `## Open`.
Ruling one adds a `**Ruling (<date>, <where it was made>).**` paragraph and moves the entry to `## Ruled` with its body intact, question included — the disagreement is why the shipped sentence reads as it does, and the next game version brings the same sources back.

**An entry can also dissolve, and that is a third outcome rather than a quiet close.**
Evidence found after an entry is opened can answer factually what was put up as a judgement — most often a source nobody had reached for, the user's own installed game above all.
Say so in the `**Ruling**` paragraph, and move the entry to `## Ruled` like any other: its body is the record of what the pipeline was one decision away from shipping on weaker authority, which is worth more than the space it costs.
So before escalating a question of the form _which source do we trust_, check whether a first-party artifact settles it — a dissolved entry costs the maintainer a round-trip that never needed making.

**Amending a ruled entry's evidence is not a fourth outcome, and gets an `**Addendum (<date>, <where it was found>).**` paragraph** below the ruling rather than a move.
Use it when new evidence overturns something the `**Established.**` section says and the decision above it still stands: name what no longer holds, say why the ruling survives it, and correct the section in place so it reads as current.
Where the new evidence would have changed the decision, this is the wrong shape — reopen the entry and put the question again.

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

**Established, amended 2026-08-03 (ticket 15b, the first pass to open the install's packaged content).** The real 1.6.0f1 values are on the machine, and what it costs to read one splits by which content it belongs to.

Content-pack prefabs ship as loose `.Prefab` entries inside plain zips — a `Prefabs*.cok` per DLC directory plus `Cities2_Data/Content/Game/Prefabs_FreeUpdate02.cok`, 1,571 entries across the eight of them.
Each entry is a **self-describing binary key/value stream**: UTF-16LE type names and field names inline, values inline, no schema needed.
`Playground04/Playground04.Prefab` decodes as a `Game.Prefabs.BuildingPrefab` carrying `Game.Prefabs.ServiceConsumption` with `m_Upkeep = 2000`, `m_ElectricityConsumption = 500`, `m_WaterConsumption = 0`, `m_GarbageAccumulation = 100`, `m_TelecomNeed = 0` — field order and types exactly as `src/Game/Game.Prefabs/ServiceConsumption.cs:14-22` declares them, and the values vary per prefab across the package (`ChirperPark01` at 1000/250/0/100, `BasketballCourt04` at 8000/3000/10000/150).
Three of those five are columns this page tabulates.

**The base game's own service buildings are not among them**, which is the half that keeps the entry open.
No `.cok` in the install holds a base-game prefab: `Blob.cok`'s 27,910 entries are materials, geometry, surfaces and textures, and the prefab packages carry DLC and free-update content only.
Every base-game prefab is a Unity serialized object in `Cities2_Data/resources.assets`, which carries type names (`ServiceConsumption`, `BuildingPrefab`) and **no field names**, so reading a value there needs a Unity serialized-file parser driven by the decompiled class's field order — a derivation rather than a read.
`docs/SOURCES.md` entry 5 now records the split; it previously described the whole set as "the shipped prefabs, assets and their data" and had never been opened.

So this entry does not dissolve the way the key-namespace one did.
First-party 1.6.0f1 numbers for the buildings the page is about exist and are reachable, at a cost that case did not carry: either that parser, or the running game, where the prefab system plus one component read returns any of them directly.

**Needs a ruling on.** Whether a mechanics reference may borrow the columns at all, given the decompile carries the same concepts as named types and can be cited directly.
What the page adds is that it gathers them into one table, which the decompile never does — and that convenience is the whole of its value.
The risk runs one way: a reader who follows the link for the schema lands on stale values presented exactly like current ones.
The ruling goes into the research files of the seven topics that borrow a wiki stat table: `city-services-and-coverage`, which this page's own data belongs to, plus `zoning-buildings-and-land-value`, `economy-and-companies`, `roads-and-traffic`, `transportation-and-vehicles`, `city-state-and-progression` and `citizens-and-households`.
They are listed by name rather than by a rule, because the rule would have to be resolved against a source list this directory does not hold, and an entry naming its topics is what this file's own shape asks for.
A later decomposition that renames or splits one of them leaves a stale name here, which is a visible problem; a rule nobody can resolve is not.
The 2026-08-03 amendment does not change the question and adds one option to it: a reference that wants the page's convenience can instead be pointed at first-party numbers, at one of the two costs above. Whether that is worth asking of seven topics is the same judgement, better informed.

## Ruled

### A mod that deletes another mod's data has one worked example and one worked refusal

**Sources.** `save-serialization.md:386` hands `mod-compatibility` "the question of when doing it is legitimate", the mechanism itself being settled there and in `mod-compatibility.md`.
The corpus takes three positions on reaching into another mod's data and they do not agree.
Read and delete: `Traffic/Code/Systems/ModCompatibility/TLEDataMigrationSystem.cs` migrates a foreign mod's per-node lane directions and then removes the foreign component from every entity that carried it, commented `// delete TLE data components to prevent data corruption` (`:89-91`), and disables the other mod's system so it cannot act on what is gone (`Traffic/Code/Mod.cs:166`).
Read only: `Traffic/Code/Systems/ModCompatibility/RoadBuilderCompatibilitySystem.cs:28-76` resolves another mod's tag component by name, queries on it as a signal, and writes only to its own components.
Outside the save, the same split appears over a third mod's residue on disk. `FindIt-CSII/FindIt/Mod.cs:115-136` deletes `ModsData/Gooee` and `Mods/Gooee` recursively, gated on that mod not being enabled, from a deferred callback inside a bare `catch {}`. `CS2-MoveIt/Code/MoveIt/Settings/FileUtils.cs:13-22` detects the same two folders and puts a warning and an **open folder** button on its own settings page instead (`CS2-MoveIt/Code/MoveIt/Settings/LocaleEN.cs:222-240`).

**Established.** Nothing in the game stops any of it, and the four capabilities are each proven.
A foreign mod's components are in the world by the time any mod's system runs, because the loader loaded that assembly and the serializer library reflected over its types like any other; a type resolved by name through `TypeManager.GetTypeIndex` becomes an ordinary `ComponentType` (`TLEDataMigrationSystem.cs:34-44`), so query, read, write and `EntityManager.RemoveComponent` all work (`:45-50`, `:90-91`).
A foreign mod's _system_ can be switched off from outside: `World.GetExistingSystemManaged(type)` on a type resolved the same way, then `Enabled = false` (`Traffic/Code/Mod.cs:158-168`).
A foreign mod's on-disk data is an ordinary directory under `EnvPath.kUserDataPath` with no game-side ownership concept at all — the decompile has no `ModsData` or `ModsSettings` constant anywhere in `src/`, so the layout is a wiki convention (`https://cs2.paradoxwikis.com/Naming_Folder_And_Files`, `survey-wiki-inventory.md:50`) that mods compose by hand.
And the game gives the wronged mod no way to notice: `IMod` has two members, `OnLoad` and `OnDispose` (`src/Game/Game.Modding/IMod.cs`), and `OnDispose` fires only at process shutdown (`src/Game/Game.SceneFlow/GameManager.cs:792`).
What is equally established is that the destructive form has a real justification in the one case that carries it: `Traffic` takes over the vanilla `LaneSystem` slot the other mod also took (`Traffic/Code/Mod.cs:76`, `:168`), so both mods' data cannot coexist, and it reports what it did to the player in a dialog naming both mods and the count (`TLEDataMigrationSystem.cs:94-98`).
What could not be established is whether the deleting mods asked their targets. Nothing in either repository records a conversation, and that is not a question any source here can answer.

**Needs a ruling on.** What the `mod-compatibility` reference teaches about acting on another mod's data, given that the mechanism is the same for all three positions and only the intent differs.
Three options and each costs something.
Teach the mechanism with the read-only position as the rule — resolve a foreign type, query it, never write it, never remove it: safe, matches the majority of the corpus's sites, and it drops the one case the technique was invented for, so a reader taking over another mod's slot gets nothing and improvises.
Teach it with the destructive form bound to a stated condition — you may migrate and remove another mod's data only when you are replacing the system that produced it, and you tell the player you did: correct on the one worked example, and it is the plugin telling agents when to delete somebody else's user data, which is a line no other reference in this plugin goes near.
Teach the mechanism and refuse the destructive form outright, naming only the read-only and write-your-own variants: cleanest, and it makes the reference silently incomplete about a technique a reader will find in the corpus the moment they look, with no warning attached.
The on-disk half rides along and may need a separate answer: deleting a component inside a save the player can restore from a backup is not the same act as `Directory.Delete(recursive: true)` on a folder outside it, and the two corpus positions there are a deletion and a refusal to delete rather than two migrations.
What turns on it is whether an agent that detects a competitor reaches for removal as the default remedy. This is the first reference in the plugin whose subject is what one author may do to another author's users, and an agent following it will not be the one who hears about it.
The ruling goes into the research file for `mod-compatibility`, at the "Migrating another mod's data" finding, and touches `save-serialization` only if that reference also states who may remove a foreign component.

**Ruling (2026-08-06, ticket 21).** None of the three, because the question was put to the wrong party: whether a mod replaces another mod, cooperates with it or ignores it is the mod author's own design decision, and the plugin's job is to make that decision informed rather than to license one of its outcomes.

So all three positions ship, as postures a mod takes toward another mod's data, over one statement of the mechanism — resolve the foreign type by name, query it, and from there read, write or remove it, which is the same machinery in every case.
**Replace** migrates the data, removes the foreign component and disables the other mod's system. **Cooperate** reads the foreign component as a signal and writes only the mod's own. **Coexist** leaves it alone.

What is not a matter of taste is the consequence set, and that is the half the reference owes its reader.
Removal is permanent inside the player's save. The other mod is never told, because `IMod` carries no hook that could tell it. A directory removed outside the save has no restore path at all, which is what separates the on-disk case from the in-save one rather than a second judgement about intent.
Replacing is coherent when the mod has taken over the vanilla system that produced the data — otherwise it has deleted data whose producer is still running, which is the failure the posture is chosen to avoid.
And the worked example tells the player what it did, in a dialog naming both mods and the count, because the player is the party losing the data.

This is why the entry does not resolve into a rule with a condition attached, which is what its own three options each assumed. A condition would read as a permission the plugin grants, and an agent that satisfies it would delete on the plugin's authority rather than on its author's.

### The only leak diagnostic this game has is a switch the game deliberately turned off, and a mod that turns it back on turns it on for everyone

**Sources.** `How To Avoid Memory Leaks` (https://cs2.paradoxwikis.com/How_To_Avoid_Memory_Leaks, fetched live 2026-08-04) teaches that a `TempJob` allocation "persists for four frames, after which point it should be disposed of", which in stock Unity is enforced by a native leak-detection warning naming the allocation.
One corpus mod ships the switch enabled: `Time2Work/NightShift/Mod.cs:175` sets `NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace` as the last statement of `OnLoad`, unconditionally.
A second used it and took it out: `Traffic/Code/Mod.cs:116` is the identical line, commented.
Nobody else in twenty-two repositories touches it.

**Established.** The game disables native leak detection at boot, and the method that does it is named `EnableMemoryLeaksDetection`. Its whole body is `NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;` (`src/Game/Game.SceneFlow/GameManager.cs:1877-1880`), called once from `Awake` (`:529`), long before any mod assembly loads.
The setter is a live call into the native runtime rather than a `[Conditional]` no-op (`src/UnityEngine.CoreModule/Unity.Collections/NativeLeakDetection.cs`, wrapping the `extern UnsafeUtility.SetLeakDetectionMode` at `src/UnityEngine.CoreModule/Unity.Collections.LowLevel.Unsafe/UnsafeUtility.cs:110`), and the allocation path goes through `MallocTracked` / `FreeTracked` (`src/UnityEngine.CoreModule/Unity.Collections/NativeArray.cs:299`, `:337`), so the tracking machinery is present in the shipped build and switched off rather than compiled out.
The mode enum is `Disabled = 1, Enabled, EnabledWithStackTrace` (`src/UnityEngine.CoreModule/Unity.Collections/NativeLeakDetectionMode.cs:7-11`).
So at 1.6.0f1 a leaked `TempJob` or `Persistent` allocation produces no warning, no log line and no console output, and this is the only mechanism in the game that would produce one — the collections safety system that reports the neighbouring class of error is compiled out entirely and the mod toolchain defines no symbol that would restore it (`NativeArray.cs:218-222` has no safety-handle field; `cs2-moddingtools/Mod.props` carries no `DefineConstants`).
What is equally established is the blast radius: the mode is a property of the native allocator, not of the calling assembly, so a mod that sets it imposes leak bookkeeping — and under `EnabledWithStackTrace`, a managed stack capture per allocation — on the game's own thousands of allocations per frame and on every other loaded mod.
What could not be established is the size of that cost at 1.6.0f1. Measuring it means running the game with the mode set and comparing frame times, which the sibling Unity plugin could drive and this pass did not.

**Needs a ruling on.** Whether `performance-and-memory` teaches a reader to set `NativeLeakDetection.Mode`, and if so in what form.
Three options and each costs something.
Say nothing: the reference then teaches the disposal discipline and leaves a reader whose memory climbs with no instrument at all, which is the single hardest failure in this area to diagnose and the one the topic exists for.
Teach it plainly: every agent-written mod that hits a memory question reaches for a process-global switch, and the cost lands on the player and on every other mod in their load order — and one corpus mod already ships it enabled, so this is a practice that spreads rather than a hypothetical.
Teach it bound to a condition — set it from a debug configuration or behind a mod setting the player does not have on by default, never in a shipped default path: correct on the evidence and it asks shipped prose to carry a caveat about other people's mods, which no other reference in this plugin does.
What turns on it is whether the plugin's readers get the one diagnostic that exists, against whether the plugin becomes the reason a player's frame time drops after installing a mod that never had a leak.
The ruling goes into the research file for `performance-and-memory`, and touches `diagnostics` only if that reference also states a diagnosis order for a mod whose memory grows.

**Ruling (2026-08-04, ticket 18).** The third option: the reference teaches the switch, bound to a condition — a debug configuration, or a mod setting that is off unless the player turns it on, and never a shipped default path.

The ground is the asymmetry the `**Established**` section proves. The cost is not paid by the mod that opts in: the mode is a property of the native allocator rather than of the calling assembly, so it lands on the game's own allocations and on every other mod in the player's load order. A reader whose memory climbs still gets the one instrument this game has, which is what the "say nothing" option gives up and what the topic exists for.

This does ask shipped prose to carry a caveat about other people's mods, which the entry correctly noted no other reference here does. That is accepted rather than worked around: the reason is the process-global scope, which is a property of this particular switch and not a precedent for a general style. Two limits ride with it — the reference states the scope of the cost and claims no figure, since nothing measured it at 1.6.0f1, and it does not name the corpus mod that ships the switch enabled, because shipped prose credits no repository and the technique stands on its own authority.

Separately and outside what the reference says: the mod shipping it enabled is `ruzbeh0/Time2Work` at `NightShift/Mod.cs:175`, the only occurrence in that repository and therefore never reset. Raising it with the author was noted as worth doing at the time of this ruling; that is an act outside this pipeline and nothing in the plugin depends on its outcome.

### The corpus's Burst gate is the one seven of ten mods get wrong, and the first-party one nobody uses needs no rebuild

**Sources.** `survey-mods-techniques.md:158` promotes the compile-time gate as a thing to teach: "Traffic gates every `[BurstCompile]` behind `#if WITH_BURST`, set only in Release … MoveIt uses `USE_BURST`; WriteEverywhere uses a `<Bursted>` property. Teach this: Burst makes stepping/debugging impossible, so gate it."
The approved reference structure carries the same instruction into this topic's **Owns** line, as "that Burst makes stepping impossible, so gating it by configuration is the norm".

**Established.** The practice is real, the reason is real, and the execution fails more often than it works.
Ten of twenty-two repositories wrap `[BurstCompile]` in a preprocessor gate, under three different symbol names: `WITH_BURST` (`Traffic`, 68 sites), `USE_BURST` (`CS2-Platter` 49, `CS2-NetworkTools` 22, `CS2-MoveIt` 12), `BURST` (`CS2-WriteEverywhere` 24, `BetterBulldozer` 20, `Water_Features` 19, `Tree_Controller` 15, `Recolor` 11, `Anarchy` 7).
**Two of the ten define the symbol in Release only**, which is what the technique is for: `Traffic/Code/Traffic.csproj:23-26` against its Debug group at `:17-21`, and `CS2-Platter/Platter/Platter.csproj:27-35`.
**One defines it in every configuration**, so the gate never fires: `CS2-MoveIt/Code/MoveIt/MoveIt.csproj:77-93` sets `USE_BURST` in Release, Stable and Debug alike.
**Seven define it nowhere in the checkout.** `Anarchy`, `BetterBulldozer`, `Recolor`, `Tree_Controller` and `Water_Features` contain no `DefineConstants`, no `Directory.Build.props`, and no `.props` or `.targets` file at all — 72 `[BurstCompile]` attributes behind `#if BURST` with nothing that could define it. `CS2-NetworkTools` uses `#if USE_BURST` 22 times and defines it in no csproj. `CS2-WriteEverywhere` sets a custom `<Bursted>` property (`CS2-WriteEverywhere/BelzontWE/BelzontWE.csproj:7-8`) whose only possible consumer, `$(SolutionDir)\_Build\belzont_public.targets` imported at `:16`, is not in the repository.
A first-party alternative exists, needs no rebuild and no cooperation from the mod, and has zero corpus users and zero wiki mentions. `BurstCompilerOptions`' static constructor sets `ForceDisableBurstCompilation` from the launch argument `--burst-disable-compilation`, and separately from the environment variable `UNITY_BURST_DISABLE_COMPILATION` set to anything but empty or `"0"` (`src/Unity.Burst/Unity.Burst/BurstCompilerOptions.cs:681-707`, the constants at `:12` and `:14`); `IsEnabled` is `EnableBurstCompilation && !ForceDisableBurstCompilation` (`:252-262`), and the managed body survives in the assembly for the fallback to reach — the post-processor's own generated shim is `if (BurstCompiler.IsEnabled) { … native call … } return <Name>$BurstManaged(…);` (`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs:542-553`, four more sites under `src/Game/`).
What could not be established is whether the flag actually restores steppable execution in this AOT player build, or whether the seven repositories with undefined symbols also ship unbursted. The first needs the running game under a debugger; the second needs `ilspycmd` over the published assemblies in the Paradox mods cache. Neither was run.

**Needs a ruling on.** Which gate `performance-and-memory` teaches as the way to debug a Burst-compiled job.
Three options and each costs something.
Teach the compile-time gate as the survey and the structure say: it is what the corpus reaches for, it is the only form verified to work here, and the reference would be teaching a technique whose failure mode is silent — a symbol defined nowhere produces no warning, no error, and a build that looks exactly like a working one, which is how seven repositories arrived where they are.
Teach the runtime flag: it is first-party, it needs no build system change, it cannot be got wrong the way a `#if` can, and no evidence exists that anyone has run it against this game.
Teach both, with the runtime flag first as the thing to try and the compile-time gate as what to set up if you are going to do this often — honest, and it makes this the second place in the plugin that ships an untested first-party path beside a proven corpus one, the first being `PostTool` in `placement-definitions` (this file, ticket 13).
What turns on it is whether an agent told to gate Burst writes a `#if` into a csproj it may not be able to verify, and whether shipped prose should carry the observation that a preprocessor gate whose symbol is undefined is indistinguishable from one that is defined — which is a fact about C# rather than about this game, and the reason the corpus's failure rate is what it is.
The ruling goes into the research file for `performance-and-memory`, and touches `diagnostics` only if that reference also states how to get a mod's job into a debugger.

**Ruling (2026-08-04, ticket 18).** The third option, and the order within it is the ruling: the reference teaches both gates and leads with the runtime one. `--burst-disable-compilation` or `UNITY_BURST_DISABLE_COMPILATION` is what a reader reaches for to get a job into a debugger; the `#if` gate is what to set up if you will do it often enough that a launch argument becomes tiresome.

What decides it is the failure rate in the `**Established**` section rather than a preference between two working techniques. Seven of the ten repositories using the compile-time gate define the symbol nowhere in the checkout, and the reason that happens is worth stating in the reference: a preprocessor symbol defined nowhere produces no warning, no error, and a build indistinguishable from a working one. That is a fact about C# rather than about this game, and it is what makes the compile-time form the more dangerous of the two to hand to an agent — an agent writing a `#if` into a csproj it cannot run is precisely the case those seven repositories describe.

The cost is accepted and stated rather than hidden: the runtime flag is unrun against this game, nothing establishes that it restores steppable execution in this AOT player build, and the reference marks that instead of implying it is proven. This is the second place the plugin puts an untested first-party path ahead of a proven corpus one, after `PostTool` in `placement-definitions`, and it is ruled the same way for the same reason — a first-party mechanism that cannot be got silently wrong beats a corpus practice that can.

### The complete vanilla key-namespace table exists only in a compiled UI bundle a mod author copied into their repository

**Sources.** The `localization` reference is the named owner of the vanilla localization-key namespace table, and that table is a mechanism table rather than balance data: an agent cannot reuse a vanilla key without it.
Three sources carry a version of it and they are not equivalent.
The decompiled game yields 21 namespaces as C# string literals — `Editor`, `Common`, `Options`, `Properties`, `Paradox`, `Assets`, `Tools`, `Menu`, `PhotoMode`, `DefaultTool`, `SelectedInfoPanel`, `Services`, `Maps`, `Infoviews`, `Policy`, `GameListScreen`, `SubServices`, `StatisticsPanel`, `Radio`, `Notifications`, `Loading` — anchored at `src/Game/Game.UI/LocaleIds.cs:5-13`, `src/Game/Game.UI.InGame/PrefabUISystem.cs:1498-1527`, `src/Game/Game.Modding/ModSetting.cs:303-371` and roughly 500 further literals.
`Localize your mod` (https://cs2.paradoxwikis.com/Localize_your_mod, fetched live 2026-08-03) tabulates roughly 35 prefixes, collapses the 17 distinct `*InfoPanel` groups into one row, and omits 14 groups that exist.
A fourth source was found after this entry was opened and it outranks all three: the game's own compiled `.loc` assets, in the user's installed copy at `Cities2_Data/Content/Game/Locale.cok` and `Cities2_Data/StreamingAssets/uk-UA.loc`.
The entry was opened because the only complete list this pipeline could then reach — 72 groups, 2,013 ids — was `CS2-Platter/Platter/UI/tools/source.js:25770-27958`, a beautified copy of the game's compiled UI bundle that a corpus mod author vendored into their repository with no version stamp and no provenance note anywhere in that repo.

**Established.** The shipped locale data answers the question directly, at a known version, from a first-party artefact.
A `.cok` is the asset-database package extension and a plain zip with each asset stored uncompressed (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/PackageAsset.cs:7-9`, `ZipPackageWriter.cs:51-74`); the payload is a flat `BinaryWriter` stream whose reader is its own specification (`LocaleAsset.Load`, `LocaleAsset.cs:109-134`, mirrored by `LocalizationCompiler.WriteLocale`, `LocalizationCompiler.cs:206-227`).
Decoding it at installed version `1.6.0f1 (419.d6c6) [6216.19404]` — the decompile's own build — yields **75 groups, 2,153 ids and 22,120 entries** in `en-US`, which is the fallback locale and therefore the key set that defines what exists (`src/Game/Game.SceneFlow/GameManager.cs:2356-2361`).
That settles all three earlier sources rather than adding a fourth opinion.
The bundle's 72 groups all exist in the shipped data and no group's id count exceeds it — the divergence runs one way only, short by three whole groups (`BikesInfoPanel`, `Glossary`, `WealthInfoPanel`) and 117 further ids, which is what an older copy looks like and not what a wrong copy looks like.
All 21 namespaces the decompiled C# names as string literals exist, none misspelled or renamed; they are 21 of 75, so 72% of the vanilla namespaces are invisible from C# and the decompile alone could never have produced this table.
The wiki names 46 distinct namespaces and every one of them exists — it collapses 23 into a single undetailed row and never names 29 of the 75, `Editor` (the largest group in the game at 263 ids) among them.
What the `.loc` does not carry is the per-id `Single`/`Hashed`/`Indexed`/`HashedIndexed` typing and the argument names.
Those are first-party too, from a second artefact in the same install: the game's compiled UI bundle at `Cities2_Data/Content/Game/UI/index.js`, whose generated `Loc` dictionary types every id by the class it is constructed from and names every argument in that constructor's arguments (`DecompiledCitiesSkylines2/src-ui/source.js:26620-28937`, the four classes at `:26557-26610`).
It carries the same 75 groups and the same 2,153 ids the `.loc` decode gives, so the two independent first-party artefacts agree exactly.

**Needs a ruling on.** The provenance question this entry was opened for is gone: the table is first-party, version-known, and re-derivable by any reader from their own install.
What is left is smaller and is a judgement about what ships rather than about what is true.
The measured table has 75 rows and two count columns (ids and entries), and the ids column is the one a reader reasons with while the entries column mostly records how heavily a group is hashed or indexed — `Assets` carries 12,028 entries against 30 ids.
So: does the reference bake both columns or just ids, and does it ship the decode recipe beside the table?
Shipping the recipe is what makes the table checkable rather than merely asserted, and it is a mechanism a reader can run — a zip reader and a `BinaryReader` against their own installed game — which is the form this plugin prefers everywhere else.
Against it: it is procedure in a reference whose subject is writing strings, and the setup skill is where procedure normally lives.
The ruling goes into the research file for `localization`, and touches `prefabs-and-assets` only if that reference also states the `Services`/`SubServices`/`Assets` key pairs a registered prefab is looked up by.

**Ruling (2026-08-03, ticket 15).** The table ships in full — all 75 groups, both count columns — and the decode recipe does not.

The provenance half of this entry was not ruled but dissolved, and the distinction is worth keeping: the question was which source the plugin is willing to state 51 rows on, and the answer turned out to be a fourth source nobody had reached for. The game ships its own strings, readable from the user's own install at a version they can state. So the reference bakes the table in its own voice, with no hedge and no per-row marking, and the entry is kept as the record of how close the pipeline came to shipping a third party's copy of compiled game code as fact.

Both count columns ship rather than ids alone. Ids is the number a reader reasons with and the one comparable to the generated dictionary, but a group whose entries far exceed its ids is a group whose keys are **constructed** rather than looked up — the reader supplies the bracket contents and the game builds the key — and the ids column alone hides that. `Assets` at 30 ids and 12,028 entries is the case that makes it concrete.

The recipe leaves the reference. It is procedure over the user's own install, the reference's subject is writing strings, and a decode recipe in the middle of it teaches a maintenance task to a reader who came to name a setting. It lands at `method-decoding-shipped-locale-data.md`, a kind of file this directory's `README.md` now documents, and shipping it as a script is roadmap work (`docs/ROADMAP.md`, "Extracting the shipped localization dictionaries"). That keeps the table checkable — the property the recipe was wanted for — without putting it in the reader's way.

**Addendum (2026-08-03, the re-sweep against the shipped UI bundle).** The ruling stands. One sentence of the evidence under it does not: the per-id typing and argument names are first-party after all, from the shipped UI bundle, and the `**Established.**` section above now says so. Nothing the ruling decided depended on it, since what ships is the group set and the two count columns and those came from the `.loc` either way, so the entry is amended rather than reopened.

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

The hedge the question objected to is accepted rather than argued away, and at the time of the ruling it was the only one in the shipped tree.
What the reader is deciding is an entity archetype, which the entry itself identifies as the hardest thing to change once a save format depends on it — a late addition is a migration and a needless one is a dead component in every save.
A reader making that call is owed the difference between what was proven and what was believed, and a reference that stated the broad rule flat would be spending its own authority on a mod comment.

**Addendum (2026-08-03, ticket 15b's re-sweep against `docs/SOURCES.md`).** The gap the ruling accepts is narrower than the `**Established.**` section states it, and the correction is about method rather than about the fact.
"What could not be proved is the converse" reads as a property of the question; it is a property of the sweep. Whether an entity with no `PrefabRef` breaks anything is not a claim any static read settles — a search of `Game.Tools/`, `Game.Serialization/` and `Game.Prefabs/` can only ever return that no unconditional site was found, which is what it did — and it is a claim an experiment settles outright.
The source that runs the experiment was on the list and unused: the running game through the sibling Unity plugin (`docs/SOURCES.md` entry 8), where creating an entity with the archetype the mod's connection entities carry, omitting the reference, and taking it through a save and load either faults or does not.
The ruling survives this unchanged, and cannot be affected by the result: it already ships the narrow rule flat and the broad practice as a practice, and an experiment moves the sentence under it from a gap in the evidence to an observation, in whichever direction it lands.
The game was not running for this pass, so this records the route and not the result.

**Second addendum (2026-08-03, the review gate over tickets 07–15).** The ruling's uniqueness clause has stopped describing the tree, and the maintainer has since given the class a token, so the clause is amended rather than left to mislead.
Four more evidence hedges of the same shape now ship — `prefabs-and-assets` on `UpdatePrefab` against a vanilla prefab, `custom-tools` on a `Temp`-original check, `placement-definitions` on the `PostTool` window, and `ecs-in-this-game` on a lifecycle hook's position relative to the barrier window.
Read the clause as what it was: a statement about the tree on the day it was ruled, and the reason the hedge was affordable, rather than a standing policy that hedging is reserved to this one entry.
What replaces it is `UNVERIFIED:`, now the plugin's second marker (`plugins/cs2-modding/AGENTS.md`), which makes the whole class greppable the way `VOLATILE:` makes the rots-with-the-version class greppable. A maintainer with a running game can now sweep for what to confirm.
The ruling's substance is untouched: the narrow rule still ships flat and the broad practice still ships as a practice.
One of the four has since been settled and is no longer a hedge — `UpdatePrefab` on a vanilla prefab was run against the running game, which is recorded at `prefabs-and-assets.md`'s dead ends and turned out to cost more than the first run showed.

**Third addendum (2026-08-03, same gate). The experiment was run, and the converse is established.**
The entry's open half was whether an entity with **no** `PrefabRef` breaks anything, which no static read settles.

**Method.** In a loaded city, a tree entity — a valid sub-object carrying `Owner`, `Transform` and a `PrefabRef` — had `PrefabRef` and nothing else removed through the sibling Unity plugin, isolating the one variable against an otherwise untouched vanilla archetype. The city was then saved and reloaded.

**Result, and it reproduced.**
At runtime the removal costs nothing, and this was checked with the simulation confirmed running rather than paused — the frame index advanced across the observation, no managed exception fired with a break armed on `System.ArgumentException` and subclasses, and world reads stayed normal. The save completed.

**The process then died at the next world transition, twice, naming the stripped entity both times as the final line of the player log.**
Run one: reloading the save, last line `Owner has no SubObject: 438602:3`. Run two, a fresh entity in a fresh session: returning to the main menu, last line `Owner has no SubObject: 180755:5`. Each index and version is exactly the entity whose `PrefabRef` had been removed, and in each case the log ends there — `SceneFlow.log` stops mid-transition at `Loading mode MainMenu with purpose Cleanup`, and `Player-prev.log`'s final line is the message itself.

The message is `Game.Serialization.SubObjectSystem`'s, at `src/Game/Game.Serialization/SubObjectSystem.cs:49`: a job whose query is `All = {Object}`, `Any = {Owner, Attached}`, `None = {Vehicle, Creature}`, which looks up each member's owner in a `BufferLookup<SubObject>` and logs when the lookup misses. It is a `Debug.Log` rather than a throw, so the log line marks the failure rather than being it; what kills the process immediately after is not established.

**It fired exactly once per run, which is what makes it the cause rather than the last thing printed.** `Player-prev.log` from the crashed process contains one `Owner has no SubObject` line — naming the stripped entity — as its final line, and the healthy session that followed contains none. A message the game emits routinely would not have that shape.

**Where the pieces sit.** The system runs in `SystemUpdatePhase.Deserialize` (`src/Game/Game.Common/SystemOrder.cs:806`), in the back band, after a front band of `PreDeserialize<T>` wrappers and `ClearSystem` (`:737-795`) that empties derived state before it is rebuilt. The `SubObject` buffer itself is not serialized: it is declared on the **owner's** archetype by whichever prefab component gives that owner sub-objects (`BuildingPrefab.cs:53`, `LotPrefab.cs:36`, `NetGeometryPrefab.cs:61/74`, `ObjectSubObjects.cs:26/31`, `NetSubObjects.cs:25`, `AreaSubObjects.cs:25`) and refilled from members' `Owner` back-references on load.

**So the actionable rule is aimed correctly — at the entity you create, not at its owner** — and that was the question worth asking, since the buffer belonging to the parent made it plausible the advice pointed at the wrong end. It does not: the entity that was mutated is the entity the log names.

**Hypothesis, explicitly not a finding:** the parent may be fine and the lookup may be failing on a dead entity rather than on a live owner missing its buffer, because `BufferLookup.TryGetBuffer` returns false for both and the message cannot tell them apart — which would mean the child's own `Owner` reference did not survive the round trip. Nothing here establishes that, and it is recorded only so the next pass does not have to re-derive the possibility.

**The trigger is any world teardown, not the save file.** Quitting to the main menu is enough, and the save written from the damaged world loaded cleanly in a freshly launched process — so nothing is wrong with the bytes on disk, and the shipped prose says the process dies at a transition rather than that the save is ruined.

**So the practice is vindicated on better evidence than its author's comment**, and on a different mechanism than the one the mod gives. The reason to stamp a prefab is not vanilla validation in the abstract: it is that the load-time reconstruction of the owner/sub-object graph runs over these entities, and an entity that fell out of its prefab-derived archetype is what it trips on.

**What is not established**, and the entry should not be read as claiming: that _every_ `PrefabRef`-less entity is fatal rather than only one carrying `Owner`, since both runs used a tree that had one; what actually terminates the process after the log line, the message being a `Debug.Log` rather than a throw; and the chain from a missing `PrefabRef` on a member to a missing `SubObject` buffer on its owner, which is the mechanically interesting part and is still guesswork.

**Three wrong calls are recorded rather than dropped**, because each was stated to the maintainer with more confidence than the evidence carried, and the sequence is the instructive part.
The first freeze was attributed to this removal from a clean timeline and a plausible Burst-lookup mechanism — a hypothesis presented as a finding.
Re-running the removal appeared to refute it, so the attribution was withdrawn; that run had been made against a **paused simulation**, which the maintainer caught, so the refutation was worthless and the withdrawal was itself premature.
In between, the freeze was attributed to a save the maintainer had been asked to perform; they had performed none.
Only the fourth attempt settled it — simulation confirmed ticking by a rising frame index, the entity named in the log, and a second independent reproduction on a fresh entity in a fresh session.
The lesson cuts both ways: a timeline plus a mechanism is a hypothesis, and a failed reproduction is evidence only if the experiment was capable of reproducing it.

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
The official toolchain wires all twelve Entities source generators as Roslyn analyzers, `JobEntityGenerator.dll` among them, and hard-errors if the directory is missing (`cs2-moddingtools/Mod.props:63-74`, `cs2-moddingtools/Mod.targets:85-87`).
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

**Sources.** `DecompiledCitiesSkylines2/AGENTS.md:56` **at commit `ec7c3720`** (`git show ec7c3720:AGENTS.md`; the file was cut down later, see the addendum), hand-written orientation prose sitting alongside the decompiled source in the maintainer's own checkout, instructs modders to inject custom systems with `[UpdateAfter]` and `[UpdateBefore]`.
The game orders systems imperatively instead, through `UpdateSystem.UpdateAt/UpdateBefore/UpdateAfter<T>(SystemUpdatePhase)`.

**Established.** A grep over `src/Game/` returns zero `[UpdateAfter]`, `[UpdateBefore]` and `[UpdateInGroup]` attributes, so they are inert here and a mod ordered with them compiles and never runs where it meant to.
The error is one sentence in one file rather than a pattern: the same checkout's `DecompiledCitiesSkylines2/docs/game.md:9`, also at `ec7c3720` and since deleted, sends modders to `SystemUpdatePhase` and `UpdateSystem`, which is the right mechanism, though only one of the three phase names it offers as examples is real — `Rendering` (`src/Game/Game/SystemUpdatePhase.cs:20`), against no `Initialization` and no `Simulation`.
It also reaches no user through this plugin: `plugins/cs2-modding/skills/cs2-modding-setup/SKILL.md:70-93` provisions a decompile by running `ilspycmd` over the user's own installed assemblies and emits `src/` alone, so no shipped path delivers that prose.

**Needs a ruling on.** Whether the trunk names this wrong guidance to warn a reader off it, now that the exposure is one checkout's hand-written file rather than anything the plugin hands out.
Against naming it: shipped prose otherwise states mechanisms on their own authority and cites no source at all.
For naming it: the attribute mechanism is what a modder arriving from stock ECS reaches for by default, so the warning earns its place whether or not anyone read that particular sentence.
The ruling goes into the research file for mod lifecycle, loading and system ordering.

**Ruling (2026-08-02, ticket 07).** Shipped prose states the trap, in `mod-lifecycle-and-ordering`, as a plain negative fact about the game rather than as a correction of any document: the attributes exist, compile, and do nothing here.
That form keeps the rule the question was asked against — prose states mechanisms on its own authority and names no source — while still reaching the reader the warning is for, who arrives from stock ECS and reaches for the attributes by default, having read nothing at all.
Ticket 07's discovery pass also widened the exposure past the checkout that raised it, which is what settles the doubt about whether the warning earns its line: a published mod in the corpus ships `[UpdateAfter(typeof(WeekSystem))]` on a system that, through the band rules, runs _before_ the system it names (`mod-lifecycle-and-ordering.md`, "Ordering is imperative, and the stock ECS attributes are inert here").
The same pass found the stronger proof the prose should rest on — the game imperatively registers a stock system that carries `[UpdateInGroup]` and never creates a system group, so no consumer of those attributes exists in the world — and that, rather than the absence of the attributes from game code, is the fact worth shipping, because it is the one a reader can act on.

**Addendum (2026-08-05, ticket 20's verification pass over the decompile checkout).** The ruling stands and both cited files are gone from the working tree, so the `**Established.**` section no longer describes the checkout it was written against.
In `DecompiledCitiesSkylines2`, `docs/cohtml.md`, `docs/colossal.md` and `docs/game.md` were deleted and `AGENTS.md` cut from 64 lines to 14, committed in `565e22b7` and `190766c4`. The current `AGENTS.md` is a two-section orientation note carrying no modding guidance at all, so the `[UpdateAfter]` sentence and the three phase-name examples reach nobody reading that checkout today.
Both citations in the `**Sources.**` and `**Established.**` text above now carry that commit, so they resolve where the evidence still exists rather than dangling at deleted paths.
**Anchor the evidence to the commit, not to `HEAD`, which has since moved past it.** Both lines are verifiable at `ec7c3720`: `git show ec7c3720:AGENTS.md` line 56 is the `[UpdateAfter]`/`[UpdateBefore]` instruction, and `git show ec7c3720:docs/game.md` line 9 offers `Initialization`, `Simulation` and `Rendering` as `SystemUpdatePhase` examples, of which only `Rendering` exists (`src/Game/Game/SystemUpdatePhase.cs:20`). The zero-attribute grep over `src/Game/` reproduces unchanged at 1.6.0f1.
The decision is untouched because it never depended on the document: it was ruled that shipped prose states the trap as a plain negative fact naming no source, and the stronger proof it rests on is the game's own `[UpdateInGroup]`-carrying stock system.
What the deletion adds is a durable fact, and it is recorded here rather than shipped: hand-written prose sitting in a decompile checkout is not part of the decompile — it carries no version, tracks nothing, and is whatever its owner last edited.
`navigating-the-decompile` was written to carry it and the gate cut the passage, on the maintainer's ruling that the checkout's own hand-written files are theirs alone and shipped prose may not describe them.
The reference states the tree-level form of the same caution instead, and states no count: an absence proves the game lacks a name only in a tree the provisioning command produced, and in a hand-trimmed one it is a fact about the trimming.

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
