# The frontend as source

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Nearly everything here rests on the frontend bundle, so the module paths, export names and class shapes below are checkable only against the install's `index.js` and `index.css` — the script reads only as the reformatted copy `cs2-modding-setup` records, and the stylesheet only once reformatted the same way beside it, a copy that record does not yet track — and the tree itself answers for the few C# handlers and types named and for nothing else in this file.
`cs2-modding-setup` provisions it.

How a mod's JavaScript gets onto the game's page and what it can do once there: the loader, the module registry and its operations, the append anchors, the proven extension points, the game's own React reached through the registry, the packages on `window`, styling, and image serving from the page's side.
The C# end of every binding is [`binding-layer`](../binding-layer/binding-layer.md)'s; what builds the `.mjs` this file describes — webpack, the scaffold, the dev loop — is [`ui-build-and-devloop`](../ui-build-and-devloop/ui-build-and-devloop.md)'s.
The C# registration of a resource host, its priority resolution and its watch parameter stay in [`prefabs-and-assets`](../../../cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md).

The quickest census is the running game: `window["cs2/modding"].findModule(<fragment>)` lists every matching module with its export names, and the sibling `coherent-gameface` plugin can run that from outside the page; the empty query answers with every module — some 1,400, more than a debugger channel comfortably carries — so scope it.

[`promised-registry-paths.md`](promised-registry-paths.md) holds the module paths other references send a reader here for — the data-binding module, the typed renderer, the `Loc` dictionary, the formatters `cs2/l10n` withholds, the unit enums, the time bindings, the panel components, the options screen and the selected-info section map.

## How a module reaches the page

The C# side registers the directory of each `.mjs` the asset database found under one shared `ui-mods` resource host and publishes the list of `coui://ui-mods/<file>.mjs` URLs on the `app.activeUIModsLocation` value binding (VOLATILE: the host name, the binding path and the URL form — `ModManager.AddUIModule` in `src/Game/Game.Modding/ModManager.cs` and `src/Game/Game.UI/AppBindings.cs`).
A `useEffect` in the root component reads that binding and does five things (VOLATILE: the loader's shape, the `hasCSS` export name and the `.mjs`-to-`.css` derivation — the root component's effect over that binding and `game-ui/modding/utils/load-css.tsx` in `Cities2_Data/Content/Game/UI/index.js`):

1. Dynamically `import()`s each URL as an ES module.
2. Pushes each module's `default` export into an array in import-completion order, not list order.
3. Where the module also exports a truthy `hasCSS`, derives the stylesheet URL by replacing `.mjs` with `.css` and fetches it; on HTTP 200 a `<link rel="stylesheet">` whose `id` is the file's base name is appended to `document.head`.
4. Once every import has settled, calls `reset()` on the registry, clears its own set of fetched URLs, and calls each registrar in the array with the registry object.
5. Gates the whole UI on that completion — a gate already open at game start, since the effect's first run is over an empty binding and the mod list is published well after the page mounts; it bites on a reload of the UI page, where the list is populated at mount.

**A built module's contract with the page is two named ESM exports: `default`, the registrar, and `hasCSS`, a boolean.**
The scaffold's own comment says only the default export is read; the bundle reads both, and the scaffold's build injects the `hasCSS` export it says is ignored.
A mod that ships a stylesheet and no `hasCSS` export gets an unstyled UI with nothing logged.
Source: the root component's module loader and `loadCss` in `game-ui/modding/utils/load-css.tsx` (`Cities2_Data/Content/Game/UI/index.js`); the scaffold's `types/validateTypes.ts` and `tools/css-presence.js`.

**Four failures ride in those steps, and none of them names the mod at fault.**
An import that rejects is caught, discarded and counted as settled, so nothing reaches the console.
A module with no `default` export pushes `undefined` into the registrar array unconditionally.
The loop calling the registrars has no `try`/`catch` and runs just before the completion callback sets the ready flag, so a registrar that throws — or an `undefined` entry — aborts the loop and skips the flag.
Every mod later in the array never registers at all, and because the array is in import-completion order, which mods survive varies between runs of the same playset; on a reload of the UI page the skipped flag leaves the whole UI unrendered.
A registrar therefore wraps its own body in `try`/`catch` for its neighbours' sake and the page's rather than its own.
The fourth spares no one: a module exporting `hasCSS` as true whose `.css` does not answer with HTTP 200 never settles, because the stylesheet loader counts one more expected completion and calls back only on a 200 and the link's `onload` — so the registrars never run and every mod goes unregistered, not just the ones after it, and on a reload of the UI page the whole UI stays blank.
Source: the root component's module loader and `loadCss` in `Cities2_Data/Content/Game/UI/index.js`.

The effect's dependency is the binding value, so enabling or disabling a UI mod mid-session re-runs every registrar from a baseline whose overrides are restored — not a clean slate: the registry map, an SCSS class map and any object mutated in place carry over.

**All mods share one `ui-mods` host, so a file name collides across mods.**
That is why a built module is named after the mod id rather than `index`.
Source: `src/Colossal.UI/Colossal.UI/DefaultResourceHandler.cs`; the scaffold's `webpack.config.js` for the entry name.

## The module registry

The registrar a mod exports receives one object, built once over three module-scope containers: the registry `Map`, the override backup, and the set of anchors appended to.
The scaffold's `types/modding.d.ts` declares the same shape, member for member (VOLATILE: the member set — the registry object literal in `Cities2_Data/Content/Game/UI/index.js` and the scaffold's `types/modding.d.ts`):

| Member | What it does |
| --- | --- |
| `get(modulePath, exportName)` | reads one export, invoking its accessor |
| `add(modulePath, module)` | registers a new path; throws on one already registered |
| `override(modulePath, exportName, newValue)` | replaces one export, recording the original once |
| `extend(modulePath, exportNameOrSCSSValue, extendCb?)` | `override` with the callback's result over the current value |
| `append(modulePath, exportName, component, index?)` | inserts a component into the `children` the export receives |
| `append(anchor, component, index?)` | mounts a component at a named anchor |
| `hasAppend(anchor)` | whether anything has appended to that anchor |
| `registry` | the raw `Map<string, Record<string, any>>` |
| `find(query)` | every `[path, ...exports]` matching the query |
| `reset()` | restores every recorded override and clears the append set |

**The registry throws strings, not `Error`s.**
`get` throws `Module <path>@<export> was not found.`, `add` throws `Module <path> was already registered. If you want to override the exports of this module use the override API`, and `override` and `extend` throw `Module <path> was not found.`; a `catch` block reading `err.message` gets `undefined` on all four (VOLATILE: the texts — the registry object literal in `Cities2_Data/Content/Game/UI/index.js`).
Source: the registry object literal in `Cities2_Data/Content/Game/UI/index.js`.

`find` filters export names first and returns `[path, ...matchingExports]` when any matched; only when none did does it test the path and return `[path, ...allExports]`, so a query matching both a path and one of its exports comes back narrowed to the export.
`find("")` matches everything and is the way to enumerate the registry — scoped, when the result travels a debugger channel.
`window["cs2/modding"]` exposes `findModule` and `getModule`, the same two functions under other names and nothing else, so every other member — `add`, `override`, `extend`, `append`, `reset`, `hasAppend`, `registry` — is reachable as a value only from inside a registrar; a breakpoint inside `find` or `get` does reach the registry object in frame, since both are declared in its scope.

**`find` never invokes an accessor and `get` always does, and hundreds of exports throw when read.**
Every module registers with a live getter per export, and where the bundler removed the binding behind one, the getter returns a name that no longer exists and throws `ReferenceError: <name> is not defined` — which reads as "this export does not exist" while `find` lists it.
Read from the running game, those dead accessors cluster in the `*-bindings.ts` modules — `game/data-binding/infoview-bindings.ts`, `tool-bindings.ts`, `menu/data-binding/menu-bindings.ts`, `options-bindings.ts` and their kin — and the dead names are the binding objects themselves, so reading a vanilla binding's value through `getModule` is the single most likely way to meet this (VOLATILE: which modules carry dead accessors — the self-returning getters in the bundle's `add` registrations).
A vanilla binding's value is read through `engine.on` and `engine.trigger` on its path, which [`binding-layer`](../binding-layer/binding-layer.md) owns; the registry is for the enums and components beside it.
Source: `find` and `get` in the registry object literal (`Cities2_Data/Content/Game/UI/index.js`).

**Some modules are registered with no exports at all — every `index.ts` barrel, the page entry `game-ui/index.tsx`, and non-barrels such as `game-ui/common/scrolling/scroll-controller.ts`.**
`game-ui/api/index.ts`, `game-ui/ui/index.ts` and `game-ui/common/localization/index.ts` are the empty registrations a mod is likeliest to try, so `cs2/api`'s functions are not reachable through the registry at that path; the module carrying them is `game-ui/common/data-binding/binding.ts` (VOLATILE: the empty registrations — the bundle's `add` calls with an empty literal).
Source: the `add` registrations in `Cities2_Data/Content/Game/UI/index.js`.

## The append anchors

The scaffold's `AppendHookTargets` union names seven anchors: `Menu`, `Editor`, `Game`, `GameTopLeft`, `GameTopRight`, `GameBottomRight` and `UniversalModMenu` (VOLATILE: the union — the scaffold's `types/modding.d.ts`).
The bundle renders eight `ModdingHook` instances, and the eighth is `GameBottomLeft` (VOLATILE: the anchor names, the hosting modules and exports in this table, and the `left-menu_L1D` class name — the bundle's `ModdingHook` render sites in `Cities2_Data/Content/Game/UI/index.js`):

| Anchor | Module and export it sits in | What appending there gets you |
| --- | --- | --- |
| `Menu` | `game-ui/menu/components/main-menu-screen/main-menu-screen.tsx` | the last child of the main menu screen, after the button column |
| `Editor` | `game-ui/editor/components/editor-main-screen.tsx` | late in the editor screen's children, under the tutorial container and the tooltip layer |
| `Game` | `game-ui/game/components/game-main-screen.tsx` (`GameMainScreen`) | the last child of the in-game screen, above every panel layer — a free overlay |
| `GameTopLeft` | same, inside the `infoMenuLayout` div | beside the infoview-menu toggle; conditional, since the branch rendering `topLayout` instead carries no hook |
| `GameTopRight` | same, first child of the `pauseMenuLayout` div | above the advisor toggle, in both layouts — except under the gamepad scheme with the top layout hidden, where neither renders |
| `GameBottomRight` | `game-ui/game/components/right-menu/right-menu.tsx` (`RightMenu`) | the first child of the right menu, above the notifications button |
| `GameBottomLeft` | `game-ui/game/components/left-menu/left-menu.tsx` (`LeftMenu`) | the whole component — a `left-menu_L1D` div whose only child is the hook |
| `UniversalModMenu` | `game-ui/game/components/universal-mod-panel/universal-mod-panel.tsx` (`UniversalModPanel`) | a button inside a scrollable mods panel |

**`GameBottomLeft` is rendered and untyped.**
`append` validates its target against no list — the anchor form stores the string and compares it to each rendered hook's `name` prop at render time — so `append('GameBottomLeft', C)` works at runtime and fails TypeScript against the shipped declaration.
The form is chosen on the second argument, not the first — the anchor branch runs only when `typeof` that argument is `"function"` — so a `React.memo` or `forwardRef` component, an object at runtime though the declaration admits it, falls to the module-path branch and throws the was-not-found string naming the anchor as a module; in the true module-path form the same object leaves the component slot unassigned and React crashes on an invalid element at render instead. Pass the plain function component and memoize inside it.
The `left-menu_L1D` host is present in a loaded city with no mod appending there, read from the running game.
Two routes pass the compiler: widen the union in the mod's own copy of `types/modding.d.ts`, which is the mod's file to edit, or cast the string through `unknown`.
The union has grown once already, so this anchor is the one most likely to be typed next.
Source: `append` in the registry object literal (`Cities2_Data/Content/Game/UI/index.js`); the scaffold's `types/modding.d.ts`.

**`index` positions a module-path append, and an anchor append always lands last.**
The implementation reads the fourth positional argument as the insertion index, which in `append(path, exportName, Component, index)` is the index and in `append(anchor, Component, index)` is undefined, so the anchor form falls through to append-at-end; the declaration promises the parameter on both overloads (VOLATILE: the index argument's position and which overload reads it — `append`'s body in `Cities2_Data/Content/Game/UI/index.js` and the scaffold's `types/modding.d.ts`).
In the module-path form the index inserts before the child at that position, a negative value counts from the end, and a value past the last child appends.
Either way the injection rides the `children` prop the export receives, so it shows only where the export renders its `children` — `ModdingHook` does, while a fixed-list component such as `MouseToolOptions` or `RightMenu` does not, and the append silently renders nothing.
Source: `append` in the registry object literal (`Cities2_Data/Content/Game/UI/index.js`); the scaffold's `types/modding.d.ts`.

`ModdingHook` itself wraps its children in a transition-group coordinator, so everything appended at an anchor is mounted inside one that tracks child add and remove for exit animations.

**`UniversalModMenu` is the one anchor whose entry point a mod creates by using it.**
The right menu gates its mods-menu button on `hasAppend("UniversalModMenu")`, so with nothing appended there the button does not exist and the panel is unreachable.
Source: `RightMenu` in `game-ui/game/components/right-menu/right-menu.tsx` (`Cities2_Data/Content/Game/UI/index.js`).

## The proven extension points

Each path below is registered in the bundle under the export named (VOLATILE: every path and export name in this table — the bundle's `add` registrations in `Cities2_Data/Content/Game/UI/index.js`).

| Module path | Export | What extending it does |
| --- | --- | --- |
| `game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx` | `MouseToolOptions` | adds rows to the mouse tool-options panel |
| same | `Section` | the row-with-title wrapper the game's own rows use |
| same | `DistanceSection`, `vegetationAgeOptions` | two further reusable row pieces |
| `game-ui/game/components/tool-options/gamepad-tool-options/gamepad-tool-options.tsx` | `GamepadToolOptions` | the gamepad twin of the above |
| `game-ui/game/components/tool-options/tool-options-panel.tsx` | `useToolOptionsVisible` | forces the panel to appear for a tool the game does not know |
| same | `ToolOptionsPanel` | replaces the panel frame outright |
| same | `ToolOptions` | the panel's inner content |
| `game-ui/game/components/tool-options/tool-button/tool-button.tsx` | `ToolButton`, `ValueToolButton`, `StepToolButton` | the three button shapes tool rows are built from |
| `game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx` | `selectedInfoSectionComponents` | a map from a section's `__Type` to its component; adding a key adds a section |
| same | `CUSTOMIZE_TAB_SECTIONS` | the `Set` of section types routed to the customize tab |
| `game-ui/game/components/right-menu/right-menu.tsx` | `RightMenu` | the bottom-right button column |
| `game-ui/game/components/toolbar/top/toggles.tsx` | `PhotoModeToggle` | wrapping a known toolbar toggle to add a button beside it |
| `game-ui/game/components/asset-menu/asset-menu.tsx` | `AssetMenu` | the asset picker |
| `game-ui/editor/components/toolbar/toolbar.tsx` | `Toolbar` | the editor toolbar |
| `game-ui/game/data-binding/game-bindings.ts` | `GamePanelType` | the panel-type enum |
| `game-ui/game/components/game-panel-renderer.tsx` | `gamePanelComponents` | the panel-type to component map |
| `game-ui/game/components/photo-mode/photo-mode-panel.tsx` | `PhotoModePanel` | the photo-mode panel, wrappable in a portal |
| `game-ui/menu/components/menu-ui.tsx` | `MenuUI` | the whole main-menu UI |
| `game-ui/menu/components/menu-ui-backdrops/menu-ui-backdrops.tsx` | `MenuUIBackdrops` | the menu backdrop, suppressible |
| `game-ui/menu/components/shared/master-screen/master-screen.tsx` | `MasterScreen` | the menu's screen frame |
| `game-ui/overlay/logo-screen/logo-screen.tsx` | `LogoScreen` | the loading screen |
| `game-ui/game/components/toolbar/bottom/time-controls/time-controls.tsx` | `TimeControls` | the clock widget |
| `game-ui/game/components/toolbar/bottom/time-controls/time-controls-new.tsx` | `TimeControlsNew` | its replacement — both ship, and a mod touching the clock extends both |
| `game-ui/game/components/toolbar/bottom/time-controls/time-controls.module.scss` | `classes` | merging a mod's class map into the vanilla one |
| `game-ui/common/focus/focus-key.ts` | `FOCUS_DISABLED`, `FOCUS_AUTO`, `useUniqueFocusKey` | focus keys — the same values `cs2/input` exports, so the import is the route |

**`extend`'s callback is not required to be a component wrapper.**
The declaration types it as one; the implementation only does `override(path, name, cb(current))`, so any transformer works, and that is how a plain object or an enum is extended.
Source: `extend` in the registry object literal (`Cities2_Data/Content/Game/UI/index.js`).

A mod may `add` a path of its own to the same registry, and every registrar that runs later can `extend` it: `add` throwing on a registered path is what makes that path a stable public surface between mods — and since `reset()` leaves the registry map intact, the second run's `add` throws: inside the `try`/`catch` above it silently costs the mod its own remaining calls, outside it every later mod's; [`mod-compatibility`](../../../cs2-modding/references/technique/mod-compatibility/mod-compatibility.md) owns the guard.
A consumer guards its side too, tolerating the path's absence, since import-completion order decides per run whether the producer has registered yet.

## Tool options: what makes the panel appear

**`useToolOptionsVisible` is a predicate over the `tool` and `toolbar` bindings, and a mod tool reaches only three of its terms.**
The hook returns true only when the active tool is not the selection tool and at least one holds: the toolbar has themes or asset packs, the tool has more than one mode, a non-zero snap mask, an elevation range, parallel mode, colour support, underground support off the default tool, or a selected brush (VOLATILE: the disjunction and its binding names — `useToolOptionsVisible` in `game-ui/game/components/tool-options/tool-options-panel.tsx`, `Cities2_Data/Content/Game/UI/index.js`).
Elevation range, parallel mode and the brush are gated on the active tool being the net, object or terrain system, while colour hangs on the active prefab and themes and asset packs on the toolbar's selected category — state no tool owns; the three terms a mod tool itself owns are `ToolBaseSystem` virtuals, `GetUIModes` returning two or more modes, a non-zero `GetAvailableSnapMask` and `allowUnderground`, each defaulting to the false side.
A mod tool overriding none of them brings nothing that mounts the panel — only leftover toolbar or prefab state still can — so there is no container for the rows a `MouseToolOptions` extension adds until one of those virtuals is overridden or the hook is extended to return true.
Source: `useToolOptionsVisible` in `Cities2_Data/Content/Game/UI/index.js`; `src/Game/Game.UI.InGame/ToolUISystem.cs`, `ToolbarUISystem.cs` beside it and `src/Game/Game.Tools/ToolBaseSystem.cs` for what feeds its terms.

`ToolOptions` renders `GamepadToolOptions` or `MouseToolOptions` by input scheme, then the editor options when in the editor, so rows for both schemes mean extending two modules.

**The mode switcher renders for any tool with two or more modes, and its C# half does nothing for a mod tool.**
The switcher inside `MouseToolOptions` draws one `ValueToolButton` per mode — icon from `mode.icon`, tooltip from the `ToolOptions.TOOLTIP_TITLE[<id>]` and `TOOLTIP_DESCRIPTION[<id>]` keys, selected on `mode.index === activeTool.modeIndex` — and calls the `tool.selectToolMode` trigger on select.
The C# handler type-tests the active tool against the vanilla tool systems with no fallback — [`custom-tools`](../../../cs2-modding/references/technique/custom-tools/custom-tools.md) owns that switch — so a mod tool falls through every branch and the only thing the trigger does is re-push the unchanged active tool — the button's selected state is a pure prop over that binding, so the click changes nothing on screen (VOLATILE: the type test — `ToolUISystem.SelectToolMode` in `src/Game/Game.UI.InGame/ToolUISystem.cs`).
The buttons render and click, and only the effect is missing, so a mod tool's mode switcher is its own: rows extended into `MouseToolOptions`, driven by the mod's own trigger binding.
`selectTool` fails differently: its string switch over the vanilla tool ids falls through to the default tool, so a mod tool id handed to it switches the player off the mod's tool rather than doing nothing (VOLATILE: the id list — `ToolUISystem.GetToolSystem` in the same file).
Source: the mode switcher in `game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx` (`Cities2_Data/Content/Game/UI/index.js`); `src/Game/Game.UI.InGame/ToolUISystem.cs`.
[`custom-tools`](../../../cs2-modding/references/technique/custom-tools/custom-tools.md) owns the C# side of the tool itself.

## Registering a whole new panel type

`game-ui/game/components/game-panel-renderer.tsx` exports `gamePanelComponents`, a map from a `GamePanelType` value to a component, and `GamePanelRenderer`, which hands that map and the active panel payload to the typed renderer keyed on `panel.__Type`, falling through to the unknown-element box.
`GamePanelType`'s values are the C# `__Type` strings — `Game.UI.InGame.ProgressionPanel` and its siblings (VOLATILE: the enum's members — `GamePanelType` in `game-ui/game/data-binding/game-bindings.ts`, `Cities2_Data/Content/Game/UI/index.js`).

A new panel type is two `extend` calls, each mutating the object it receives and returning it:

```ts
moduleRegistry.extend("game-ui/game/data-binding/game-bindings.ts", "GamePanelType", (types: any) => {
  types["MyPanel"] = "MyMod.MyPanel";
  return types;
});
moduleRegistry.extend("game-ui/game/components/game-panel-renderer.tsx", "gamePanelComponents", (components: any) => {
  components["MyMod.MyPanel"] = MyPanel;
  return components;
});
```

The `any` on each parameter is the route past the compiler: `ModuleRegistryExtend` types the callback as a component wrapper, so an unannotated parameter is inferred as a component type and neither the index write nor the object return type-checks.
The C# half is a `GamePanelUISystem` panel class whose emitted `__Type` equals the string added; [`binding-layer`](../binding-layer/binding-layer.md) owns how `__Type` is written.
Only the second call affects rendering — the renderer keys on `gamePanelComponents[panel.__Type]` and nothing enumerates the enum — so the first is bookkeeping for the mod's own code and for other mods reading the enum.
Mutating in place is what makes the registration survive `reset()`, for the reason under composition below.

## Reusing the game's unexported components

**Every module registers with a live getter and setter per export, whatever `cs2/*` re-exports.**
The registration shape is uniform — `add(path, { get X() { return ident; }, set X(v) { ident = v; } })` — so a component the game never exposed is readable through `get(path, name)` and replaceable through the setter, which is what `override` uses.
Nothing is hidden; the empty barrels and the dead accessors above are the only gaps.
Source: the `add` registrations in `Cities2_Data/Content/Game/UI/index.js`.

**A `.module.scss` module registers differently, and the difference decides how styling over vanilla works.**
Its single export is `classes`, and its setter is `Object.assign(target, value)` rather than an assignment, so overriding a class map merges into the existing object instead of replacing it (VOLATILE: the `classes` convention — any `.module.scss` registration in `Cities2_Data/Content/Game/UI/index.js`).
Source: the `left-menu.module.scss` registration in `Cities2_Data/Content/Game/UI/index.js`.

The reusable shape is a lazy map from a name to a `[modulePath, exportName]` pair, read through the registry at first use and cached, with hand-written prop types.
Two traps ride in the common form of it:

- **Reading through `registry.get(path)[name]` gives a worse failure than `get(path, name)`.** Both invoke the getter and both throw on a dead accessor; the first turns a missing path into a `TypeError` on `undefined[name]` instead of the registry's own string.
- **A value cached at first read never sees a later `override`.** Reading eagerly at registrar time captures the export before any mod later in the array has registered; reading lazily at first render captures it after all of them; neither sees an override that lands afterwards, because an export read once is a value, not a subscription.

Source: `get`, `registry` and the loader's registrar loop in `Cities2_Data/Content/Game/UI/index.js`.

The discovery workflow the game offers is its own devtools: launch with UI developer mode on, open the UI debugging port `cs2-modding-setup` documents, pretty-print `index.js`, search for the `.tsx` or `.scss` file name, and read the function it maps to for the props.
The registry is the faster route and needs no debugger: `findModule(<fragment>)` returns `[path, ...exports]` for every match and is safe on every module.

## The packages on `window`

The bundle defines twelve globals in one call: `React`, `ReactDOM`, `ReactDOMClient`, `cs2/api`, `cs2/bindings`, `cs2/l10n`, `cs2/ui`, `cs2/utils`, `cs2/input`, `cs2/modding`, `cohtml/cohtml` and `chart.js`; that is exactly the set present on the running game (VOLATILE: the global names and each package's export set — the `window` definitions and the package export bindings in `Cities2_Data/Content/Game/UI/index.js`).

| Global | Provides |
| --- | --- |
| `cs2/api` | `bindValue`, `bindMap`, `bindEvent`, `bindTrigger`, `bindTriggerWithArgs`, `bindLocalValue`, `call`, `trigger`, and the `useValue`, `useMapValue`, `useReducedValue` hook family |
| `cs2/bindings` | the vanilla binding namespaces — `tool`, `toolbar`, `game`, `selectedInfo`, `prefab`, `time`, `map`, `economyBudget` and the rest — not bindings themselves |
| `cs2/l10n` | `Localized`, `LocalizedString`, `LocalizedNumber`, `LocalizedFraction`, `LocalizedPercentage`, `LocalizedBounds`, `LocalizedEntityName`, `LocalizedDate`, `LocalizedDuration`, `Unit`, `useLocalization` |
| `cs2/ui` | `Button`, `FloatingButton`, `MenuButton`, `Icon`, `Panel`, `PanelSection`, `PanelSectionRow`, `PanelFoldout`, `Dropdown` and its items, `Tooltip`, `Portal`, `Scrollable`, the dialog family, and the formatted and markdown text renderers |
| `cs2/utils` | `entityEquals`, `entityKey`, `parseEntityKey`, `shallowEqual`, `isNullOrEmpty`, `formatLargeNumber`, `useFormattedLargeNumber`, `preloadImages`, `useCssLength`, `useMemoizedValue`, `useRem` |
| `cs2/input` | focus, navigation, input hints, control icons, gamepad, input barriers, and the focus-key trio |
| `cs2/modding` | `findModule` and `getModule` — two exports, and nothing else |

**In the scaffold's generated `cs2/*` declaration files, a doubled `export export` marks a real runtime export, and a single `export` marks a declaration emitted for typing only.**
The distinct value names carrying the doubled keyword, plus the value entries of the trailing `export { … }` alias block, are the runtime export set of every value-carrying declaration file — distinct names, since an overloaded function is declared twice, and value entries, since the alias block also renames types.
So a value `ui.d.ts` exports once is not in `cs2/ui` under that name, and it meets one of three fates: renamed by the alias block (`InfoSection`, `InfoSectionFoldout` and `InfoRow` reach a mod as `PanelSection`, `PanelFoldout` and `PanelSectionRow`), shipped by another package (`Unit` by `cs2/l10n`, the `FocusSymbol`/`FOCUS_DISABLED`/`FOCUS_AUTO` trio by `cs2/input`), or on no package at all (`UISound`, `ScrollController`, `ParagraphStyle` among them); `api.d.ts`'s `LocalValueBinding` and `ListenerRef` are not exported, though `bindLocalValue` returns one; `l10n.d.ts`'s `LocElementType`, `TimeFormat`, `TemperatureUnit`, `UnitSystem` and `NameType` do not exist under `cs2/l10n` at runtime (VOLATILE: those declaration files — the scaffold's `types/`).
The one exception is the `HTMLImageElement`, `Element` and `MorphAnimation` interface trio repeated at the end of most of the declaration files: interfaces have no runtime value, so the rule applies to value declarations only.
The hand-written `cohtml.d.ts` and `react.d.ts` ambients sit outside the rule entirely — `cohtml.d.ts`'s singly-exported `engine` is real.
Source: the scaffold's `types/ui.d.ts`, `types/api.d.ts`, `types/l10n.d.ts`; the package export bindings in `Cities2_Data/Content/Game/UI/index.js`.

**`cs2/assets` is not a package.**
The scaffold's `assets.d.ts` is wildcard module declarations for `*.scss`, `*.css`, `*.svg`, `*.png`, `*.jpg` and `*.gif` — the ambient typing that lets a mod `import icon from "./icon.svg"` — and there is no such window global, read from the running game, and no such external.
Source: the scaffold's `types/assets.d.ts`.

`ReactDOMClient` and `chart.js` are on `window` and absent from the scaffold's webpack externals, so a mod importing `chart.js` bundles its own copy beside the one the game already ships; whether adding the external yourself reaches the game's copy instead is [`ui-build-and-devloop`](../ui-build-and-devloop/ui-build-and-devloop.md)'s to answer.

## Styling

**One rem is one pixel at 1920×1080 and scales by the smaller of `width/1920` and `height/1080`.**
The stylesheet sets `html { font-size: 0.0925926vh }`, switching to `0.0520833vw` under `@media (min-height: 56.25vw)` — `100/1080` and `100/1920` — which is why every game and mod stylesheet writes sizes in `rem` and a mod writing `px` breaks at every other resolution (VOLATILE: the two values — the `html` rule in `Cities2_Data/Content/Game/UI/index.css`).
Turning the player's interface-scaling toggle off overrides it outright by pinning `document.documentElement.style.fontSize` to `1px`.
Source: the `html` rule in `Cities2_Data/Content/Game/UI/index.css`; `applyInterfaceScalingEnabled` in `game-ui/common/app/interface.ts` (`Cities2_Data/Content/Game/UI/index.js`).

The same module's siblings drive the rest: `applyInterfaceStyle` swaps a `style--<name>` class on `<body>`, `applyInterfaceTransparency` writes `--panelOpacityNormal`, `--uiOpacity` and `--panelOpacityDark` and toggles `no-panel-blur`, `applyTextScale` writes `--fontScale` and `--fontScaleChange`, `applyToolbarScale` writes `--toolbarScale` (VOLATILE: the function names, class names and custom properties — `game-ui/common/app/interface.ts` in `Cities2_Data/Content/Game/UI/index.js`).
**A font size is written through `--fontScale`**, either by using one of the stylesheet's own `--fontSizeXXS` … `--fontSizeXXXL` tokens — an eight-step ladder, each a `calc(<n>rem * var(--fontScale) …)` — or by multiplying a `rem` value by the variable directly; a hard-coded size ignores the player's text-scale setting.
Source: the `--fontSize*` declarations and the `--fontScale` uses in `Cities2_Data/Content/Game/UI/index.css`.

**Every class a `.module.scss` emits is a CSS-module local suffixed with a three-character hash**, `[local]_[hash:base64:3]` — the hash covers the module's path and the local name, never the rule's declarations — and the scaffold configures a mod's build identically, so a mod's classes look exactly like the game's; prefixing every local name with the mod's own is what keeps the pre-hash half unique (VOLATILE: `localIdentName` — the scaffold's `webpack.config.js`).
Classes declared outside a module stay unhashed and survive patches: the `<body>` state classes — `style--<name>`, `no-panel-blur`, `overwrite-legacy` and its partner `legacy-interface` — and bare state names such as `selected`, `checked` and `disabled`.
Source: the scaffold's `webpack.config.js`; the `body` and `:root` rules in `Cities2_Data/Content/Game/UI/index.css`.
Two shapes appear in the registered class maps, and both matter to a mod matching on them:

- `exportLocalsConvention: "camelCase"` adds a camel alias beside a dashed local's kebab name, both pointing at the same hashed string — `{"left-menu": "left-menu_L1D", leftMenu: "left-menu_L1D"}` — while an undashed local appears once.
- A value can be several classes where the SCSS composes: `item: "item_RBL item-focused_FuT"`.
  Matching a class-map value with `===` fails where matching by token does not.

**`extend` on a path containing `.scss` takes two forms, and the second is not undoable.**
With a callback in the third position, `extend(path, "classes", cb)` routes `cb(currentClasses)` through `override(path, "classes", …)` and the callback returns the whole new map — give it an `any` parameter and return the map through it, since the declaration types this callback as a component wrapper too; a callback in the second position with nothing after it throws, since the two-argument form expects the object below.
With a plain object, `extend(path, {local: "myHashedClass"})` copies the current map, appends a space and the value to each named key — creating it empty first if absent — and overrides, so a vanilla `item: "item_bZY"` becomes `"item_bZY item_MyHash"` and both rule sets apply.
Source: `extend` in the registry object literal (`Cities2_Data/Content/Game/UI/index.js`).

**`reset()` cannot undo an SCSS extend.**
`override` records the current value once, and for a `.module.scss` module that value is the live `classes` object; the setter is `Object.assign` onto that same object, so the write mutates what the backup points at, and `reset()` replays an assignment of the object onto itself.
Since `reset()` runs at the head of every mod-list reload, a mod using the SCSS extend leaves its classes in the vanilla map for the rest of the session, and the appended class string grows by one copy per reload, since `extend` copies the live map that already carries the mod's class.
Source: `override`, `extend` and `reset` in the registry object literal (`Cities2_Data/Content/Game/UI/index.js`).

## Images from the page's side

**The page is served over `assetdb://`, and `coui://gameui` does not exist.**
`document.baseURI` is `assetdb://gameui/index.html`, read from the running game, so a bare `Media/Glyphs/Advisor.svg` in JSX — the form the game uses everywhere, and the form of the mode-icon path [`custom-tools`](../../../cs2-modding/references/technique/custom-tools/custom-tools.md) says a mod tool's modes resolve to nothing under — resolves against `assetdb://gameui/`; the same path under `coui://gameui/` returns 404.
`gameui` is a database host the base game and each UI-shipping content pack contribute to; [`prefabs-and-assets`](../../../cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md) owns its registration.
Source: `Cities2_Data/Content/Game/UI/index.html` and `gameui.uiHost`; `src/Game/Game.SceneFlow/GameManager.cs`.

A mod's own files come through `coui://ui-mods/`, which the scaffold's webpack config makes the emitted `publicPath`, imported images landing under `images/` beneath it; a mod registering its own host reaches it as `coui://<host>/…`, which [`prefabs-and-assets`](../../../cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md) owns along with the C# call (VOLATILE: `coui://ui-mods/` — `UIModuleAsset` in `src/Colossal.IO.AssetDatabase/` and the scaffold's `webpack.config.js`).

The frontend's own image helpers, all registry-reachable (VOLATILE: the paths and exports — their `add` registrations in `Cities2_Data/Content/Game/UI/index.js`):

- `game-ui/ui/icon.tsx` → `Icon` renders `<img data-src={src} src={src} onError={handleThumbnailError}>`, or a tinted variant that is a `mask-image` div rather than an `<img>`; the `data-src` duplicating `src` is the durable selector anchor for anything driving the DOM.
- `game-ui/common/utils/thumbnails-errors.ts` → `handleThumbnailError` rewrites a failed `src` to a `Media/Editor/Thumbnails/Fallback_<Kind>.svg` or `Media/Placeholder.svg`.
- `game-ui/common/image/preload.ts` → `preloadImages` (the one `cs2/utils` re-exports) records each URL as a key, `usePrefetchImages` later constructs an `Image()` for each unrealised key and stores it forever, and nothing ever removes a key.

**An image at an unchanged URL is never re-fetched, and a changed query string is the only lever.**
Read from the running game with a throwaway host, an SVG behaving the same as a PNG.
The cache is keyed on the full URL string including the query and each key is pinned to the bytes it first resolved to, so a fixed `?v=` is no better than none.
A DOM `<img>` behaves identically to a detached `Image()`, a `location.reload()` does not evict it, and `ClearCachedUnusedImages()` on the UI system did not evict it with the `<img>` removed and frames advanced between two calls (UNVERIFIED: whether that entry genuinely counted as unused, since nothing proved from outside that no reference to the decoded image remained — settled by a Cohtml-side statement of what "unused" means, which is the sibling `coherent-gameface` plugin's material).
It is the image cache and not a resource cache: the resource handler issues a fresh request per attempt, so a rebuilt `.mjs` or `.css` at an unchanged URL is picked up on reload.
Source: `RequestResourceAsync` in `src/Colossal.UI/Colossal.UI/DefaultResourceHandler.cs` for the resource half; the image half was read from the running game.

A mod writing an image once per file name is fine; a mod rewriting a fixed file name — a screenshot preview, a live chart, a recoloured icon — sees the first version forever unless it changes the query on every write — and each new query pins another decoded copy, so a high-frequency writer grows the cache for as long as nothing evicts — and nothing observed did.
The other way a `coui://` image serves bytes that are not the ones on disk is a raster whose base name collides with a shipped shared image, which the resource handler short-circuits before the host walk; [`prefabs-and-assets`](../../../cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md) owns that, and the two are indistinguishable from the page.

## The DOM hack the community moved past

The superseded technique is C# reaching into the page through `ExecuteScript` with a string of JavaScript that walks `document.getElementsByTagName("img")`, matches `src.includes("<file>.svg")` and toggles a class.
[`binding-layer`](../binding-layer/binding-layer.md) owns why it cannot work as a channel; the frontend half is three failures of its own:

- it matches on a rendered attribute, which a re-render, a theme change or a renamed shipped asset silently breaks;
- it writes `className` under React, which the next render of that subtree discards;
- it targets a hashed class name, which a renamed local or a moved `.module.scss` in the vanilla bundle silently changes.

The replacement is the registry: `MouseToolOptions` for the rows, `ToolButton` for the button, `useToolOptionsVisible` for the container, and the mod's own binding for the selected state.

## Composition: what chains, what clobbers, what survives a reset

- **`extend` chains.** It is `override(path, name, cb(current))`, so the second mod's callback receives the first mod's result, and every extension of one export composes into one wrapper chain, outermost being the mod that registered last.
- **`append` chains too, and across anchors**, because every anchor append extends the same `ModdingHook` export and each wrapper filters on the rendered hook's `name` prop, passing through unchanged when it does not match; the wrapper sets `displayName` to `Extended ModdingHook:<Anchor>+`, which is how a chain reads back in the component tree.
- **`override` clobbers.** The second mod's value replaces the first's, and the backup still holds the original because it records once.
- **`add` throws** on a registered path.
- **Order is import-completion order**, not playset order, and nothing a mod can do makes it deterministic.
- **`reset()` is global and runs first**, restoring every recorded override and clearing the append set before any registrar runs.
  Its blind spots: the registry map itself, which keeps every added path; an SCSS class map, for the aliasing reason above; and anything a mod mutated in place — the panel-type registration above mutates and returns the same object, so `reset()` re-installs the object the mod already added its key to.
- **One throwing registrar silences every mod after it**, in an order that varies per run, and on a reload of the UI page blanks the whole UI.

Source: the registry object literal and the root component's module loader (`Cities2_Data/Content/Game/UI/index.js`).
[`mod-compatibility`](../../../cs2-modding/references/technique/mod-compatibility/mod-compatibility.md) owns the playset-level consequences; this reference states the mechanism.

## What this reference hands to others

[`binding-layer`](../binding-layer/binding-layer.md) owns the C# end of every wire and the `__Type` contract; this reference owns the far side — the data-binding module, the typed renderer and its unknown-element box, and the component maps (`gamePanelComponents`, `selectedInfoSectionComponents`) that are each a `__Type` payload rendered through a map; the `Loc` dictionary rides beside them, keyed on localization ids rather than `__Type`.

[`ui-build-and-devloop`](../ui-build-and-devloop/ui-build-and-devloop.md) owns everything upstream of the `.mjs`; the contract that build satisfies is stated here — `default` and `hasCSS`, a file named after the mod id, `coui://ui-mods/` as the public path, the hashed `localIdentName` — and so is the dev-loop fact that a rebuilt `.mjs` or `.css` at an unchanged URL is picked up on reload while an image is not; a blank UI after a rebuild-and-reload is the loader's — a throwing registrar or a stylesheet not answering 200.

[`custom-tools`](../../../cs2-modding/references/technique/custom-tools/custom-tools.md) owns the tool; it gets `useToolOptionsVisible` — the extension a tool overriding none of `ToolBaseSystem`'s panel-triggering virtuals needs — and the mode switcher's silent C# half from here, with `selectTool`'s fall-through to the default tool.

[`localization`](../../../cs2-modding/references/technique/localization/localization.md) owns what goes into a localized string; it gets the `Loc` dictionary and its four key shapes from `promised-registry-paths.md`.

[`units-and-formatting`](../../../cs2-modding/references/technique/units-and-formatting/units-and-formatting.md) and [`simulation-time-and-units`](../../../cs2-modding/references/mechanics/simulation-time-and-units/simulation-time-and-units.md) get the formatters `cs2/l10n` withholds, the three unit-preference enums and the `Unit` enum from the same file, with the caveat that the module carrying the enums carries dead accessors beside them; [`simulation-time-and-units`](../../../cs2-modding/references/mechanics/simulation-time-and-units/simulation-time-and-units.md) also gets the time-bindings module and the twice-shipped clock widget.

[`settings-and-input`](../../../cs2-modding/references/technique/settings-and-input/settings-and-input.md) gets the options-screen modules and the widget renderer that draws a settings page, and the fact that the focus-key trio is `cs2/input`'s.

[`mod-compatibility`](../../../cs2-modding/references/technique/mod-compatibility/mod-compatibility.md) gets the composition list, the unguarded registrar loop, the `hasCSS` hang that keeps every mod unregistered, import-completion order and `reset()`'s blind spots.

[`prefabs-and-assets`](../../../cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md) keeps the C# registration of a host and the shared-image shadowing; it gets from here that `gameui` is an `assetdb` host rather than a `coui` one, that the page's base is `assetdb://gameui/`, and the image-cache answer.

[`city-state-and-progression`](../../../cs2-modding/references/mechanics/city-state-and-progression/city-state-and-progression.md) and [`citizens-and-households`](../../../cs2-modding/references/mechanics/citizens-and-households/citizens-and-households.md) get the panel components and the selected-info section map as the registry route to replacing a panel or adding a section, cheaper than forking the C# system behind it.
