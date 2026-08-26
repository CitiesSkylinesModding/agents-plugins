# The frontend as source (the UI bundle, the module registry, injection)

**Baseline.** Installed game 1.6.0f1 at `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II`; decompiled game 1.6.0f1 (`src/Game/Properties/AssemblyInfo.cs`, `VersionInternal("1.6.0f1 (419.d6c6) [6216.19404]")`).
Frontend claims cite two reformatted copies of the shipped bundle, both produced with prettier at its defaults: `Cities2_Data/Content/Game/UI/index.js` as `DecompiledCitiesSkylines2/src-ui/source.js`, **135,021 lines**, and `Cities2_Data/Content/Game/UI/index.css` as `DecompiledCitiesSkylines2/src-ui/source.css`, **24,902 lines**. Check a fresh reformat against those counts before trusting a line number.
Scaffold citations are to `@colossalorder/create-csii-ui-mod` version `1.0.0` (`create-csii-ui-mod/package.json:3`); `create-csii-ui-mod/<path>` cites expand through the npm-global junction to `<install>/Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod/<path>`, the game's own files, versioned by the install and not by that npm version.
Mod corpus read 2026-08-23 at the commits the 22-repository checkout carried.
Wiki `UI Modding` fetched live 2026-08-23 through `index.php?action=raw` (no bot challenge, no snapshot substitution needed); it is stamped `{{ParadoxVerifiedAmbox|version=1.5.7 f1}}`, one version behind the install.

**Ruled (2026-08-24, the ticket 35 orchestrating session under the maintainer's delegation; conflicts.md).** Governs every scaffold cite in this file: the short `create-csii-ui-mod/<path>` form stays, declared in `README.md`'s citation list as an alias whose expansion each citing file's baseline states once — the baseline above does — so a cite stays short while the reader still learns the files behind it are the game's own, versioned by the install and not by the inert npm `1.0.0`.

Claims marked **Settled live** were read on 2026-08-23 from the running game over the `coherent-gameface` plugin (Cohtml 1.64.0.7, `assetdb://gameui/index.html`) and, where a C# call was needed, the `unity-devtools` plugin. The playset carried three UI mods — Hall of Fame, Find It and Skyve.

---

## Findings

### The module registry is the whole injection mechanism, and it is one object with eight operations plus a map

The bundle builds it once, as an object literal over three module-scope containers: `$` the registry `Map`, `q` the override backup, `X` the set of anchors that have been appended to (`source.js:13370-13373`). The registrar every mod exports is handed exactly this object.

The scaffold declares the same shape (`create-csii-ui-mod/template/types/modding.d.ts:7-21`):

| Member | Declaration (`modding.d.ts`) | Implementation |
| --- | --- | --- |
| `get(modulePath, exportName): any` | `:8` | `source.js:13374-13378` |
| `add(modulePath, module): void` | `:9` | `:13379-13383` |
| `override(modulePath, exportName, newValue): void` | `:10` | `:13384-13388` |
| `extend(modulePath, exportNameOrSCSSValue, extendCb?): void` | `:11` | `:13389-13408` |
| `append(modulePath, exportName, appendedComponent?, index?): void` | `:12` | `:13409-13456` |
| `append(target, appendedComponent, index?, _?: never): void` | `:13` | same body, first branch |
| `hasAppend(target): boolean` | `:14` | `:13476` |
| `registry: Map<string, Record<string, any>>` | `:15` | `:13457` |
| `find(query): [path, ...exports][]` | `:16-19` | `:13458-13470` |
| `reset(): void` | `:20` | `:13471-13475` |

Verdict on the seed survey's count: `survey-mods-techniques.md:316-317` says "Two operations", naming `append` and `extend`. That is an accurate census of what the corpus at 12 repositories used and not a description of the API. The declaration and the implementation agree on ten members, and `get`, `registry` and `find` are the discovery half a mod needs before it can write either of the two the survey saw.

`get` throws a **string**, not an `Error`: `` `Module ${e}@${t} was not found.` `` (`:13377`), and `add` throws `` `Module ${e} was already registered. If you want to override the exports of this module use the override API` `` (`:13381`). `override` and `extend` throw `` `Module ${e} was not found.` `` (`:13386`, `:13390`). A `catch` block that reads `err.message` gets `undefined` on all four.

**`find` never evaluates an accessor and `get` always does.** `find` walks the map with `for (const t in s)` over property *names* (`:13462`), so a getter is never invoked; `get` reads `n[t]` (`:13376`). That is the mechanism behind `docs/SOURCES.md:112`.

`find` filters export names first and returns `[path, ...matchingExports]` when any matched, and only when none did does it test the *path* and return `[path, ...allExports]` (`:13463-13467`). So a query matching both a path and one of its exports comes back with the export list narrowed, not with the whole module. `find("")` matches everything, since `"".includes` is true of every string — that is the way to enumerate the registry.

Rots: every member name above, and the throw texts. Re-check against `source.js` at the `Q = {` object and against `modding.d.ts`.

### The registry holds 1,386 modules, and 582 of its 2,994 exports throw when read

**Settled live.** `findModule("")` returns **1,386** entries, and calling `getModule(path, name)` on every export of every one of them succeeds 2,412 times and throws `ReferenceError` **582** times, across **64** modules. The static extraction of `Q.add(` call sites from the reformatted bundle gives the same 1,386.

The composition, from the same enumeration (live) and from the extracted path list (static, identical):

- by extension: 710 `.tsx`, 506 `.module.scss`, 167 `.ts`, plus one each of `.theme.module.scss`, `.generated.ts` and `.bound.tsx`;
- by top-level segment under `game-ui/`: `game` 617, `common` 346, `menu` 178, `editor` 159, `debug` 42, `overlay` 28, `ui` 8, `widgets` 4, `modding` 2, `api` 1, and `game-ui/index.tsx` itself.

**The dead exports are a bundler artifact with a greppable tell.** A live export registers as `get X() { return <minified-identifier>; }`; a dead one registers as `get X() { return X; }` — a getter returning its own name, where no binding by that name exists in the emitted scope. `game-ui/game/data-binding/infoview-bindings.ts` opens with three of them and then a live one (`source.js:34842-34863`: `get infoviews() { return infoviews; }` beside `get closeInfoviewMenu() { return Rf; }`). A regex for self-returning getters over the reformatted bundle finds 581 of the 582; the one-unit gap is unexplained and the live count is the one to trust, because it was produced by calling the accessor rather than by pattern-matching it.

**They are concentrated exactly where a mod would reach.** The eight worst modules, live: `game/data-binding/infoview-bindings.ts` 141, `game/data-binding/tool-bindings.ts` 37, `menu/data-binding/menu-bindings.ts` 32, `menu/data-binding/options-bindings.ts` 23, `common/data-binding/input-bindings.ts` 16, `game/data-binding/toolbar-bindings.ts` 16, `game/data-binding/tutorial-bindings.ts` 15, `common/data-binding/app-bindings.ts` 14. Every one is a `*-bindings.ts`, and the dead names are the binding objects themselves. So "read a vanilla binding's value through `getModule`" is the single most likely way to meet this, which is why `docs/SOURCES.md:108-112` sends that question to `engine.on`/`engine.trigger` instead.

**Twenty-two modules are registered with no exports at all** (live): `game-ui/api/index.ts`, `game-ui/ui/index.ts`, `game-ui/common/localization/index.ts`, `game-ui/common/focus/index.ts`, `game-ui/common/input-events/index.ts`, `game-ui/common/svg/index.ts`, `game-ui/common/svg/elements/index.ts`, `game-ui/editor/widgets/item/index.ts`, `game-ui/index.tsx`, `game-ui/common/image/missing-icon-handler.ts`, `game-ui/common/hooks/use-window-size.ts`, `game-ui/common/hooks/use-bounding-client-rect.ts`, `game-ui/common/focus/controller/focus-controller-base.ts`, `game-ui/common/input-events/input-stack.tsx`, `game-ui/common/text/renderers/markup-renderer.tsx`, `game-ui/common/scrolling/scroll-controller.ts`, `game-ui/common/input/clickable-wrapper.tsx`, `game-ui/game/data-binding/budget-panel-types.ts`, `game-ui/game/data-binding/infoview-types.ts`, `game-ui/game/components/tutorials/tutorial-target/tutorial-target-manager.ts`, `game-ui/game/components/tutorials/tutorial-container.tsx`, `game-ui/game/components/economy-panel/production-page/resource-detail/production-chain-diagram/flow-diagram.tsx`. The barrel `index.ts` files are the notable ones: `game-ui/api/index.ts` is registered as a bare `Q.add("game-ui/api/index.ts", {})` on one line (`source.js:25980`), so `cs2/api`'s functions are **not** reachable through the registry — only through the window global and through `game-ui/common/data-binding/binding.ts`.

Rots: all counts, and every module path named. Re-run the live enumeration against a new build rather than re-deriving from a stale bundle.

### Eight append anchors exist in the bundle, and the scaffold's union names seven

`AppendHookTargets` is declared as `"Menu" | "Editor" | "Game" | "GameTopLeft" | "GameTopRight" | "GameBottomRight" | "UniversalModMenu"` (`create-csii-ui-mod/template/types/modding.d.ts:6`).

The bundle renders **eight** `ModdingHook` instances, each with a `name` prop, and the eighth is `GameBottomLeft`:

| Anchor | Rendered at | Module and export it sits in | What hosts it |
| --- | --- | --- | --- |
| `Menu` | `source.js:133807` | `game-ui/menu/components/main-menu-screen/main-menu-screen.tsx` (`:133905`) | last child of the main menu screen, after the button column and the Paradox/notifications column |
| `Editor` | `:132906` | `game-ui/editor/components/editor-main-screen.tsx` (`:132916`) | late in the editor screen's children, under the tutorial container and the tooltip layer, after the toolbar and the pause-menu toggle |
| `Game` | `:130541` | `game-ui/game/components/game-main-screen.tsx` (`:130620`, export `GameMainScreen`) | last child of the in-game screen, after every panel layer and both unlock modals — a free overlay layer |
| `GameTopLeft` | `:130418` | same, inside the `infoMenuLayout` div beside the infoview-menu toggle | **conditional**: the surrounding branch renders `topLayout` or `infoMenuLayout`, and the hook is only in the second |
| `GameTopRight` | `:130566` | same, first child of the `pauseMenuLayout` div, above the advisor toggle | rendered in both layouts (`:130406` inside the gamepad-scheme branch's top layout, `:130481` as `!j && …` for the mouse scheme), so absent only under the gamepad scheme with the top layout hidden |
| `GameBottomRight` | `:127077` | `game-ui/game/components/right-menu/right-menu.tsx` (`:127253`, export `RightMenu`) | first child of the right menu, above the notifications button |
| `GameBottomLeft` | `:126118` | `game-ui/game/components/left-menu/left-menu.tsx` (`:126120`, export `LeftMenu`) | **the entire component**: a `<div class="left-menu_L1D">` whose only child is the hook |
| `UniversalModMenu` | `:115052` | `game-ui/game/components/universal-mod-panel/universal-mod-panel.tsx` (`:115057`, export `UniversalModPanel`) | the button container inside a scrollable panel |

**Settled live.** `document.querySelectorAll(".left-menu_L1D").length` is 1 in a loaded city with no mod appending there, so the `GameBottomLeft` host is a real, always-present DOM node.

Verdict: **the bundle wins and `GameBottomLeft` exists.** `append` does not validate its target against any list — the anchor form only stores the string in `X` and compares it to the rendered hook's `name` prop at render time (`source.js:13415`, `:13421`) — so `moduleRegistry.append('GameBottomLeft', C)` works at runtime and fails TypeScript against the shipped declaration. The wiki lists the same seven and no more, describing `GameTopLeft, GameTopRight` and `GameBottomRight` as "for appending mod trigger buttons" (https://cs2.paradoxwikis.com/UI_Modding). The scaffold is `docs/SOURCES.md`'s authority for what a mod may import; it is not the authority for what the game renders.

**The union has grown before, which is the shape this is.** Seventeen corpus repositories vendor their own copy of `modding.d.ts` (eighteen copies — Time2Work carries two), and fourteen of those copies declare a **six**-member union with no `UniversalModMenu` (for example `Traffic/UI/types/modding.d.ts:6` and `FindIt-CSII/FindIt/UI/types/modding.d.ts:6`), against four carrying the current seven (`Anarchy/Anarchy/UI/types/modding.d.ts:6`, `ExtraDetailingTools/UI/types/modding.d.ts:6`, and two copies in `Time2Work`). A vendored first-party file settles nothing about the install — `docs/SOURCES.md:216` bars exactly that — but the spread is evidence about the declaration itself: the anchor list grew from six to seven within the corpus's lifetime, and the bundle is currently one ahead of it again.

That settles the fact and leaves a judgement the verdict cannot make: whether shipped prose hands a reader an anchor their compiler rejects. It was put to `conflicts.md` under "Two first-party sources disagree about the mod-facing UI API", together with the `append` index parameter in the composition finding below, which is the same disagreement in its second instance.

**Ruled (2026-08-23, the ticket 34 orchestrating session under the maintainer's delegation; conflicts.md).** State both, as the split they are. The reference ships the seven typed anchors **and** `GameBottomLeft` as rendered and untyped, in its own voice, with the two routes past the compiler — widen the union in the mod's own copy of `types/modding.d.ts`, which is the mod's file to edit, or cast the string through `unknown` — and one sentence that this anchor is the one most likely to be typed next, since the union has grown once already. Both claims carry `VOLATILE:` naming the bundle's `ModdingHook` render sites and the scaffold's union, because the declaration is the half that moves. Nothing goes to `ui-build-and-devloop`.

`ModdingHook` itself is one line — `(e) => <TransitionGroupCoordinator>{e.children}</TransitionGroupCoordinator>` (`source.js:74469`, registered at `:74470-74477`, the coordinator at `game-ui/common/animations/transition-group-coordinator.tsx`, `:30491`). So everything appended at an anchor is mounted inside a transition coordinator that tracks child add/remove for exit animations.

**`hasAppend` has exactly one consumer and it is a visible feature.** `right-menu.tsx` computes `Q.hasAppend("UniversalModMenu")` in its body (`:127073`) and gates the mods-menu button on it (`:127175-127190`, icon `Media/Glyphs/ParadoxMods.svg`, shortcut `"Universal Mod Panel"`). With no mod appending to that anchor the button does not exist, so the panel is unreachable. This is the one anchor whose entry point a mod has to create by using it.

Rots: the eight anchor names, the module paths and export names in the table, and `left-menu_L1D`.

### How a mod's module reaches the page, and the two named exports the loader reads

The C# side registers each `.mjs` found in the asset database under one shared `ui-mods` host and publishes its URL: `AddHostLocation("ui-mods", Path.GetDirectoryName(uiModuleAsset.path), uiModuleAsset.isLocal)` then `AddActiveUIModLocation(couiPath)`, where `couiPath => "coui://ui-mods/" + Path.GetFileName(path)` (`src/Game/Game.Modding/ModManager.cs:461-473`, `:475-483`; `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/UIModuleAsset.cs:41`). The list lands on the `app.activeUIModsLocation` value binding (`src/Game/Game.UI/AppBindings.cs:206`, updated at `:242-268`), which the frontend reads as `Xr("app", "activeUIModsLocation")` (`source.js:40287`).

**Settled live**: that binding currently reads `["coui://ui-mods/HallOfFame.mjs", "coui://ui-mods/FindIt.mjs", "coui://ui-mods/Skyve Mod.mjs"]` — one flat list of `coui://ui-mods/<file>.mjs` URLs, spaces and all.

The loader is a `useEffect` in the root component (`source.js:134914-134947`), and it does five things:

1. `import(url)` each URL, dynamically, as an ES module (`:134931`).
2. Push `t.default` into an array in **import-completion order**, not list order (`:134933`).
3. If the module also exports a truthy **`hasCSS`**, derive the stylesheet URL by `url.replace(".mjs", ".css")` and load it (`:134933-134937`).
4. Once every import has settled, call `pR(array)` — which is `Q.reset()`, `gR.clear()`, then `for (const t of array) t(Q)` (`:47116-47121`).
5. Gate the whole UI on `ready`, so nothing renders until every mod has been imported or has failed (`:134947`, consumed at `:134950`).

The stylesheet loader is `game-ui/modding/utils/load-css.tsx` → `loadCss` (`:47090-47115`): an `XMLHttpRequest` GET, and on HTTP 200 a `<link rel="stylesheet" id="<basename without .css>" href="<url>">` appended to `document.head`.

**Settled live**: `document.querySelectorAll("link")` returns `assetdb://gameui/index.css` (the game's own, from `index.html`) plus `<link id="FindIt" href="coui://ui-mods/FindIt.css">` and `<link id="HallOfFame" href="coui://ui-mods/HallOfFame.css">`. The `id` is the mod id, because the file is named after it.

**So a UI module's contract with the page is two named ESM exports: `default`, the registrar, and `hasCSS`, a boolean.** The scaffold's own validation file says otherwise — "only default export is processed by the UI, any named exports will be ignored" (`create-csii-ui-mod/template/types/validateTypes.ts:4`) — and the scaffold's build contradicts its own comment, injecting `const hasCSS = <bool>; export { hasCSS, …}` into the built `.mjs` from a plugin (`create-csii-ui-mod/template/tools/css-presence.js:12-26`). Verdict: **the bundle wins; `hasCSS` is read.** Confirmed on a built artifact, whose module ends `export{As as default,Ps as hasCSS}` (`%CSII_LOCALMODSPATH%/HallOfFame/HallOfFame.mjs`). A mod that ships CSS and no `hasCSS` export gets an unstyled UI with nothing logged.

**Four failure modes ride in those six lines, and all four are silent.**

- **An import that rejects is swallowed.** `.catch((e) => { o(); })` (`:134940-134942`) discards the error and counts the module as settled. Nothing reaches the console.
- **A module with no `default` export poisons the array.** `a.push(t.default)` runs unconditionally (`:134933`), so `undefined` enters the registrar list.
- **`pR`'s loop has no `try`/`catch`** (`:47120`). A registrar that throws — or an `undefined` entry from the previous point — aborts the loop, and **every mod later in the array never registers at all**. Since the array is in import-completion order, which mods survive varies between runs of the same playset.

- **A `hasCSS` whose stylesheet never answers 200 hangs the whole UI.** The loader counts one extra expected completion for the stylesheet (`s++`, `:134934`) and `mR` calls back only inside `if (200 === n.status)` and the link's `onload` (`:47090-47105`), so the completion counter never reaches its target, `ready` never flips, and the whole app tree — gated on it at `:134974-134975` — never renders. A built module whose `hasCSS` is true and whose `.css` is missing or misnamed blanks the game's UI rather than its own.

**Verdict (2026-08-23, the review gate): the gate is already open at game start.** The effect's first run is over the empty binding and takes the else branch that sets `ready` (`:134945`), because `InitializeModManager` runs well after `InitializeUI` (`GameManager.cs:593`, `:618`) and nothing ever unsets the flag. So a registrar throw or a `hasCSS` hang blanks the UI only on a reload of the page whose first run carries a populated list — the dev loop's case — and at boot leaves every mod unregistered under a vanilla-rendering UI instead.

A defensive registrar wraps its own body in `try`/`catch` for its neighbours' sake rather than its own, which is what Hall of Fame's built module does (its registrar body is `try { … } catch(e){ return <handler>(e, true) }`, `%CSII_LOCALMODSPATH%/HallOfFame/HallOfFame.mjs`).

**`Q.reset()` runs before the registrars, every time the mod list changes** (`:47119`), and the effect's dependency is the binding value (`:134946`). So enabling or disabling a UI mod mid-session re-runs every registrar from a baseline whose overrides are restored — not a clean slate: the registry `Map`, an SCSS class map and any object mutated in place carry over (the 2026-08-23 verdict below, and `reset()` at `:13471-13475`).

**All mods share one `ui-mods` host, so file names collide across mods** — the host is a priority-ordered list of directories and the first that reads wins (`src/Colossal.UI/Colossal.UI/DefaultResourceHandler.cs:599-623`). That is why the scaffold names the webpack entry after the mod id (`create-csii-ui-mod/template/webpack.config.js:28-30`) rather than `index`.

Prevalence, from the Paradox mods cache and for prevalence only (`docs/SOURCES.md:217`): of 341 versioned mod directories covering 302 distinct mod ids, 49 directories carry a `.mjs` (30 distinct file names) and 39 carry a `.css`. So roughly one installed mod in ten has a frontend half, and about four in five of those ship a stylesheet.

Rots: `app.activeUIModsLocation`, the `hasCSS` export name, the `.mjs`→`.css` derivation, and `coui://ui-mods/`.

### The extension points the corpus has proven, path by path

**Nineteen of the 22 corpus repositories carry a UI registrar** — every one except `PlopTheGrowables`, `SceneExplorer` and `ExtraAssetsImporter` (swept for `ModRegistrar` across every `.ts`/`.tsx`). Between them they name **twenty** distinct game module paths, and every one was confirmed present in the 1.6.0f1 bundle registry under the export name the mod uses. The corpus is the lead and the bundle is the authority.

| Module path (registry) | Export | Registered at | What extending it does | Corpus |
| --- | --- | --- | --- | --- |
| `game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx` | `MouseToolOptions` | `source.js:80893-80898` | adds rows to the mouse tool-options panel | 11 repos |
| same | `Section` | `:80899-80904` | the row-with-title wrapper the game's own rows use | via `VanillaComponentResolver` |
| same | `DistanceSection`, `vegetationAgeOptions` | `:80905-80916` | two further reusable pieces | none |
| `game-ui/game/components/tool-options/gamepad-tool-options/gamepad-tool-options.tsx` | `GamepadToolOptions` | `:81989` | the gamepad twin of the above | `Anarchy/Anarchy/UI/src/index.tsx:25` |
| `game-ui/game/components/tool-options/tool-options-panel.tsx` | `useToolOptionsVisible` | `:82182-82187` | forces the panel to appear for a tool the game does not know | 9 repos |
| same | `ToolOptionsPanel` | `:82170-82175` | replaces the panel frame outright | `FindIt-CSII/FindIt/UI/src/index.tsx:23`, `CS2-Platter/Platter/UI/src/index.tsx:23-27` |
| same | `ToolOptions` | `:82176-82181` | the panel's inner content | none |
| `game-ui/game/components/tool-options/tool-button/tool-button.tsx` | `ToolButton`, `ValueToolButton`, `StepToolButton` | `:79907-79924` | the three button shapes tool rows are built from | via `VanillaComponentResolver` |
| `game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx` | `selectedInfoSectionComponents` | `:125266-125271` | a map from a section's `__Type` to its component; adding a key adds a section | `Anarchy`, `Recolor`, `CS2-Platter` (×2), `InfoLoom` (×5), `ExtraDetailingTools`, `Time2Work` |
| same | `CUSTOMIZE_TAB_SECTIONS` | `:125272-125277` | a `Set` of the section types routed to the customize tab | commented out at `ExtraDetailingTools/UI/src/index.tsx:23` |
| `game-ui/game/components/right-menu/right-menu.tsx` | `RightMenu` | `:127260` | the bottom-right button column | `Anarchy/…:30`, `FindIt-CSII/…:22`, `RoadBuilder-CSII/RoadBuilder/UI/src/index.tsx:15` |
| `game-ui/game/components/toolbar/top/toggles.tsx` | `PhotoModeToggle` | `:129342` | hijacking a known toolbar toggle to add a button beside it | `CS2-MoveIt/UI/src/index.tsx:14`, `FindIt-CSII/…:26` |
| `game-ui/game/components/asset-menu/asset-menu.tsx` | `AssetMenu` | `:90476` | the asset picker | `FindIt-CSII/…:21` |
| `game-ui/editor/components/toolbar/toolbar.tsx` | `Toolbar` | `:132734` | the editor toolbar | `CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/index.tsx:14` |
| `game-ui/game/data-binding/game-bindings.ts` | `GamePanelType` | `:35934-35939` | the panel-type enum (see the next finding) | `CS2-WriteEverywhere/…:12` |
| `game-ui/game/components/game-panel-renderer.tsx` | `gamePanelComponents` | `:115171-115176` | the panel-type → component map | `CS2-WriteEverywhere/…:13` |
| `game-ui/game/components/photo-mode/photo-mode-panel.tsx` | `PhotoModePanel` | `:85510` | wrapping the photo-mode panel in a portal | `HallOfFame/HallOfFame/UI/src/area-game/index.tsx:12-20` |
| `game-ui/menu/components/menu-ui.tsx` | `MenuUI` | `:134905` | the whole main-menu UI | `HallOfFame/HallOfFame/UI/src/area-menu/index.tsx:21-32` |
| `game-ui/menu/components/menu-ui-backdrops/menu-ui-backdrops.tsx` | `MenuUIBackdrops` | `:134018` | suppressing the menu backdrop | `HallOfFame/…/area-menu/index.tsx:11-19` |
| `game-ui/menu/components/shared/master-screen/master-screen.tsx` | `MasterScreen` | `:70989` | the menu's screen frame | `HallOfFame/…/area-menu/index.tsx:34-48` |
| `game-ui/overlay/logo-screen/logo-screen.tsx` | `LogoScreen` | `:67363` | the loading screen | `HallOfFame/HallOfFame/UI/src/area-overlay/index.tsx:6-20` |
| `game-ui/game/components/toolbar/bottom/time-controls/time-controls.tsx` | `TimeControls` | `:70444` | the clock widget | `Time2Work/NightShift/Time2WorkUI/src/index.tsx:24-32` |
| `game-ui/game/components/toolbar/bottom/time-controls/time-controls-new.tsx` | `TimeControlsNew` | `:128127` | its replacement widget — **both exist and a mod must extend both** | `Time2Work/…:34-42` |
| `game-ui/game/components/toolbar/bottom/time-controls/time-controls.module.scss` | (`classes`) | `:70118` | merging a mod's class map into the vanilla one | `Time2Work/…:50-53` |
| `game-ui/common/focus/focus-key.ts` | `FOCUS_DISABLED`, `FOCUS_AUTO`, `useUniqueFocusKey` | `:32898-32917` | focus keys — but see the trap under the resolver finding | 15 repos |

The `append` anchors the corpus actually uses are five of the eight: `Game` (11 repositories, 15 calls), `Editor` (10, 11 calls), `GameTopLeft` (8), `UniversalModMenu` (3: `ExtraDetailingTools/UI/src/index.tsx:25`, `NodeController/NodeController/UI/src/index.tsx:9`, `Time2Work/NightShift/Time2WorkUI/src/index.tsx:57`) and `Menu` (1, `CS2-MoveIt/UI/src/index.tsx:19`; the only other `Menu` append in the corpus is commented out at `ExtraDetailingTools/UI/src/index.tsx:26`). **No repository appends to `GameTopRight`, `GameBottomRight` or `GameBottomLeft`**.

**One corpus mod extends a path that is not the game's**: `ExtraDetailingTools` extends `"ExtraLib/ExtraPanels/ExtraPanelsRoot/ExtraPanelsRoot"` → `extraPanelsComponents` (`ExtraDetailingTools/UI/src/mods/TransformPanel/RegisterTransformPanel.tsx:11`), a path registered by another mod's shared library. That is `add` used as a public extension surface between mods, and it is why `add` exists: a mod may `add` its own path to the same registry and every later mod can `extend` it. The library's sources are absent from the checkout, so nothing here settles how it registers.

Rots: every path and export name in the table. Re-check with `findModule` against the running game rather than against a stale bundle.

### What makes a tool options panel appear, and the mode switcher's silent half

**`useToolOptionsVisible` is a pure predicate over nine bindings, and a mod tool overriding none of `ToolBaseSystem`'s virtuals satisfies none of them by itself** (themes, asset packs and colour hang on toolbar and prefab state no tool owns — `ToolbarUISystem.cs:446-455` and `:616-624`, `ToolUISystem.cs:621-624`; the tool-owned terms are the `GetUIModes`, `GetAvailableSnapMask` and `allowUnderground` virtuals, `src/Game/Game.Tools/ToolBaseSystem.cs:282/:445/:184`). The implementation (`source.js:82146-82168`):

```
tool.activeTool.id !== "Selection Tool" && (
     toolbar.themes.length > 0
  || toolbar.assetPacks.length > 0
  || tool.activeTool.modes.length > 1
  || tool.availableSnapMask !== 0
  || tool.elevationRange.min < tool.elevationRange.max
  || tool.parallelModeSupported
  || tool.colorSupported
  || (tool.activeTool.id !== "Default Tool" && tool.undergroundModeSupported)
  || !entityEquals(tool.selectedBrush, Entity.Null)
)
```

The binding names resolve at `:46085-46102` and `:46161-46165` (group `"tool"`, `source.js:46084`) and `:46629-46632` (group `"toolbar"`, `:46614`); the two tool-id literals are `"Selection Tool"` (`:46161`) and `"Default Tool"` (`:46154`); `entityEquals` is `cs2/utils`' and `Entity.Null` is the frontend's `{index: 0, version: 0, __Type: "Unity.Entities.Entity"}` (`:34663`).

So a mod tool overriding none of those virtuals brings nothing that mounts the panel — only leftover toolbar or prefab state still can — which is exactly why nine of the nineteen extend this hook to force `true`. Without it there is no container for the rows a `MouseToolOptions` extension adds.

`ToolOptions` renders `gamepadActive ? <GamepadToolOptions/> : <MouseToolOptions/>`, then `tool.isEditor && <EditorOptions/>` (`:82130-82133`, the editor half at `game-ui/game/components/tool-options/editor-options/editor-options.tsx`, `:80996`). So a mod adding rows for both input schemes extends two modules, and `Anarchy` is the only repository that does.

**The mode switcher is generic on the frontend and dead on the C# side, so its buttons appear and do nothing.** The switcher is the third child of `MouseToolOptions` (`:80222-80278`): it returns `null` when `tool.activeTool.modes.length < 2`, otherwise renders one `ValueToolButton` per mode — `src` from `mode.icon`, tooltip from the `ToolOptions.TOOLTIP_TITLE[<mode.id>]` and `TOOLTIP_DESCRIPTION[<mode.id>]` localization keys, selected on `mode.index === activeTool.modeIndex` — and calls `tool.selectToolMode(index)` on select (`:46114-46116`).

The C# handler for that trigger is a five-way type test over the vanilla tool systems, with no fallback: `NetToolSystem`, `ZoneToolSystem`, `BulldozeToolSystem`, `AreaToolSystem`, `ObjectToolSystem` (`src/Game/Game.UI.InGame/ToolUISystem.cs:306-342`, registered at `:169`). A mod tool falls through every branch, and the only statement outside them is `m_ActiveToolBinding.Update()` (`:341`), which re-pushes the unchanged active tool; since the button's `selected` is a pure prop over `tool.activeTool.modeIndex` (`source.js:80265`) and never changes for a mod tool, the click changes nothing on screen. `selectTool` fails differently: `SelectTool(string)` resolves the id through `GetToolSystem` (`:280-283`, `:345-358`), a nine-name string switch whose `_` arm returns `m_DefaultToolSystem`, so a mod tool id handed to it switches the player to the default tool rather than doing nothing. `custom-tools.md:418` records the C# half; the frontend half is what makes the failure hard to diagnose — **the buttons render, highlight and click, and only the effect is missing.**

So a mod tool's mode switcher has to be its own: rows extended into `MouseToolOptions`, driven by the mod's own trigger binding rather than `tool.selectToolMode`.

Rots: `useToolOptionsVisible`, the nine binding names, the two tool-id literals, and `selectToolMode`'s type list.

### Registering a whole new game panel type takes exactly two `extend` calls

The renderer is `game-ui/game/components/game-panel-renderer.tsx` (`source.js:115170`), which exports `gamePanelComponents` — a map from a `GamePanelType` value to a component — and `GamePanelRenderer`, a thin wrapper handing that map and the panel payload to `TypedRenderer` (`:115150-115156`).

`GamePanelType`'s values **are the C# `__Type` strings**: `InfoviewMenu = "Game.UI.InGame.InfoviewMenu"`, `Progression = "Game.UI.InGame.ProgressionPanel"`, `Economy`, `CityInfo`, `Statistics`, `TransportationOverview`, `Chirper`, `LifePath`, `Journal`, `Radio`, `PhotoMode`, `CinematicCamera`, `Notifications`, `Glossary`, `ModsMenu` — fifteen of them (`:35770-35787`, exported from `game-ui/game/data-binding/game-bindings.ts` at `:35934-35939`). The active panel arrives on the `game` binding group and the renderer keys the map on `panel.__Type` (`:115151-115156`), falling back to the unknown-element box.

The two calls, and the corpus's only worked example (`CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/index.tsx:12-13`, the callbacks at `:18-26`):

```
moduleRegistry.extend("game-ui/game/data-binding/game-bindings.ts", 'GamePanelType', input => { input["K45_WE"] = WeMainPanelId; return input; });
moduleRegistry.extend("game-ui/game/components/game-panel-renderer.tsx", 'gamePanelComponents', input => { input[WeMainPanelId] = WEMainPanel; return input; });
```

**`extend`'s callback is not required to be a HOC** — the declaration types it as `ModuleRegistryExtend`, a component wrapper (`create-csii-ui-mod/template/types/modding.d.ts:4`), and the implementation only does `override(path, name, cb(current))` (`source.js:13406`), so any transformer works and this is how a mod extends a plain object or an enum. And **both callbacks mutate the object and return it** rather than returning a copy, which matters for `reset()` (see the composition finding).

The C# half is a `GamePanelUISystem` panel class whose `__Type` matches the value added to the enum; `binding-layer.md:201-243` owns how `__Type` is emitted.

Rots: `GamePanelType`'s fifteen values and both module paths.

### Reusing the game's unexported components, and the trap in the pattern the corpus copies

**The mechanism is that `Q.add` registers every module with a live getter/setter pair per export, whatever `cs2/*` does or does not re-export.** The registration shape is uniform — `Q.add(path, { get X() { return <ident>; }, set X(e) { <ident> = e; } })` — so a component the game never exposed is readable through `get(path, name)` and replaceable through the setter, which is exactly what `override` uses (`source.js:13387`). Nothing is hidden; the 22 empty modules and the 582 dead accessors are the only gaps.

**`.module.scss` modules register differently, and the difference decides how a mod styles.** Their single export is `classes`, and the setter is `Object.assign(target, e)` rather than an assignment (for example `game-ui/game/components/left-menu/left-menu.module.scss` at `:126106-126113`, whose map is the single line above it at `:126105`). So overriding a class map **merges into the existing object** instead of replacing it.

**The corpus's shared helper is `VanillaComponentResolver`, and it is in ten of the 19 UI repositories** — a `class VanillaComponentResolver` in Anarchy, BetterBulldozer, CS2-MoveIt, FindIt, InfoLoom, Recolor, RoadBuilder, Realistic Trips, Tree Controller and Water Features (swept for the class declaration; Realistic Trips carries three `registryIndex` files, so twelve files across the ten). Verdict on the seed survey's list: `survey-mods-techniques.md:334` names six and includes Platter, which does not have one. Its own variant is a different shape recorded below.

Reference copy: `Anarchy/Anarchy/UI/src/mods/VanillaComponentResolver/VanillaComponentResolver.tsx:39-84`, carrying the attribution comment naming Klyte (`:53`) — a `registryIndex` map of `name → [modulePath, exportName]` (`:39-49`), a lazy per-name `cachedData` (`:64-68`) and hand-written prop types the comment admits are guesses (`:5-8`).

Two things in it are worth not reproducing.

- **It reads through `registry.get(path)[name]`** (`:67`) rather than through `moduleRegistry.get(path, name)`. Both invoke the getter, so both are exposed to the 582 `ReferenceError` cases; the difference is only in the failure text — a missing path gives a `TypeError` on `undefined[name]` instead of the registry's own string throw.
- **It caches the first value it reads and never invalidates.** A later mod that `override`s the same export leaves this mod holding the pre-override component, and which mod is "later" is import-completion order.

**Platter's variant is eager where that one is lazy, and trades one staleness for another.** `initialize(moduleRegistry)` is called first thing in the registrar and resolves every path immediately into module-level objects (`CS2-Platter/Platter/UI/src/components/vanilla/Components.tsx:126-139`, the path tables at `:4-49` and `:51-120`, the call at `CS2-Platter/Platter/UI/src/index.tsx:17`). Reading eagerly at registrar time captures the value before any mod later in the array has registered at all; reading lazily at first render captures it after all of them. Neither sees a later `override`, and there is no third option in the registry as shipped — an export read once is a value, not a subscription.

**Three of the resolver's nine entries are unnecessary.** `FOCUS_DISABLED`, `FOCUS_AUTO` and `useUniqueFocusKey` are pulled from `game-ui/common/focus/focus-key.ts` (`:32898-32917`) and are the *same values* the `cs2/input` package exports directly — the bundle binds `FOCUS_AUTO: () => Ug` (`:13297`), `FOCUS_DISABLED: () => Fg` (`:13298`) and `useUniqueFocusKey: () => Bg` (`:13364`) to the same identifiers the registry entry returns. **Settled live**: `window["cs2/input"]` has 77 exports and all three are among them. So the ordinary import works and the registry hop is a habit rather than a need — and it is a widespread one, since fifteen of the 19 UI repositories name that path.

**The discovery workflow the corpus records** is: launch with `--uiDeveloperMode`, open `localhost:9444`, Sources → `index.js`, pretty-print, search for the `.tsx`/`.scss` file name, then read the function it maps to for the props (`Anarchy/Anarchy/UI/src/mods/VanillaComponentResolver/VanillaComponentResolver.tsx:5-8`, and `Anarchy/Anarchy/UI/src/index.tsx:15-16` which suggests `console.log('mr', moduleRegistry)` for the same purpose). The registry itself is the faster route and needs no debugger: `findModule(<fragment>)` returns `[path, ...exports]` for every match, and it is safe on every module.

Rots: `game-ui/common/focus/focus-key.ts` and its three exports; the `.module.scss` `classes` convention.

### The packages the game puts on `window`, and the `export export` tell in the declarations

The bundle defines twelve properties on `window`, in one call (`source.js:47076-47089`):

`React`, `ReactDOM`, `ReactDOMClient`, `cs2/api`, `cs2/bindings`, `cs2/l10n`, `cs2/ui`, `cs2/utils`, `cs2/input`, `cs2/modding`, `cohtml/cohtml`, `chart.js`.

**Settled live**, that is exactly the set present, and the export counts are:

| Global | Exports | Notes |
| --- | --- | --- |
| `cs2/api` | 15 | `bindEvent bindLocalValue bindMap bindTrigger bindTriggerWithArgs bindValue call trigger useMapValue useMapValueOnChange useMapValues useReducedValue useValue useValueOnChange useValueRef` (`:12395-12412`) |
| `cs2/bindings` | 37 | namespaces, not bindings: `budget camera chirper cinematic cityInfo climate devTree economyBudget event feature game infoview infoviewTypes life loan map milestone photo policy prefab prefabEffects prefabProperties prefabRequirements production radio selectedInfo service signatureBuilding statistics taxation time tool toolbar toolbarBottom transport tutorial upgrade` (`:13230-13268`) |
| `cs2/l10n` | 11 | `Localized LocalizedBounds LocalizedDate LocalizedDuration LocalizedEntityName LocalizedFraction LocalizedNumber LocalizedPercentage LocalizedString Unit useLocalization` (`:12414-12426`) |
| `cs2/ui` | 22 | `Button ConfirmationDialog DialogContext DialogRenderer DialogStack Dropdown DropdownItem DropdownToggle FloatingButton FormattedParagraphs FormattedText Icon MarkdownRenderer MarkupRenderer MenuButton Panel PanelFoldout PanelSection PanelSectionRow Portal Scrollable Tooltip` (`:12666-12689`) |
| `cs2/utils` | 11 | `entityEquals entityKey formatLargeNumber isNullOrEmpty parseEntityKey preloadImages shallowEqual useCssLength useFormattedLargeNumber useMemoizedValue useRem` (`:13271-13283`) |
| `cs2/input` | 77 | focus, navigation, input hints, control icons, gamepad, barriers, and the focus-key trio (`:13286-13365`) |
| `cs2/modding` | 2 | `findModule`, `getModule` — and nothing else (`:13367`, aliases at `:47074-47075`) |

**Two of the twelve globals are not in the scaffold's webpack `externals`**: `ReactDOMClient` and `chart.js`. The externals map lists ten (`create-csii-ui-mod/template/webpack.config.js:31-43`), so a mod importing `chart.js` bundles its own copy instead of using the one the game already has — which `InfoLoom` does (`InfoLoom/InfoLoom/UI/src/index.tsx:5`).

**`cs2/assets` does not exist.** The scaffold's `assets.d.ts` is six lines of wildcard module declarations for `*.scss`, `*.css`, `*.svg`, `*.png`, `*.jpg`, `*.gif` (`create-csii-ui-mod/template/types/assets.d.ts:1-6`) — the ambient typing that lets a mod `import icon from "./icon.svg"`. There is no `cs2/assets` module, no such window global (settled live) and no such external.

**Verdict: the `export export` doubled keyword in a `types/*.d.ts` marks a real runtime export, and a single `export` marks a declaration emitted for typing only.** The generator emits `export export const/function/class/enum` for values the module actually exports and a plain `export` for the rest, plus a trailing `export { … }` alias block. Counting the **distinct value names** that way — `api.d.ts:45-46` declares `useMapValue` twice as overloads, and `ui.d.ts:696-704`'s alias block carries two type renames (`ButtonProps$1`, `PanelProps$1`) beside its five values — reproduces the bundle exactly in all seven declaration files that carry values: `cs2/ui` 17 doubled + 5 value aliases = 22; `cs2/l10n` 6 + 5 = 11; `cs2/utils` 11 + 0 = 11; `cs2/api` 15 + 0 = 15; `cs2/modding` 2 + 0 = 2; `cs2/input` 76 + 1 = 77; `cs2/bindings` 0 + 37 = 37, its whole surface arriving through the alias block. Every count matches the live measurement above.

That rule is what settles a class of mistakes the declarations otherwise invite. `ui.d.ts` declares `FocusSymbol`, `FOCUS_DISABLED`, `FOCUS_AUTO`, `UISound`, `ScrollController`, `usePortalContainer`, `PortalContainerProvider`, `InfoSection`, `InfoSectionFoldout` and `InfoRow` with a single `export` (`create-csii-ui-mod/template/types/ui.d.ts:49-56`, `:187-234`, `:524-546`, `:562-594`) and **none of them is in `window["cs2/ui"]`**; the last three reach the mod under their alias names `PanelSection`, `PanelFoldout`, `PanelSectionRow` (`:696-704`) and the focus trio through `cs2/input` instead. `api.d.ts` declares `LocalValueBinding` and `ListenerRef` as `export class` (`create-csii-ui-mod/template/types/api.d.ts:48-68`) and neither is exported, though `bindLocalValue` returns one. `l10n.d.ts` declares `LocElementType`, `TimeFormat`, `TemperatureUnit`, `UnitSystem` and `NameType` singly (`create-csii-ui-mod/template/types/l10n.d.ts:57-62`, `:95-107`, `:141-145`) and none of them exists at runtime under `cs2/l10n`.

The one qualification: the doubled keyword also appears on an `interface` trio — `HTMLImageElement`, `Element`, `MorphAnimation`, the Gameface shape-morphing declarations — repeated verbatim at the end of six of the ten `.d.ts` files in the scaffold — `api`, `bindings`, `input`, `l10n`, `modding`, `ui` (for example `modding.d.ts:28-43`); `utils`, `assets`, `cohtml` and `react` carry none. Interfaces have no runtime value, so those are a generator artifact and the rule applies to value declarations only.

Verdict against the wiki: its `cs2/input` section says "Components and utilities for adding keyboard bindings and gamepad support **will be exposed in the future**" (https://cs2.paradoxwikis.com/UI_Modding). The package ships 77 exports at 1.6.0f1. The page is stamped 1.5.7 f1 and this sentence is stale; the install wins.

Rots: all seven export counts and the twelve global names.

### The registry paths the shipped siblings were promised

Each of these is a promise made in shipped prose that this reference has to keep. All are confirmed present in the 1.6.0f1 registry.

**The data-binding module** (`binding-layer.md:391` handed it over): `game-ui/common/data-binding/binding.ts` (`source.js:25882`) exports **16** names — the 15 of `cs2/api` plus `bindMapPersistent` (`:25901-25905`), which is registry-only. So a mod needing a persistent map binding reaches it here and nowhere else.

**The typed renderer**: `game-ui/common/typed-renderer/typed-renderer.tsx` (`:49824`) exports `TypedRenderer`, `TypedListRenderer`, `renderTyped`, `entityKeyProvider` and `UnknownElement` (`:49824-49855`). `UnknownElement` is the failure box: a `<div style="background-color: red; color: yellow">` reading `` `Unknown element type ${typeName}` `` (`:49792-49796`). The two renderer components draw it whenever a payload's `__Type` is not a key of the map they were handed; `renderTyped` returns `undefined` silently instead (`:49787-49789`). Its sibling `game-ui/widgets/components/widget-renderer.tsx` (`:49857`) exports `WidgetRenderer`, `WidgetListRenderer` and `WidgetComponentMapContext`, and draws the same box for an unknown widget type (`:49797-49821`).

**The generated typed dictionary of vanilla localization keys** (the shipped `plugins/cs2-modding/skills/cs2-modding/references/technique/localization/localization.md:435`): `game-ui/common/localization/loc.generated.ts` (`:28971`) exports one name, `Loc`. It is built by `createLocDictionary` (`game-ui/common/localization/loc-dictionary.tsx`, `:26611`) from a two-level object of key-shape constructors (`:26620-28937`), and the four constructors encode the four key shapes (`:26557-26608`):

- plain id — `Loc.Common.VALUE_YEARS`;
- hashed, rendering `` `${id}[${props.hash}]` `` — `Loc.Assets.NAME`;
- indexed, rendering `` `${id}:${props.index}` `` — `Loc.Assets.CITY_NAME`;
- hashed-and-indexed, rendering `` `${id}[${hash}]:${index}` ``.

Each constructor takes the argument names, and `createLocComponent` turns the pair into a memoised React component carrying `displayName`, `renderString` and `propsAreEqual` (`game-ui/common/localization/loc-component.tsx` → `createLocComponent`, `:26438-26448`, registered at `:26464-26487`). So `Loc.Assets.CITIZEN_NAME_FORMAT` is a component taking `FIRST_NAME` and `LAST_NAME` props, and `renderString(localization, props)` is the escape hatch for a plain string. Every component also accepts `fallback` and `showIdOnFail` (`game-ui/common/localization/localized-string.tsx` → `renderLocalizedString`, `:26509-26524`).

**The time and date formatters `cs2/l10n` does not export** (the shipped `plugins/cs2-modding/skills/cs2-modding/references/technique/units-and-formatting/units-and-formatting.md:115` and `plugins/cs2-modding/skills/cs2-modding/references/mechanics/simulation-time-and-units/simulation-time-and-units.md:113`): `game-ui/common/localization/localized-date.tsx` (`:29896`) exports **eight** names — `LocalizedDate`, `useDateFormat`, `formatDate`, `LocalizedTime`, `useTimeFormat`, `LocalizedDateTime`, `formatDateTime`, `LocalizedTimestamp` (`:29896-29943`). Of those, `cs2/l10n` re-exports **only `LocalizedDate`**. So the gap is precisely: `LocalizedTime`, `LocalizedDateTime`, `LocalizedTimestamp` and the four hook/function forms. Its sibling `game-ui/common/localization/localized-duration.tsx` (`:29996`) exports `LocalizedDuration`, which *is* in `cs2/l10n`.

**The three live unit-preference enums** (the shipped `units-and-formatting.md:81`): `game-ui/menu/data-binding/options-bindings.ts` (`:26110`) exports `TimeFormat`, `TemperatureUnit`, `UnitSystem` and `defaultUnitSettings` (`:26345-26373`), beside `OptionsWidgetType`, `RebindOptions`, `ModifierOptions` and `BindingConflict`. Reaching them through the registry is what saves a mod from writing `0`/`1`/`2` literals — and this module is the fourth-worst for dead accessors (23 of them), so read the *enums* here and the *binding values* through `engine`.

**The unit enum and the US-customary conversions** (the shipped `simulation-time-and-units.md:113`): `game-ui/common/localization/unit.ts` → `Unit`, 38 members from `Integer = "integer"` to `DurationSeconds = "durationSeconds"` (`:28979-29026`); `game-ui/common/localization/units-us-customary.ts` (`:29027`); `game-ui/common/localization/localized-number.tsx` (`:29336`).

**The progression, statistics, notifications and policy panels** (the shipped `plugins/cs2-modding/skills/cs2-modding/references/mechanics/city-state-and-progression/city-state-and-progression.md:132`): `game-ui/game/components/progression/progression-panel/progression-panel.tsx` (`:112660`), `game-ui/game/components/statistics-panel/statistics-panel.tsx` (`:114008`), `game-ui/game/components/notifications-panel/notifications-panel.tsx` (`:110607`, with a menu-side twin at `:110530`), `game-ui/game/components/city-info-panel/city-info-panel.tsx` (`:91923`) and its `city-info-policies/policies-page.tsx` (`:91833`) and `city-info-policies/city-policy.tsx` (`:91732`). Four of them — progression, statistics, the game-side notifications panel and city-info — are `GamePanelType` entries and `gamePanelComponents` values, so replacing one is either an `extend` on its component or a key swap; the menu-side twin, `policies-page.tsx` and `city-policy.tsx` are components rendered inside a panel, not panel entries (the map carries fourteen of the enum's fifteen members as keys — `CinematicCamera` has none; enum at `:35770-35787`, map at `:115069-115148`).

**The options screen** (`settings-and-input.md:427`): `game-ui/menu/components/options-screen/options-screen.tsx` (`:74376`), `option-page/option-page.tsx` (`:73807`), `option-page/option-page-header.tsx` (`:73597`), `options-search.tsx` (`:73984`), `input-rebinding-dialog/input-rebinding-dialog.tsx` (`:66472`), `display-confirmation-dialog/display-confirmation-dialog.tsx` (`:65971`), and the widget renderer above, which is what draws a `ModSetting` page.

**The selected-info section map** carries **80** keys, all `Game.UI.InGame.*Section` strings (the enum at `:45316-45395`, the map at `:125184-125262`). Adding a key is how a mod adds a section; the key must equal the `__Type` its C# section emits.

Rots: every path in this finding.

### Styling: one rem is one 1080p pixel, and every class name is hashed

**The rem basis is the viewport.** `html { font-size: 0.0925926vh }`, switching to `0.0520833vw` under `@media (min-height: 56.25vw)` (`source.css:614-621`). Those are `100/1080` and `100/1920`, so **1rem is one pixel at 1920×1080 and scales by the smaller of `width/1920` and `height/1080`** (on a 16:10 or 4:3 display the width term wins, so "shorter axis" misreads it) — the wiki's "1rem is equal to about 1px at a FullHD resolution" (https://cs2.paradoxwikis.com/UI_Modding), confirmed from the shipped stylesheet. That is why every game and mod stylesheet writes sizes in `rem` and why a mod writing `px` breaks at every other resolution.

The player's interface-scaling toggle overrides it outright: `applyInterfaceScalingEnabled(false)` sets `document.documentElement.style.fontSize = "1px"` (`source.js:47179-47181`), pinning rem to one physical pixel. Its four siblings in `game-ui/common/app/interface.ts` (registered at `:47188-47219`) drive the rest: `applyInterfaceStyle` swaps a `style--<name>` class on `<body>` (`:47137-47143`), `applyInterfaceTransparency` writes `--panelOpacityNormal`, `--uiOpacity` and `--panelOpacityDark` and toggles `no-panel-blur` and a higher-contrast class (`:47144-47165`), `applyTextScale` writes `--fontScale` and `--fontScaleChange` (`:47169-47178`), `applyToolbarScale` writes `--toolbarScale` (`:47182-47187`).

`--fontScale` and `--fontScaleChange` appear 167 times in `index.css`, and only 10 of those are on a `font-size` declaration directly: the stylesheet's own form is the `--fontSizeS`/`--fontSizeM`/`--fontSizeL` tokens (`source.css:211-216`, each a `calc(14rem * var(--fontScale) …)`), which 327 of its 418 `font-size` declarations read, and the rest of the occurrences scale widths and gaps — `--rightPanelWidth: calc(400rem * var(--fontScale))` (`:285`). A mod that hard-codes a font size ignores the player's text-scale setting; multiplying by the variable is the convention.

The stylesheet declares **400 distinct custom properties** over 1,272 declaration lines in **13 `:root` blocks**, re-declared per breakpoint and per locale: it opens with responsive `--gap1`…`--gap8` and `--stroke1`…`--stroke4` (`source.css:1-13`, and the media queries after it), carries per-locale overrides (`:115`, `:121`, `:127`, `:133`, `:139`), and runs through the colour and tooltip palette (`:457-613`). **Settled live**, `document.body.className` is `overwrite-legacy overlay-visible_iR9 style--default no-panel-blur`; the two named interface styles with rules of their own are `style--bright-blue` and `style--dark-grey-orange` (`:391`, `:422`), and `style--default` has none, so it is the unstyled baseline.

**Class names are CSS-module locals with a three-character hash over the module's path and the local name** — css-loader's `[hash]` never reads the rule's declarations — `[local]_[hash:base64:3]`, 3,110 distinct hashed classes in `index.css`. The scaffold configures a mod's own build identically (`create-csii-ui-mod/template/webpack.config.js:61-65`: `modules: { auto: true, exportLocalsConvention: "camelCase", localIdentName: "[local]_[hash:base64:3]" }`), so a mod's classes look exactly like the game's. A real built mod shows the result: `.hof-button_KTp`, `.hof-cityInfo_zmB`, `.hof-cityInfo-name_jSx` (`%CSII_LOCALMODSPATH%/HallOfFame/HallOfFame.css`) — the author prefixes every local name to keep the pre-hash half unique.

Two shapes appear in the registered class maps and both matter to a mod matching on them:

- **`exportLocalsConvention: "camelCase"` adds a camel alias beside a dashed local's kebab name** (an undashed local appears once), both pointing at the same hashed string: `{"left-menu": "left-menu_L1D", leftMenu: "left-menu_L1D"}` (`source.js:126105`).
- **A value can be several classes**, when the SCSS composes: `item: "item_RBL item-focused_FuT"` (`:81009`), `slider: "slider_g0V slider_pUS"` in a theme module composing the base module (`:47242`). Matching a class map value with `===` therefore fails where matching by token does not.

**The two ways to style over vanilla, and the second one is not undoable.** `extend` on a path containing `.scss` takes two forms (`source.js:13392-13402`):

- with a callback in the **third** position, `extend(path, "classes", cb)` where `cb(currentClasses)` returns the whole new map — routed through `override(path, "classes", n(s.classes))` (`:13395`); `extend(path, cb)` with the callback second and nothing third throws `Extending ${e} SCSS without callback requires passing single argument with scss module classes.` (`:13393-13394`), since the guard is `!n && "object" != typeof t`;
- with a plain object, `extend(path, {local: "myHashedClass"})` — which copies the current map, **appends** `` ` ${value}` `` to each named key (creating it empty first if absent), and overrides (`:13397-13401`). So a vanilla `item: "item_bZY"` becomes `"item_bZY item_MyHash"` and both rule sets apply. `Time2Work/NightShift/Time2WorkUI/src/index.tsx:50-53` is the corpus's only use.

The trap: **`reset()` cannot undo an SCSS extend.** `override` saves the current value once, as `q[e][t] || (q[e][t] = s[t])` (`:13387`), and for a `.module.scss` module `s[t]` is the live `classes` *object*; the setter is `Object.assign(target, e)`, so the write mutates the very object the backup points at. `reset()` then replays `override(path, "classes", <that same mutated object>)` (`:13474`), which is an `Object.assign` of an object onto itself. Since `reset()` runs at the head of every mod-list reload (`:47119`), a mod using the SCSS extend leaves its classes in the vanilla map for the rest of the session.

**Verdict (2026-08-23, the review gate): the appended class string grows by one copy per reload, settled from the source alone.** The object form copies the live map (`const n = {...s.classes}`, `:13397`) and appends to the copy (`n[s] += ` + the value, `:13398-13401`), the setter `Object.assign`s onto the same object the backup aliases, and `reset()` replays that object onto itself — so the second registrar run reads a map already carrying the mod's class and appends it again.

Rots: the `font-size` values, the CSS variable names, `localIdentName`, and the class-map shapes.

### Images: how a URL resolves from the page, and the image cache that pins it

**The page is served over `assetdb://`, not `coui://`.** `index.html` links `index.css` and `index.js` relatively (`Cities2_Data/Content/Game/UI/index.html:6/8`), and **settled live** `document.URL` and `document.baseURI` are both `assetdb://gameui/index.html`. So a bare `Media/Glyphs/Advisor.svg` in JSX — the form the game itself uses everywhere, and the form `custom-tools` teaches for a mode icon — resolves against `assetdb://gameui/`.

**Settled live**, by XHR against five URLs:

| URL | Status |
| --- | --- |
| `Media/Glyphs/Advisor.svg` | 200, 1090 bytes |
| `assetdb://gameui/Media/Glyphs/Advisor.svg` | 200, 1090 bytes |
| `coui://gameui/Media/Glyphs/Advisor.svg` | **404** |
| `assetdb://gameui/index.css` | 200, 462,876 bytes |
| `coui://gameui/index.css` | **404** |
| `coui://ui-mods/HallOfFame.css` | 200, 19,448 bytes |

**Verdict: there is no `coui://gameui` host in the shipped game, and `prefabs-and-assets.md:391` and `:395` are wrong about it.** That file records `gameui → EnvPath.kContentPath + "/Game/UI"` registered "at bootstrap" and cites `src/Game/Game.UI/UISystemBootstrapper.cs:53`. `UISystemBootstrapper` is a `MonoBehaviour` development harness whose own `Awake` opens with `UnityEngine.Debug.LogWarning("UISystemBootstrapper is only meant for development purpose")` (`:40`), and it is not the shipped path. The shipped path is `GameManager.InitializeUI`, which registers **only** what `UIHostAsset`s in the database declare: `scheme == "assetdb"` routes to `AddDatabaseHostLocation(hostname, uiUri, priority)` and anything else to `AddHostLocation` (`src/Game/Game.SceneFlow/GameManager.cs:1743-1762`). The install ships **eleven** `gameui.uiHost` files — one beside the base game's UI at `Cities2_Data/Content/Game/UI/gameui.uiHost` with `{"hostname":"gameui","scheme":"assetdb","priority":-1000}` and one per content pack under `Cities2_Data/Content/<Pack>/UI/` at priority `-10`, each contributing that pack's `Media/` tree. So `gameui` is a stacked, priority-ordered **database** host, `assetdb://gameui/` is the only scheme it is registered under. (Whether a mod's own `AddDatabaseHostLocation`/`AddHostLocation` call — both public, `UISystem.cs:226`, `:254` — could add a location under the `gameui` name was not tested; the sentence claims only what ships.)

The operative half of the sibling's claim survives and is what `custom-tools` needs: a bare `Media/...` src reaches the game's own UI directory, which no mod can write into.

**A mod's own files come through `coui://ui-mods/`**, which the scaffold's webpack config makes the emitted `publicPath` (`create-csii-ui-mod/template/webpack.config.js:92`), with imported images copied to `images/[name][ext][query]` under it (`:71-77`). A mod registering its own host with `AddHostLocation` gets `coui://<its host>/…` as `prefabs-and-assets.md:389` records; `RemoveHostLocation(string uri)`'s single-argument overload removes the **whole** host including every other mod's directory (`src/Colossal.UI/Colossal.UI/UISystem.cs:305-315`), so a mod tidying up calls the two-argument form (`:317-329`).

The frontend's own image helpers:

- `game-ui/ui/icon.tsx` → `Icon` (`source.js:43040`, component at `:43017-43038`) renders `<img data-src={src} src={src} onError={handleThumbnailError}>`, or a `TintedIcon` when `tinted` is set — the tint being a `mask-image: url(<src>)` div rather than an `<img>` (`game-ui/common/image/tinted-icon.tsx`, `:36854`, component at `:36842-36852`). The `data-src` attribute duplicating `src` is the durable selector anchor for anything driving the DOM.
- The error handler is `game-ui/common/utils/thumbnails-errors.ts` → `handleThumbnailError` (`:42999`), which rewrites `src` to `Media/Editor/Thumbnails/Fallback_<Kind>.svg` or `Media/Placeholder.svg` (`:42991-42998`). A second, simpler handler exists at `:36829` — and its module `game-ui/common/image/missing-icon-handler.ts` is registered with **no exports**, so it is unreachable.
- `game-ui/common/image/preload.ts` (`:39722`) exports `imagesCache`, `usePrefetchImages` and `preloadImages`. `preloadImages` — the one `cs2/utils` re-exports — only *records* each URL as a key with a `null` value (`:39714-39717`); `usePrefetchImages` later constructs a real `Image()` for every key not yet realised and stores it forever (`:39705-39713`). Nothing ever removes a key, so this page-level map is permanent for the life of the context.

**The experiment: a rewritten file at an unchanged URL is not re-fetched, and a version query is the only thing that gets fresh bytes.**

`prefabs-and-assets.md` recorded this as `Unconfirmed:` and named the experiment; it now opens "Settled (2026-08-23, was Unconfirmed)". Run 2026-08-23 against the running game.

*Method.* A throwaway directory under `%TEMP%` was registered as its own host with watching off, by `unity` `eval` of `Colossal.UI.UIManager.defaultUISystem.AddHostLocation("couiprobe", "<temp>/host", false, 0)`. A 32×16 PNG was written there and loaded from the page by `game_eval` of `new Image()`; Cohtml's `HTMLImageElement` has no `naturalWidth`/`naturalHeight` (both came back `undefined`), so `img.width`/`img.height` were the readout. The file was then rewritten in place at 64×8 with different bytes and requested again at the same URL, then at `?v=2`; rewritten a third time at 128×4 and all three URLs requested together. The whole sequence was repeated with a DOM `<img>` appended to the document rather than a detached `Image`, and again with SVG, where no size is reported and a clipped screenshot of three 32×32 `<img>`s was the readout instead. The host was removed and the directory deleted afterwards; no artifact survives, per `README.md`'s rule for a purpose-built source.

*Results.*

| Request | Bytes on disk at the time | Served |
| --- | --- | --- |
| `coui://couiprobe/probe.png` (first) | 32×16 | 32×16 |
| `coui://couiprobe/probe.png` | 64×8 | **32×16** |
| `coui://couiprobe/probe.png?v=2` | 64×8 | **64×8** |
| `coui://couiprobe/probe.png` | 128×4 | **32×16** |
| `coui://couiprobe/probe.png?v=2` | 128×4 | **64×8** |
| `coui://couiprobe/probe.png?v=3` | 128×4 | **128×4** |

So the cache is keyed on the **full URL string including the query**, and each key is pinned to the bytes it first resolved to. A *fixed* `?v=` value is no better than none. The SVG run reproduced it exactly: a red square written, requested, rewritten blue, then rendered three times side by side as bare / `?v=2` / bare came back red, blue, red.

Four further properties were established the same way:

- **A DOM `<img>` behaves identically to a detached `Image`**, so this is not a JS-object-level cache.
- **A view reload does not evict it.** `location.reload()`, confirmed by the reload counter going 48 → 49 and the fresh context re-registering `cs2/modding`, then the bare URL again: still 32×16. The cache outlives the page.
- **`ClearCachedUnusedImages()` did not evict it** — called twice through `unity` `eval` on `Colossal.UI.UIManager.defaultUISystem`, with the `<img>` removed from the document and eleven frames advanced between the calls, and the bare URL still served 32×16. Unconfirmed: whether the entry genuinely counts as *used*, since no way was found to prove from outside that nothing still referenced the decoded image. What would settle it is a Cohtml-side statement of what "unused" means, which is the sibling `coherent-gameface` plugin's material and not a source `docs/SOURCES.md` lists.
- **It is the image cache and not a resource cache.** A plain text file under the same host, fetched by `XMLHttpRequest`, returned the *new* content at the *same* URL immediately after being rewritten. That matches the decompile — `RequestResourceAsync` walks the host's paths and issues a fresh `UnityWebRequest` per attempt (`src/Colossal.UI/Colossal.UI/DefaultResourceHandler.cs:599-623`, `:692-703`) — and it means a rebuilt `.mjs` or `.css` at an unchanged URL **is** picked up on reload. Only images are pinned.

A mod rewriting a fixed file name is what `HallOfFame/HallOfFame/Mod.cs:152-160` and `HallOfFame/HallOfFame/Services/ScreenshotSnapshot.cs:26-66` do with a `?v={counter}` on two fixed names.

One shape not to walk into, from the decompile: the resource handler short-circuits a raster request whose extension is one of eleven (`.png .jpg .jpeg .gif .bmp .psd .tga .astc .pkm .dds .ktx`, `.svg` absent) into `Resources.LoadAsync<Texture>("UI/SharedImages/<base name>")` and only falls through to the host walk when that misses (`DefaultResourceHandler.cs:25-29`, `:81-87`, `:650-690`). So a mod's raster file whose base name collides with a shipped shared image is shadowed permanently. `prefabs-and-assets.md:419` owns this; it is repeated here because it is the other reason a `coui://` image can serve bytes that are not the ones on disk, and the two are indistinguishable from the page.

Rots: the `assetdb://gameui/` document base, `coui://ui-mods/`, the eleven `gameui.uiHost` files and their priorities, `Icon`'s `data-src`, and the image-extension list.

### `ExecuteScript` DOM hacks, and what replaced them

The technique the community moved past is C# reaching into the page with a string of JavaScript: `GameManager.instance.userInterface.view.View.ExecuteScript(...)`, walking `document.getElementsByTagName("img")` and matching `src.includes("<Filename>.svg")` to toggle a CSS class. Two repositories do it, eight times between them: `BetterBulldozer/BetterBulldozer/Systems/BetterBulldozerUISystem.cs:252-277` and `Tree_Controller/Tree_Controller/Tools/TreeControllerUISystem.cs:462-465/633/1191-1194`.

`binding-layer.md:349-353` owns why it cannot work as a channel — `ExecuteScript` is `void`, PInvokes straight through, and has no reader, writer, path or observer count, so nothing on the C# side learns whether it even parsed. The frontend half is why it cannot work as a *technique*, and it is three separate failures:

- **It matches on a rendered attribute.** The game's `Icon` emits `src` from a prop; a re-render, a theme change or a renamed shipped asset silently breaks the match, and nothing reports it.
- **It writes to the DOM under React.** React owns `className` on those nodes; the next render of that subtree discards the write.
- **It targets a hashed class name.** The hash covers the module's path and the local name, so a renamed local or a moved `.module.scss` silently renames the target.

The replacement is the registry, and the four vanilla paths those two mods would need are all in this file's extension-point table: `MouseToolOptions` for the rows, `ToolButton` for the button, `useToolOptionsVisible` for the container, and the mod's own binding for the selected state. Both mods already use it for everything else — `BetterBulldozer/BetterBulldozer/UI/src/index.tsx:15/17` and `Tree_Controller/Tree_Controller/UI/src/index.tsx:15/17/19/22` — so in both cases the `ExecuteScript` calls are residue from before `ModRegistrar`, sitting beside the registry code that superseded them. `survey-mods-techniques.md:345-347` flagged this in 2026-07-31 and the characterisation holds at 22 repositories.

### Composition: what chains, what clobbers, and what survives a reset

This is the compatibility surface `mod-compatibility.md:382` names, and the mechanism is four lines of the registry.

- **`extend` chains.** It is `override(path, name, cb(current))` (`source.js:13406`), so the second mod's callback receives the first mod's result. Every extension of `MouseToolOptions` in a playset composes into one wrapper chain, outermost being the mod that registered last.
- **`append` chains too, and across anchors**, because every anchor append extends the same `ModdingHook` export and each wrapper filters on the rendered hook's `name` prop, passing through unchanged when it does not match (`:13421`). The wrapper sets `displayName = "Extended ModdingHook:<Anchor>+"` (`:13452`), which is how a chain reads back in the component tree.
- **`override` clobbers.** The second mod's value simply replaces the first's, and the backup in `q` still holds the *original* because `q[e][t] || (q[e][t] = s[t])` only records once (`:13387`).
- **`add` throws** on a path already registered (`:13380-13381`), which is what makes a mod's own added path a stable public surface for other mods.
- **Order is import-completion order**, not playset order — the loader pushes each module's default as its dynamic `import()` resolves (`:134933`). Nothing a mod can do makes it deterministic.
- **`reset()` is global and runs first**, restoring every recorded override and clearing the append set before any registrar runs (`:13471-13475`, called at `:47119`). Its blind spots: the registry `Map` itself, which keeps every added path (`:13471-13475` touches only the append set and the override backup); an SCSS class map, for the aliasing reason under the styling finding; and anything a mod mutated *in place* — the `gamePanelComponents` and `GamePanelType` pattern above mutates and returns the same object, so `reset()` re-installs the object the mod already added its key to.
- **`append`'s `index` parameter is honoured in the module-path form and silently ignored in the anchor form.** The implementation reads the *fourth* positional argument for the insertion index (`append(e, t, n, s = void 0)`, the index branch gated on `"number" == typeof s`, `:13409`/`:13423`). In `append(path, exportName, Component, index)` that is `index`; in `append(anchor, Component, index)` the fourth argument is undefined and the index falls through to the append-at-end branch (`:13443-13449`). The declaration promises the parameter in both overloads (`create-csii-ui-mod/template/types/modding.d.ts:12-13`, the second carrying a `_?: never` fourth slot that makes passing anything there a type error). Verdict: **the bundle wins; anchor appends cannot control their position.** In the module-path form the index inserts the component before the child at that position, a negative value counting from the end, and a value past the last child appends (`:13423-13441`).

**Ruled (2026-08-23, the ticket 34 orchestrating session under the maintainer's delegation; conflicts.md).** The same ruling as the anchors finding: the reference states that `index` positions a module-path append and that an anchor append always lands last, in its own voice, with `VOLATILE:` naming `append`'s body in the bundle and the scaffold's two overloads.

The registrar-throw failure under the loader finding belongs to this list too and is the worst member of it: one mod's exception aborts every mod after it in a nondeterministic order.

### Catalog gaps

**Write Everywhere** (`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:362`). Add to **Demonstrates**:
> The corpus's only registration of an entirely new game panel type: two `extend` calls that mutate the panel-type enum and the type-to-component map the game's own panel renderer keys on, paired with a C# panel class whose emitted type name is the key.
Source lines: `CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/index.tsx:12-13` with the callbacks at `:18-26`, and the C# half at `BelzontWE/WriteEverywhereCS2Mod.cs:101` and `Systems/WEMainUISystem.cs:19`.

**Hall of Fame** (`mod-catalog.md:381`). Its entry already says its product is its frontend; add beside it:
> Wrapping whole vanilla screens in portals rather than adding rows to them — the loading screen, the menu shell, the menu backdrop and the photo-mode panel — with the registrar split into one function per UI area and composed into a single default export.
Source lines: `HallOfFame/HallOfFame/UI/src/area-overlay/index.tsx:6-20`, `area-menu/index.tsx:11-48`, `area-game/index.tsx:9-21`.

**Realistic Trips** (`mod-catalog.md:207`). Add to **Demonstrates**:
> The corpus's only use of the module registry's SCSS form — merging a mod's own class map into a vanilla `.module.scss` so both rule sets apply — and the pattern of extending a vanilla widget and its newer replacement in the same registrar, since both ship.
Source lines: `Time2Work/NightShift/Time2WorkUI/src/index.tsx:24-32`, `:34-42`, `:50-53`.

**Anarchy** (`mod-catalog.md:404`). Add beside the raycast entry:
> The reference copy of the community's vanilla-component resolver, the singleton ten repositories share for pulling unexported game components out of the module registry, carrying the discovery workflow in its own comments.
Source lines: `Anarchy/Anarchy/UI/src/mods/VanillaComponentResolver/VanillaComponentResolver.tsx:5-8`, `:39-84`, and its use at `Anarchy/Anarchy/UI/src/index.tsx:19`.

**Area Bucket** (`mod-catalog.md:75`). Add beside the geometry entry:
> Naming every registry path and export as a typed constant in one file and spreading the pair into the registry call, which is the shape that survives a game version renaming a path.
Source lines: `AreaBucket/UI/area-bucket/src/constants.ts:5-16`, used at `AreaBucket/UI/area-bucket/src/index.tsx:9-10`.

### Source-list gaps

**Entry 3 (the game's UI bundle) names only `index.js`, and `index.css` is a source in its own right.** The entry listed `index.css` among the files and then described the reformatting workflow for `index.js` alone. The stylesheet answers questions nothing else can — the rem basis, the 400 custom properties across its thirteen `:root` blocks, the interface-style classes, and the fact that every class name carries a path-and-local-name hash — and it needs the same treatment for the same reason (it ships the same single-line way). A reformatted copy already exists beside the JS one at `DecompiledCitiesSkylines2/src-ui/source.css`, 24,902 lines. **Amended in place on 2026-08-23** under that file's own rule for a pass that finds an entry narrower than the truth: the entry now names the stylesheet's authority and states that the reformatted-copy rule covers both files.

**Entry 6 (the official UI mod scaffold) was wrong in two particulars and missing the one rule that makes `types/*.d.ts` readable.** It described `cs2/modding` as "the module registry's five operations and its append-hook targets" — the declaration has eight operations and a `registry` map — and listed `cs2/assets` among the importable modules, which does not exist. And it treats the declaration files as the record of what a mod may import, which is right, but a reader taking every `export` in them at face value will reach for a dozen names that do not exist at runtime; the doubled `export export` keyword is the tell, and it reproduces the shipped export set exactly in all seven value-carrying files. **Amended in place on 2026-08-23**, all three, under that file's own rule for a pass that finds an entry wrong: the operation count is corrected, `assets.d.ts` is described as the wildcard declarations it is, and the entry now states the `export export` rule and its one interface-shaped exception.

Nothing else in `docs/SOURCES.md` needed correcting for this topic. Entries 1, 9, 10 and 11 were used exactly as described, and entry 9's warning about `getModule` versus `findModule` is confirmed and quantified above.

---

## Bridge

**Sibling techniques in the UI skill.**

- **`binding-layer`** is the other end of every wire and this file is the far side of its handover. What it asked for is delivered above: `game-ui/common/data-binding/binding.ts` and its sixteen exports; the `cs2/api` fifteen; and `game-ui/common/typed-renderer/typed-renderer.tsx` with `UnknownElement`. What travels back is the reciprocal: **the `Loc` dictionary is keyed on localization ids, not `__Type`, and never passes through the typed renderer** — the `__Type`-keyed maps are `gamePanelComponents` and `selectedInfoSectionComponents`; and the 582 dead accessors are the measured form of that file's warning that the registry is not the route to a binding's value (`binding-layer.md:355-360`). Its `ExecuteScript` finding (`:349-353`) is completed here by the three frontend reasons the technique cannot work.
- **`ui-build-and-devloop`** owns everything upstream of the `.mjs`, and this file states the contract that build has to satisfy: two named exports (`default`, `hasCSS`), a file named after the mod id because all mods share one `coui://ui-mods` host, `publicPath: "coui://ui-mods/"`, and `localIdentName: "[local]_[hash:base64:3]"` (`create-csii-ui-mod/template/webpack.config.js:28-30`, `:61-65`, `:92`, `:98-101`; the `hasCSS` injector at `tools/css-presence.js:12-26`). The dev-loop fact it needs from the experiment: **a rebuilt `.mjs` or `.css` at an unchanged URL is picked up on reload, and an image at an unchanged URL is not.**

**Trunk techniques.**

- **`custom-tools`** gets the two halves it was promised. The tool-options panel appears only when `useToolOptionsVisible`'s nine-term disjunction is satisfied, which a mod tool overriding none of the three `ToolBaseSystem` virtuals does not itself do, so the panel needs a virtual override or that hook extended (`source.js:82146-82168`). And the mode switcher is the sharper finding: it renders generically for any tool with two or more modes and calls `tool.selectToolMode`, whose C# handler tests for five vanilla system types and silently does nothing for anything else (`src/Game/Game.UI.InGame/ToolUISystem.cs:306-342`), so a mod tool gets buttons that highlight and do not act. `custom-tools.md:418`'s two `extend` paths are both confirmed present at 1.6.0f1.
- **`localization`** gets the `Loc` dictionary: `game-ui/common/localization/loc.generated.ts` → `Loc`, the four key-shape constructors at `source.js:26557-26608`, and the component factory at `:26438-26448` with `renderString` as the plain-string escape hatch. And the correction it needs: `cs2/l10n` **does** export `LocalizedDate` and `LocalizedDuration`; the withheld formatters below are `units-and-formatting`'s errand rather than this topic's (its shipped file claims them as one of its two), and they are `LocalizedTime`, `LocalizedDateTime`, `LocalizedTimestamp`, `useDateFormat`, `useTimeFormat`, `formatDate` and `formatDateTime`, all in `game-ui/common/localization/localized-date.tsx`.
- **`units-and-formatting`** and **`simulation-time-and-units`** get `game-ui/menu/data-binding/options-bindings.ts` for the three live preference enums plus `defaultUnitSettings` (`:26345-26373`) — with the caveat that the same module carries 23 dead accessors, so the enums read and the binding values do not — and `game-ui/common/localization/unit.ts`'s 38-member `Unit` (`:28979-29026`).
- **`settings-and-input`** gets the options-screen module set and the widget renderer that draws a `ModSetting` page, plus the finding that `types/input.d.ts`'s 812 lines describe `cs2/input`'s 77 runtime exports — the focus, navigation and control-icon surface — and that its `FOCUS_DISABLED`/`FOCUS_AUTO`/`useUniqueFocusKey` are the same values fifteen of the nineteen UI repositories pull out of the registry by hand instead.
- **`mod-compatibility`** gets the composition finding, and the three facts it did not have: `pR`'s unguarded loop, so one throwing registrar silences every later mod; import-completion order, so which mods those are varies per run; and `reset()`'s blind spots (the registry `Map` keeping every added path, an SCSS class map, and any export a mod mutated in place). Its own instruction — that this reference states the registry API and that one states only the composition property — still holds, and the count behind it is now 1,386 modules and 2,994 exports.
- **`prefabs-and-assets`** gets a correction and a closure. The correction: there is no `coui://gameui` host in the shipped game; `gameui` is an `assetdb` database host contributed by eleven `.uiHost` files, the page's own base is `assetdb://gameui/index.html`, and `coui://gameui/…` 404s live. The closure: its once-`Unconfirmed:` image-cache question (now "Settled (2026-08-23, was Unconfirmed)" in `prefabs-and-assets.md`) is answered — a rewritten file at an unchanged URL serves the cached bytes, the cache key is the full URL including the query, a changed `?v=` is the only lever, and neither a view reload nor `ClearCachedUnusedImages()` cleared it.
- **`navigating-the-decompile`** is confirmed at its own boundary: 1,386 `game-ui/…` registry paths exist in the bundle and the string literal is the only link back to C# (`navigating-the-decompile.md:602`). Two of this file's findings are on the far side of exactly that line — `useToolOptionsVisible` and `GamePanelType` — and neither is derivable from a C# search.

**Mechanics topics this technique exercises.**

- **`city-state-and-progression`** — the five panels it names are the React components listed above, and four of them are `GamePanelType` keys (the policy pages are tabs inside city-info), so a mod replacing one either extends the component or swaps the `gamePanelComponents` entry rather than forking the C# system. `city-state-and-progression.md:515` records the corpus's one worked case as a wholesale C# fork with a replacement binding group; the registry route above is the cheaper one that finding exists to make available.
- **`citizens-and-households`** — the selected-info section map's 80 keys are how a mod adds a row to the panels that topic drives, and six repositories reach it through `selectedInfoSectionComponents`, between them adding eleven sections.
- **`simulation-time-and-units`** — the clock widget ships twice (`time-controls.tsx` and `time-controls-new.tsx`), and a mod touching the clock has to extend both. Its fourth promised path is `game-ui/game/data-binding/time-bindings.ts` (`source.js:45986`), whose first export `timeSettings` is a dead accessor like the rest of its group, so the tick arithmetic reads from the module and the values read through `engine`.

**Soft pointer, not a bridge slug.** Every claim marked *Settled live* above came from the sibling `coherent-gameface` plugin over ordinary `game_eval`, and the image experiment additionally used `unity-devtools` for four `UISystem` calls. Enumerating the registry from the running game with `findModule("")` is both cheaper and more current than re-deriving it from a reformatted bundle, and it is the only way to learn which exports are dead.

---

## Dead ends

- **`cs2/assets` is not a module and never was.** Recorded here because the name is plausible enough that the next pass will look for it.
- **`naturalWidth` and `naturalHeight` do not exist on Cohtml's `HTMLImageElement`.** Both read `undefined` on a loaded image; `img.width`/`img.height` carry the intrinsic size for a raster image.
- **An SVG has no intrinsic size in Cohtml.** A loaded `<img>` pointing at an SVG with a `viewBox` and no `width`/`height` reports `width`/`height` of 0 and a zero bounding rect, so an SVG cannot be measured the way a PNG can; the wiki says the same in one line ("SVG images in coherent require width and height provided", https://cs2.paradoxwikis.com/UI_Modding). The colour-in-a-screenshot readout is the substitute.
- **`window` carries no handle on the module registry object.** `cs2/modding` exposes `findModule` and `getModule` and nothing else (settled live, and `source.js:13367`), and both are plain references to `Q.find` and `Q.get` (`:47074-47075`), leaking no reference to `Q` as a value. So `override`, `extend`, `append` and `reset` are exercised from inside a registrar, or from a debugger session by breaking inside `find`/`get` and evaluating in frame, where `Q` is in scope. Every claim about those four in this file is read from the bundle, and the SCSS reset behaviour once led this list and is now settled from the source (the 2026-08-23 verdict above).
- **The `game-ui/api/index.ts` barrel is registered empty.** Reaching `bindValue` through the registry looks like it should work through that path and does not; the module with the functions is `game-ui/common/data-binding/binding.ts`. Twenty-one other modules are registered empty for the same reason (barrels and re-export files), listed under the registry-composition finding.
- **`moduleRegistry.get` on a `*-bindings.ts` export is the wrong instrument and the error text says the wrong thing.**
- **The decompile says nothing about any of this.** Grepping `src/` for a `game-ui/` path, for `ModRegistrar`, for `moduleRegistry`, for `GameTopLeft`, `GameBottomLeft` or `UniversalModMenu` returns zero files on every one; the only C# that touches the frontend's module system is `UIModuleAsset`'s banner-comment parser (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/UIModuleAsset.cs:45-110`), `ModManager.InitializeUIModules`/`AddUIModule`/`RemoveUIModule` (`src/Game/Game.Modding/ModManager.cs:461-493`) and the `app.activeUIModsLocation` binding (`src/Game/Game.UI/AppBindings.cs:206/242-268`). That is the whole C# surface of this topic and it is four files.
- **The wiki has no module-path list and no code beyond two illustrative paths.** `UI Modding` (https://cs2.paradoxwikis.com/UI_Modding, fetched live 2026-08-23 through `index.php?action=raw`) has eighteen sections. Its Find/Override/Extend/Append vocabulary is correct as far as it goes and omits `get`, `add`, `hasAppend`, `registry` and `reset`; its anchor list is the scaffold's seven; the only module paths it names are `game-ui/menu/components/main-menu-screen/main-menu-screen.tsx` and its `.module.scss` sibling, both of which exist. Two of its statements are worth taking: "1rem is equal to about 1px at a FullHD resolution" and "SVG images in coherent require width and height provided", both confirmed above. One is stale and got a verdict: its `cs2/input` section says the package "will be exposed in the future".
- **No corpus mod passes an index to `append` in either overload.** Swept all 19 repositories with a registrar; every call is two or three arguments with no numeric index. So the finding that the anchor overload ignores it has no corroboration and no contradiction from the corpus, and rests on the implementation alone.
- **No corpus mod calls `reset()`, `hasAppend` or `add`.** Zero call sites across all 22 repositories; the only `hasAppend` occurrences are its declaration in two vendored `modding.d.ts` copies (`Anarchy/Anarchy/UI/types/modding.d.ts:14`, `ExtraDetailingTools/UI/types/modding.d.ts:14`), and the one `add`-shaped surface — `ExtraLib`'s `ExtraPanelsRoot`, which `ExtraDetailingTools` extends — is registered by a library whose sources are not in the checkout. Nothing there settles how those three behave in practice.
