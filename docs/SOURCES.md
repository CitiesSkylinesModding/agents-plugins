# Sources

Every source the `cs2-modding` pipeline may read, in one place: what each one is, what it is authoritative for, and how to reach it.

**This list grows from the passes that use it.** Every artifact on it was unlisted until somebody went looking, and a first-party artifact sitting unread on the machine looks exactly like one that does not exist. So a pass that reaches something this file does not name, or finds an entry wrong — a path moved, a format not as described, a narrower authority than claimed — amends it and says so, the way a discovery pass amends the mod catalog. A list only its maintainer can grow is a snapshot of the day it was written.
**A new entry appends and never renumbers**, whatever it is about: entry numbers are cited from elsewhere in the pipeline, so the order below is arrival order rather than kinship.

**Precedence: first-party beats everything, and which first-party source wins depends on how the subject ships.**
The decompiled C# and the installed game are both the game itself, and the install holds whole subsystems the C# never names.
Where a topic's subject matter ships as data or as JavaScript, the install outranks the decompile on anything it can answer. Where it ships as C#, the decompile is ground truth and nothing moves.
The official toolchain is first-party too, and authoritative for its own half — how a mod is built, post-processed and published, and what a UI mod may import. It does not outrank the game on anything about the game, and the game does not outrank it on anything about the build.
So among first-party sources the tie-break is which one **owns** the subject, not which sits higher on a list.
Two sources are first-party for something that is not the game: the Harmony library for patch semantics, and Unity's own documentation for the engine. Both answer questions the decompile cannot — the decompiled C# shows what the engine's code contains and not what the runtime does with it — and neither outranks the decompile on anything the decompile can answer.

Below them the order is flat and absolute. The wiki ships as authoritative only where nothing first-party covers the subject, and is a lead generator everywhere else. The mod corpus never settles anything about the game: it is evidence of what mod authors do, which is a different fact.

**Locating a source.** Where the official toolchain is installed it sets `CSII_*` environment variables naming most of these paths, and each entry below gives the variable where one exists. Expect a variable to be missing rather than assuming it: an agent whose session started before the toolchain did will not have inherited them.
Three roots are the user's own choice and no variable names them — the decompile, the mod corpus, and the reformatted UI bundle, the last of which the record also stores a line count for. The record is `~/.cs2-modding/setup.md`, which the setup skill writes and every skill reads before touching a local source; it is the route that works on both supported harnesses, so read it first.
Finding no record, ask the user to run the setup rather than guessing a path.

---

## 1. The decompiled game

The C# of every shipped assembly, decompiled from the install's own managed DLLs.
**Ground truth for anything C# names**: systems, components, prefabs, the modding API, serialization, the binding layer's C# half.
Blind to the frontend entirely, and to any behaviour that lives in shipped data rather than in code. Grepping it and finding nothing proves nothing about either.
Reach it under the checkout's `src/`, one directory per assembly.
**It states its own version.** `src/Game/Properties/AssemblyInfo.cs` carries a `VersionInternal` attribute holding the game version, the changelist and the build. The `AssemblyVersion` attribute on the line below it reads `0.0.0.0` in most assemblies and settles nothing.

## 2. The game's managed assemblies

`Cities2_Data/Managed/` at `%CSII_MANAGEDPATH%`.
The decompile's origin, and what a mod project compiles against.
Read these directly only to confirm what shipped when the decompile is stale or a type is missing from it; the decompile is the readable form of the same thing.

## 3. The game's UI bundle

`Cities2_Data/Content/Game/UI/` under `%CSII_INSTALLATIONPATH%` — `index.js`, `index.css`, `index.html`, beside `Fonts/`, `Media/` and `Static/`.
**First-party and authoritative for the whole frontend**: the module registry and its paths, the React component tree, every number and string formatter, the wire format a binding arrives in, the exported package surface.
It is also the only first-party statement of what a C# value's *magnitude* means: `Game.UI.Unit`'s constants are opaque strings, and the divisor and threshold that turn one into kilowatts or tonnes exist only in the bundle's formatter table, so for every value that crosses to the frontend "what unit is this field in?" is answered here and nowhere else.
**`index.css` is a source in its own right**, and the only one for how the frontend is sized and themed: the rem basis every UI mod's stylesheet is written against, the `:root` custom properties, the interface-style body classes, and the hashed class names a mod has to match on.
`index.js` ships minified to a single line, which costs less than it looks. Grepping it settles presence and absence exactly as usual; only `grep -c` misleads, counting matching lines and so answering 1 or 0 however many hits there are — `grep -o <pattern> | wc -l` gives the real occurrence count.
What the single line genuinely blocks is reading around a match and citing one, and those need a reformatted copy.
**Reformat with prettier at its defaults**, and check your copy's line count against the one the citing file's baseline states before trusting a line number from it. A differing count means the citations do not resolve against your copy; a matching one is good enough to go on. This holds for the stylesheet as well as the script, since it ships the same single-line way; the record names the script's copy only, so reformat the stylesheet beside it.

## 4. The game's compiled locale data

`Cities2_Data/Content/Game/Locale.cok`, a plain zip holding most locales, and `Cities2_Data/StreamingAssets/uk-UA.loc`, the one that ships loose beside it, under `%CSII_INSTALLATIONPATH%`.
Reading only the package silently misses that last one.
**Authoritative for every vanilla string and for the localization-key namespace table.**
A `.loc` payload is a flat `BinaryWriter` stream with no compression, no checksum and no table of contents, so a zip reader and a `BinaryReader` get the whole of it.
That the payload is uncompressed and the package stored also means a raw byte-grep over the `.cok` finds a key by name with no decoding at all — use it to settle whether a key exists and to enumerate a key family, and the decoder for counts and for the whole table.
It is also a first-party enumeration of any prefab family whose strings are keyed by prefab name — `Policy.TITLE[…]`, `Notifications.DESCRIPTION[…]` — and it ships every content pack's strings whether or not the pack is installed, so it lists what exists across all content where a live query lists what this install has. The locale is the cheapest cross-check on a swept roster.

## 5. The game's packaged content

The `.cok` set under `Cities2_Data/Content/Game/`, plus the DLC and radio-pack directories under `Cities2_Data/Content/`, at `%CSII_INSTALLATIONPATH%`.
**Every `.cok` is a plain zip**, stored rather than deflated, one entry per asset beside a `.cid` sibling. A zip reader opens the largest of them without unpacking it.
What is in them splits by kind, and the split is the entry's whole point:

- **Art assets** — `Blob*.cok`, `VT*.cok`, `MidMips*.cok`. Materials, geometry, surfaces, textures, animations. `Blob.cok` alone is 27,910 entries and holds not one prefab.
- **Prefabs, but only for content packs** — a `Prefabs*.cok` in each DLC directory and `Prefabs_FreeUpdate02.cok` in `Game/`. 1,571 `.Prefab` entries across the eight of them. Each is a **self-describing binary key/value stream**: UTF-16LE type names and field names inline, values inline, so a small reader gets `m_Upkeep`, `m_ElectricityConsumption` and the rest by name without a schema.
- **Neither: the base game's own prefabs.** Every road, service building, zoned building and vehicle is a Unity serialized object in `Cities2_Data/resources.assets`, which carries type names and **no field names**. Reading a value there needs a Unity serialized-file parser driven by the field order of the decompiled class — a derivation, not a read. The input action asset (`Resources.Load<InputActionAsset>("Input/InputActions")`) is in the same file and under the same limit.

So this source answers a content-pack prefab question cheaply and a base-game one expensively; where the base game is the subject and a value is what you want, the running game (source 8) is the shorter road.

## 6. The official UI mod scaffold

`@colossalorder/create-csii-ui-mod`, reached at the npm global root. Its `template/types/` holds one declaration file per importable module.
**It is not an npm package that ages on its own: the entry under the global root is a junction into the game install**, at `Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod`, created by the toolchain running `npm link` from that directory (`src/Game/Game.Modding.Toolchain.Dependencies/NpxModProjectDependency.cs:20/116`). So the scaffold is versioned by the game and a patch changes it with no npm step — and the toolchain's own up-to-date check reads the link target, returning `true` when there is no link at all (`:89-97`), so a hand-copied global install reports current forever.
**First-party and authoritative for what a UI mod may import**, module by module: `cs2/bindings` (every binding group's payload types, and by far the largest), `cs2/ui`, `cs2/input`, `cs2/l10n`, `cs2/api`, `cs2/modding` (the module registry's eight operations and its registry map, plus its append-hook targets), `cs2/utils`, plus the Cohtml and React ambient declarations. `assets.d.ts` is not a module beside those: it declares the `*.scss`, `*.css`, `*.svg`, `*.png`, `*.jpg` and `*.gif` wildcards that let a mod import a file, and no `cs2/assets` exists.
**One rule makes these files readable and nothing in them states it: a doubled `export export` marks a real runtime export, and a single `export` marks a declaration emitted for typing only.** Counting the doubled keyword plus the trailing alias block reproduces the shipped package surface exactly, module for module; taking every `export` at face value hands a reader a dozen names that are not there at runtime, several of which live in a different package instead. The rule covers value declarations — `const`, `function`, `class`, `enum` — and not the Cohtml shape-morphing interface trio repeated at the end of six of the ten files, which is a generator artifact; the hand-written `cohtml.d.ts` and `react.d.ts` ambients sit outside the rule entirely — `cohtml.d.ts`'s singly-exported `engine` is real.
**`cs2/bindings` also declares the `__Type` wire tags those payloads arrive under** — 186 of them, as enum values and `const` declarations — which makes it the frontend's record of the strings a C# `TypeBegin` must emit for the game's own components to render a payload; the C# side's record is the `TypeBegin` literals in the decompile, a few gathered in `src/Game/Game.UI/TypeNames.cs`.
The template beside them also carries the reference `webpack.config.js`, `tsconfig.json` and `mod.json`, plus two small files that decide what a built module exports: `tools/css-presence.js`, the plugin that injects the `hasCSS` export the game's loader reads, and `types/validateTypes.ts`, whose comment claims the game ignores every named export — which the bundle contradicts, and which is the one place this source is wrong about the game rather than about itself.
`webpack.config.js` and `mod.json` are a source in their own right for the **UI-module manifest**: `mod.json`'s lowercase `id`, `author`, `version` and `dependencies` are interpolated by the webpack config into a banner comment carrying the capitalised `Id`, `Author`, `Version` and `Dependencies` keys the game's `UIModuleAsset` parser reads back, so the two casings are not interchangeable. The banner reaches the bundle through the minifier's extracted-comments option rather than a banner plugin, which is where to look when a built module carries no manifest at all. The pair is therefore authoritative for the manifest format of every UI mod, with the game's parser as the other half of the same contract.

## 7. The official modding toolchain

MSBuild targets in the `cs2-moddingtools` NuGet package, plus the `ModPostProcessor` and `ModPublisher` executables and the C# project template `ColossalOrder.ModTemplate.1.0.0.nupkg` (its `content/ModTemplate.csproj` is the shipped csproj, import-placement comment included) under `Cities2_Data/Content/Game/.ModdingToolchain/`, and the tool cache at `%CSII_TOOLPATH%`.
Authoritative for how a mod is built, post-processed and published, and for the Unity and package versions a mod **targets** (`%CSII_UNITYVERSION%`, `%CSII_ENTITIESVERSION%`, and the fuller list in `%CSII_UNITYMODPROJECTPATH%/Packages/manifest.json`, whose sources that project resolves and entry 15 reads).
Targets, not runs — the declared Entities version is ahead of the shipped assembly (entry 13).
The full `CSII_*` set is the toolchain's own record of where everything lives: installation, managed, user data, local mods, Paradox mods cache, and the Unity mod project.

It is also the only source for what a mod project _compiles with_. `Mod.props` and `Mod.targets` under `%CSII_TOOLPATH%` are the record of the mod compile's own configuration: `Mod.props` sets no `DefineConstants` at all, which is what settles that a mod compiles without the conditional-compilation symbols the engine's own guards are gated on.
`ModPostProcessor.exe PostProcess --help` is self-documenting and resolves the short flags the targets compose into the post-processing command; nothing else in the pipeline records what they mean.

## 8. The running game — Unity

The sibling `unity-devtools` plugin, over the Mono soft debugger.
Settles what no static read can: live component values, real ECS query results, actual execution order, whether a patch took, the contents of any registry the game builds by reflection at startup, and the load-time context of the city currently open — including the build that wrote its save.
Needs the game running as a debug-patched development build, which `cs2-modding-setup` provisions.

**It also reaches the managed object behind a prefab entity**, which no component on that entity carries: `PrefabSystem.GetPrefab<T>` on the entity holding a data component returns the authoring `PrefabBase`, and its own fields — arrays of other prefabs among them — read directly off it. That is what answers a question like _which mode prefabs does this game mode carry_.

**What it can answer depends on what the game has loaded, not only on whether it is running.**
A question about a mod-declared surface — a serializable component, a mod's own entities, a system a mod registers — needs a mod present that declares one, and with none loaded the question looks unanswerable when it is merely unequipped.
So name what the game must be carrying before recording a live question as unanswerable, and ask for it: the user will install any mod on request, and one from the setup skill's catalog is a minute's work.

**A prefab family is enumerable with an ordinary query.** This game's prefab entities carry `PrefabData` rather than Unity's own prefab tag, so `ecs_query` on a data component returns them directly, labelled through `PrefabSystem.GetPrefabName` — no `PrefabSystem` walk needed. The general ECS caveat that prefab state is invisible to a query does not bind here.
**No established route builds an `EntityQuery` inside `eval`.** Array-creation expressions and the `as` operator are outside the evaluator, `params` expansion fails at overload matching (all in the tool description since 2026-08-10), and the by-ref-like query builder fails on the evaluator's own boxing; a debuggee-side `System.Array.CreateInstance` route — the one `sdb/Ecs.cs` itself uses — stays inside the grammar and untried, and one eval against a running game settles it. A bulk read is therefore `ecs_query` for the entity list plus batched `eval` reads, several entities per call in one interpolated final expression; for a single entity whose values sit one reference away, `ecs_list_components` with `values=true` and `follow="<Component>:<field>"` collapses the two-hop read into one call — it takes one entity per call, so it never replaces the batched route.
**`em.GetComponentData<T>(e)` inside `eval` returns uninitialised memory rather than throwing when `e` does not carry `T`** — the shipped assemblies have the collections checks compiled out (entry 15 owns why), so the failure is silent and the garbage looks like data (plausible denormal floats). Establish presence first with `ecs_query` or `ecs_list_components`; whether `ecs_get_component` shares the behaviour is unobserved.

## 9. The running game — the UI

The sibling `coherent-gameface` plugin, over a direct CDP connection to the Cohtml view.
Settles the live DOM, computed styles, what a component actually renders, and console output from injected JavaScript.
It also reaches simulation-side data the DOM never renders: a C# value binding is readable from the page.
`engine.on("<group>.<name>.update", cb)` followed by `engine.trigger("<group>.<name>.subscribe")` returns the binding's current payload, and the matching `.unsubscribe` trigger ends it — no click, no input, so it is not an act on the user's running game.
**A map binding answers a different verb set**, and the value-binding verbs above will look like a binding that does not exist: `engine.on("<group>.<name>.updateMapEntry", (key, value) => …)`, then `engine.trigger("<group>.<name>.subscribeMapEntry", key)`, released with `engine.trigger("<group>.<name>.unsubscribeMapEntry", key, false)`. The plain `.subscribe` verb does still answer on a map binding rather than erroring — it returns the currently-observed `[key, value]` pairs — so a probe run before anything is subscribed gets an empty array and reads it as absence. The empty array is itself proof the binding exists, since a missing one fires no `.update` at all. That plain subscribe leaves an observer on the map's hidden debug binding, so end it with the matching `.unsubscribe`.
The binding groups are enumerated in the reformatted UI bundle copy, and their writers are `Game.UI.*` systems in the decompile.
The module registry is the route that looks equivalent and is not: `window["cs2/modding"].getModule` throws `ReferenceError` on many mutable exports bundle-wide — dead accessors left by the bundle's packaging, with an error text that points at the wrong cause — while `findModule` never evaluates an accessor and is always safe, so discover with `findModule` and read values through the engine route above. Those two are all `window["cs2/modding"]` exposes: `override`, `extend`, `append` and `reset` are reachable as values only from inside a registrar, or in frame from a breakpoint inside `findModule`/`getModule`, which are declared in the registry object's scope.
Needs the game running with the UI debugging port open.

**Both are ordinary tools when connected, and neither is yours to start.**
When a call fails because the game is not running, the plugin is not installed, or the server has not started, stop the phase and ask, under the root `AGENTS.md` rule on acting on the user's running game.
What this adds is what happens next: the phase resumes on their word rather than on a substitute, since a static read standing in for the live one, or the claim written down as unestablished, are the two ways this gets quietly skipped.

## 10. The wiki

`https://cs2.paradoxwikis.com/`.
Primary for process — toolchain, debugging, publishing, key bindings, options — and it ships as authoritative wherever it is the only source, which for the _why_ and the _when_ of a procedure it usually is.
Where the toolchain itself answers, source 7 wins: the wiki describes the build and the targets are the build, and the wiki's most load-bearing modding page is five versions stale.
The `Map Creation:` family is the exception and is worth checking first for anything about terrain, water, climate or resources: seven pages stamped `{{ParadoxVerifiedAmbox|version=1.6.0 f1}}` and absent from `Category:Potentially outdated`, where a stamped game-concept page carries `{{Version|1.0}}` and is in it. They are editor-facing, so they describe the authoring surface rather than the runtime, and within that they have been right where the concept pages were stale.
An unflagged page is not a current one: `Category:Potentially outdated` is populated by the `{{Version}}` template, so a page that never claimed a version is absent from the category for the wrong reason — `Notifications`, `Development Tree` and `Achievements` are all unstamped and all unverified.
For internals it sets the agenda and is then verified against the game; it has been wrong on capability while right on convention.
A web-fetch tool normally renders past the site's JavaScript bot challenge and returns full article content; a plain HTTP fetch is still expected to lose to it. Ask the user only when the challenge comes back instead of content.
The same tool can refuse a stat table on copyright grounds — re-asking for the page's statistics as a plain data listing gets it — and silently truncates a long page, which `index.php?title=<Title>&action=raw&section=<n>` recovers, with the section index from `api.php?action=parse&prop=sections`.
A DLC or content-pack product page can carry mechanism material the gameplay pages do not, stamped "timeless" rather than verified-for-a-version — a resource-processing chart and the wiki's only statement of the production-milestone unlock system sit on one.
Several plausible titles are bare redirects into a larger page and carry nothing of their own; the wiki's own `Special:Search` endpoint resolves a moved or merged title where a web search does not.
The MediaWiki API is the exact instrument under both problems: `api.php?action=query&titles=<Title>&format=json` reports `missing` for a title that does not exist and a page ID for one that does (titles are case-sensitive after the first character — `Development Tree` and `Map Tiles` exist while `Development tree` and `Map tiles` report `missing`), `index.php?title=<Title>&action=raw` returns the article's wikitext verbatim — which reproduces a table cell for cell and distinguishes "the page says nothing" from "the fetch did not render it" — and `action=query&list=allpages&aplimit=500` enumerates the whole main namespace (roughly 350 pages) in one batch.

## 11. The mod corpus

The checked-out open-source mod repositories. Which ones, and what each demonstrates, is [`../plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md`](../plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md) — the one file that names a mod, and the one that gains an entry as passes learn.
**Input, never output.** It is evidence of what mod authors do, which is a different fact from what the game does.
**It is also a lead generator, and its leads are worth chasing.** A mod contradicting your derivation is one whose author hit that behaviour in a running game. Read the first-party source again before you write. The corpus still settles nothing — but discarding its lead on that ground is how a half-finished derivation survives a pass.

## 12. The Harmony library

Ships with no first-party artifact: it is not in `Cities2_Data/Managed/`, and the toolchain's `Mod.props` and `Mod.targets` never mention it.
Every mod that patches brings its own `0Harmony.dll` through the `Lib.Harmony` package, and the deploy target ships it beside the mod's assembly.
**Authoritative for patch semantics** — the injected parameter names, prefix and postfix ordering, the `ArgumentType` mapping, unpatch filtering — none of which any other source here can answer: the decompiled game says what a patch target contains and nothing about what a patch does, and the wiki mentions the library twice in passing.
Read it by decompiling a copy with `ilspycmd`, taking any `0Harmony.dll` under the Paradox mods cache at `%CSII_USERDATAPATH%/.cache/Mods/pdx_mods/` and checking the assembly identity first: several versions are in circulation and none is strong-named, so which one a given copy is cannot be assumed.

## 13. Unity's own documentation

The engine's manual and package documentation at `https://docs.unity3d.com/`, fetched live. Pages are static HTML served per version, and a plain fetch is normally enough — there is no bot challenge here, unlike the wiki.

**Authoritative for engine mechanism the decompile cannot state** — what the job scheduler does with a dependency chain, why completing a handle costs more than that job's own duration, what a sync point drains, what an allocator's contract is, what the safety system would have caught had it been compiled in. The decompiled engine shows the code; it does not explain the runtime's behaviour around it, and no other source here does either.

**Never authoritative for API shape, names, counts, or what this game's build does.** Two facts about the version are why.

**The declared version is not the version that runs.** `%CSII_ENTITIESVERSION%` and the mod project's manifest at `%CSII_UNITYMODPROJECTPATH%/Packages/manifest.json` declare Entities 1.3.10, Collections 2.5.7, Burst 1.8.23 and Mathematics 1.3.2 — what a mod compiles against. The shipped `Unity.Entities.dll` is **1.3.5 to 1.3.8**. No Unity package assembly states its **package** version — the `VersionInternal` attribute entry 1 describes is Colossal's own, and the four packages that matter all report `AssemblyVersion("0.0.0.0")` — so re-dating one is an investigation: [dating a shipped Unity package](solutions/dating-a-shipped-unity-package.md) carries the method and the four traps, and the other three packages have not been dated this way. The gap is benign because nothing a mod plausibly calls changed across it, and this manifest is where a wider one would show first.

**The assembly is not stock.** `Colossal.CORuntimeApplication` ships inside `Unity.Entities`, appears in no Unity release, and calls an `internal` method, so it can only have been compiled in from source. Colossal build Entities themselves, and the docs therefore describe a package adjacent to the one that runs.

So read the docs for the mechanism, then read the decompile for what this build actually contains, and where they disagree the decompile wins without argument.

Three URL shapes, and all three must carry a version:

- **Editor manual**, `https://docs.unity3d.com/2022.3/Documentation/Manual/<Page>.html`. The game runs Unity 2022.3.71f1, so this one pins exactly. It owns the low-level job system: `JobSystem.html` is the entry point.
- **Package manual**, `https://docs.unity3d.com/Packages/com.unity.<package>@<major.minor>/manual/<page>.html`, with `/api/<Type>.html` for the scripting reference and `/changelog/CHANGELOG.html` for when a behaviour changed. The packages that matter are `entities`, `collections` and `burst`. Read `@1.3` for Entities, because that is the branch this game's assembly sits on; `@1.4` resolves and describes a package this build does not run.
- Page names moved between major versions and guessing one wastes a fetch — `sync_points.html` under `@0.50` became `performance-sync-points.html` under `@1.3`. Search with `allowed_domains: ["docs.unity3d.com"]` to get the current name rather than composing URLs by hand.

**An unversioned URL is the trap.** `docs.unity3d.com/Manual/<Page>.html` without a version segment redirects to the newest Editor and serves it: `Manual/JobSystem.html` 301s to `Manual/job-system.html` and returns a Unity 6 page. Nothing fails and nothing on the page objects, so a version segment on every fetch is what keeps you on this game's Editor.

## 14. The game's own logs

The `Logs/` directory under `%CSII_USERDATAPATH%`, plus `Player.log` and `Player-prev.log` at that path's root.
First-party, version-stamped, readable with the game closed — and the only artifact that records what one _particular run_ did, which is what separates it from every other source here.

**Authoritative for what one run did**: which launch flags were passed — in `SceneFlow.log`, as a single `Command line:` entry at the head of the file listing the executable and every argument on its own line — the game and Unity versions, whether the build is a development one, which mods were in the active playset, which assemblies loaded and how long each took, and the last thing a process that died printed.
Flag _names_ only — the value of every `name=value` argument is masked before it is written, so the logs never settle what a flag was set to.
Each logger writes its own `<Name>.log`, and `Player.log` sits at the root beside the directory rather than inside it.

Two properties decide whether a log can answer your question at all, and both are the shipped `diagnostics` reference's to explain: which severities reach which file, and that a file is truncated at each session's first message rather than appended, so only `Player-prev.log` survives a relaunch.
`FallbackSettings.coc` at the same root governs what these files contain — it holds the persisted settings for every logger, so it is read with them rather than separately.

## 15. The Unity package sources on the machine

`Library/PackageCache/` under `%CSII_UNITYMODPROJECTPATH%`, one directory per package of entry 7's manifest — `com.unity.entities`, `com.unity.collections`, `com.unity.burst`, `com.unity.mathematics` and the rest — as full C# source with the upstream `#if` directives intact.
`Library/` is generated by Unity resolving the project rather than shipped with the toolchain, so check the directory is there before citing it.

**Authoritative for the source a mod compiles against, and for nothing about what runs.** It holds the declared version, which this file's entry 13 records as ahead of the shipped assembly, and the shipped assembly is not stock — so the decompile wins on every question about the game's build exactly as it wins over the docs.

What it answers that nothing else here can: **what the conditional-compilation guards contained.** They gate the safety and validation code out of the shipped assemblies, so the decompile shows an absence and never the text; reading the two side by side settles what was taken out, and `ComponentTypeHandle`'s `m_Safety` field — under `#if ENABLE_UNITY_COLLECTIONS_CHECKS` in the package source, simply gone from the decompiled struct — is the shortest demonstration. Entry 13 owns what the safety system would have _done_; this one owns what it was.

**One pattern finds every guard, and it is `ENABLE_UNITY_COLLECTIONS_CHECKS`.** `UNITY_DOTS_DEBUG` gates a large minority of the same checks and never stands alone, so it widens what a build could have kept and adds nothing to a search. That is worth knowing before writing the second pattern, because a symbol that only ever appears as an alternative looks from a hit list exactly like one that gates code of its own.

It also saves a network round trip on the declared version, which [dating a shipped Unity package](solutions/dating-a-shipped-unity-package.md) otherwise reaches through Unity's needle-mirror tags. It is not a substitute there: dating compares arbitrary releases and this holds exactly one.

---

## What looks like a source and is not

- **First-party files vendored into a mod repository** — a copy of the UI bundle, a `types/*.d.ts`. The install supersedes them, and where a copy disagrees with it, assume the copy is stale rather than wrong: the failure mode is recording what the copy lacks as something the game lacks.
- **The user's installed mods** — the built assemblies under `%CSII_LOCALMODSPATH%` and the Paradox cache. Compiled, not source, and whoever wrote one may not be in the corpus at all, so they settle nothing about how a mod works. The Paradox cache alone settles one thing they cannot: how common something is, since the set of files a mod ships is readable without decompiling anything and that cache is the only sample of the ecosystem this pipeline can reach that nobody selected for it. Use **the Paradox cache** for prevalence and never for a claim about how a mod works, say how many mods the count was over, and do not read `%CSII_LOCALMODSPATH%` this way — it holds what the user themselves built and is a sample of nothing.
  This bars the cache as evidence about mods; it is also where a copy of a _library_ a mod ships is reached, which is source 12 and a different question.
- **The `.coc` files at the `%CSII_USERDATAPATH%` root** — one per mod, plus the game's own. These are saved settings, not code and not content, however much the extension resembles the `.cok` packages. The exception is `FallbackSettings.coc`, which governs what entry 14 contains and is filed there.
