# Sources

Every source the `cs2-modding` pipeline may read, in one place: what each one is, what it is authoritative for, and how to reach it.

**This list grows from the passes that use it.** Every artifact on it was unlisted until somebody went looking, and a first-party artifact sitting unread on the machine looks exactly like one that does not exist. So a pass that reaches something this file does not name, or finds an entry wrong — a path moved, a format not as described, a narrower authority than claimed — amends it and says so, the way a discovery pass amends the mod catalog. A list only its maintainer can grow is a snapshot of the day it was written.

**Precedence: first-party beats everything, and which first-party source wins depends on how the subject ships.**
The decompiled C# and the installed game are both the game itself. The install is the one that carries a version the agent can state, and it holds whole subsystems the C# never names.
Where a topic's subject matter ships as data or as JavaScript, the install outranks the decompile on anything it can answer. Where it ships as C#, the decompile is ground truth and nothing moves.
The official toolchain is first-party too, and authoritative for its own half — how a mod is built, post-processed and published, and what a UI mod may import. It does not outrank the game on anything about the game, and the game does not outrank it on anything about the build.
So among first-party sources the tie-break is which one **owns** the subject, not which sits higher on a list.
Below them the order is flat and absolute. The wiki ships as authoritative only where nothing first-party covers the subject, and is a lead generator everywhere else. The mod corpus never settles anything about the game: it is evidence of what mod authors do, which is a different fact.

**Locating a source.** Where the official toolchain is installed it sets `CSII_*` environment variables naming most of these paths, and each entry below gives the variable where one exists. Expect a variable to be missing rather than assuming it: an agent whose session started before the toolchain did will not have inherited them.
Three roots are the user's own choice and no variable names them — the decompile, the mod corpus, and the reformatted UI bundle, the last of which the record also stores a line count for. The record is `~/.cs2-modding/setup.md`, which the setup skill writes and every skill reads before touching a local source; it is the route that works on both supported harnesses, so read it first. Under Claude Code the agent memory note `cs2-modding-source-paths` carries the same paths and is faster.
Finding neither, ask the user to run the setup rather than guessing a path.

---

## 1. The decompiled game

The C# of every shipped assembly, decompiled from the install's own managed DLLs.
**Ground truth for anything C# names**: systems, components, prefabs, the modding API, serialization, the binding layer's C# half.
Blind to the frontend entirely, and to any behaviour that lives in shipped data rather than in code. Grepping it and finding nothing proves nothing about either.
Reach it under the checkout's `src/`, one directory per assembly.

## 2. The game's managed assemblies

`Cities2_Data/Managed/` at `%CSII_MANAGEDPATH%`.
The decompile's origin, and what a mod project compiles against.
Read these directly only to confirm what shipped when the decompile is stale or a type is missing from it; the decompile is the readable form of the same thing.

## 3. The game's UI bundle

`Cities2_Data/Content/Game/UI/` under `%CSII_INSTALLATIONPATH%` — `index.js`, `index.css`, `index.html`, beside `Fonts/`, `Media/` and `Static/`.
**First-party and authoritative for the whole frontend**: the module registry and its paths, the React component tree, every number and string formatter, the wire format a binding arrives in, the exported package surface.
`index.js` ships minified to a single line, which costs less than it looks. Grepping it settles presence and absence exactly as usual; only `grep -c` misleads, counting matching lines and so answering 1 or 0 however many hits there are — `grep -o <pattern> | wc -l` gives the real occurrence count.
What the single line genuinely blocks is reading around a match and citing one, and those need a reformatted copy.
**Reformat with prettier at its defaults**, and check your copy's line count against the one the citing file's baseline states before trusting a line number from it. A differing count means the citations do not resolve against your copy; a matching one is good enough to go on. The reformatted copy is one of the roots the record names.

## 4. The game's compiled locale data

`Cities2_Data/Content/Game/Locale.cok`, a plain zip holding most locales, and `Cities2_Data/StreamingAssets/uk-UA.loc`, the one that ships loose beside it, under `%CSII_INSTALLATIONPATH%`.
Reading only the package silently misses that last one.
**Authoritative for every vanilla string and for the localization-key namespace table.**
A `.loc` payload is a flat `BinaryWriter` stream with no compression, no checksum and no table of contents, so a zip reader and a `BinaryReader` get the whole of it.

## 5. The game's packaged content

The `Blob*.cok` set under `Cities2_Data/Content/Game/`, plus the DLC and radio-pack directories under `Cities2_Data/Content/`, at `%CSII_INSTALLATIONPATH%`.
The shipped prefabs, assets and their data, in the asset-database package format.
This pipeline has not opened one, so the format claim above is untested here.
They are where a prefab, asset or content-pack question goes when the decompile only shows the loader.

## 6. The official UI mod scaffold

`@colossalorder/create-csii-ui-mod`, an npm package installed globally by the toolchain. Its `template/types/` holds one declaration file per importable module.
**First-party and authoritative for what a UI mod may import**, module by module: `cs2/bindings` (every binding group's payload types, and by far the largest), `cs2/ui`, `cs2/input`, `cs2/l10n`, `cs2/api`, `cs2/modding` (the module registry's five operations and its append-hook targets), `cs2/utils`, `cs2/assets`, plus the Cohtml and React ambient declarations.
The template beside them also carries the reference `webpack.config.js`, `tsconfig.json` and `mod.json`.

## 7. The official modding toolchain

MSBuild targets in the `cs2-moddingtools` NuGet package, plus the `ModPostProcessor` and `ModPublisher` executables under `Cities2_Data/Content/Game/.ModdingToolchain/`, and the tool cache at `%CSII_TOOLPATH%`.
Authoritative for how a mod is built, post-processed and published, and for the Unity version a mod targets (`%CSII_UNITYVERSION%`, `%CSII_ENTITIESVERSION%`).
The full `CSII_*` set is the toolchain's own record of where everything lives: installation, managed, user data, local mods, Paradox mods cache, and the Unity mod project.

## 8. The running game — Unity

The sibling `unity-devtools` plugin, over the Mono soft debugger.
Settles what no static read can: live component values, real ECS query results, actual execution order, whether a patch took.
Needs the game running as a debug-patched development build, which `cs2-modding-setup` provisions.

## 9. The running game — the UI

The sibling `coherent-gameface` plugin, over a direct CDP connection to the Cohtml view.
Settles the live DOM, computed styles, what a component actually renders, and console output from injected JavaScript.
Needs the game running with the UI debugging port open.

**Both are ordinary tools when connected.** When a call fails because the game is not running, the plugin is not installed, or the server has not started, ask the user to start what is missing rather than recording the question as unanswerable.

## 10. The wiki

`https://cs2.paradoxwikis.com/`.
Primary for process — toolchain, debugging, publishing, key bindings, options — and it ships as authoritative wherever it is the only source, which for the _why_ and the _when_ of a procedure it usually is.
Where the toolchain itself answers, source 7 wins: the wiki describes the build and the targets are the build, and the wiki's most load-bearing modding page is five versions stale.
For internals it sets the agenda and is then verified against the game; it has been wrong on capability while right on convention.
A plain fetch usually loses to the site's JavaScript bot challenge. Try a web-fetch tool, and ask the user when it comes back with the challenge instead of content.

## 11. The mod corpus

Twenty checked-out open-source mod repositories. Which ones, and what each demonstrates, is [`../plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md`](../plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md) — the one file that names a mod, and the one that gains an entry as passes learn.
**Input, never output.** It is evidence of what mod authors do, which is a different fact from what the game does.

---

## What looks like a source and is not

- **First-party files vendored into a mod repository** — a copy of the UI bundle, a `types/*.d.ts`. The install supersedes them, and where a copy disagrees with it, assume the copy is stale rather than wrong: the failure mode is recording what the copy lacks as something the game lacks.
- **The user's installed mods** — the built assemblies under `%CSII_LOCALMODSPATH%` and the Paradox cache. Compiled, not source, and whoever wrote one may not be in the corpus at all.
- **The `.coc` files at the `%CSII_USERDATAPATH%` root** — one per mod, plus the game's own. These are saved settings, not code and not content, however much the extension resembles the `.cok` packages.
