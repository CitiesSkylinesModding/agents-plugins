# The UI build and the dev loop

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The build-side claims below check against the scaffold's own files and the toolchain's, but the manifest parser, the shared module host, the file watcher and the debugger switches are game C#, and without the tree none of those can be confirmed.
`cs2-modding-setup` provisions it.

A `Source:` line citing `create-csii-ui-mod/<path>` names the scaffold's own files, which resolve under `<install>/Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod/`.

How a UI mod's frontend is built and iterated on.
Hooking this build into the C# build is `cs2-mod-project`'s — its skill body owns the scaffold invocation and the hook, and its build-pipeline reference the supported hook moves; this reference starts at the build the hook invokes.
What the built module does once loaded — the registry, injection, the page — is `frontend-and-injection`'s, and the C#-to-JS binding protocol is `binding-layer`'s.

## What `npm run build` produces, and where it lands

The scaffold's webpack config is the whole build; the MSBuild toolchain ships no UI stage at all.
Its output path is `${CSII_USERDATAPATH}\Mods\${MOD.id}` — the installed mod folder itself, with no intermediate `dist/`, so `npm run build` *is* the install.
The config throws before webpack starts when `CSII_USERDATAPATH` is unset, naming the missing variable.

Four kinds of file come out, and every name is load-bearing (VOLATILE: the produced file set and its names — `create-csii-ui-mod/template/webpack.config.js`, `UIModuleAsset.kExtension`, and a built mod's own folder for the names the config leaves to webpack's defaults):

- `<id>.mjs`, the entry bundle — the entry key is `mod.json`'s `id`, and the `.mjs` extension is webpack's own default once `experiments.outputModule` is on;
- `<id>.css`, only when the mod imported any style — the CSS extract plugin's default filename is the same entry key;
- `images/[name][ext][query]`, every imported `.png`, `.jpg`/`.jpeg`, `.gif` and `.svg`, emitted as `asset/resource`;
- `<id>.mjs.LICENSE.txt`, Terser's extracted third-party licence comments — the mod's own manifest banner is not in it, that is prepended to the `.mjs`.

**The `.mjs` extension is what makes the file a UI module at all.**
The asset factory maps `.mjs` to `UIModuleAsset`, whose `kExtension` is the literal `".mjs"`; a build that emits any other extension installs a file the game never indexes.
Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/DefaultAssetFactory.cs`, `UIModuleAsset.cs`.

`npm run build` also type-checks: the template compiles `.tsx?` through ts-loader with no `transpileOnly`, so a type error fails the build rather than shipping.
The template's tsconfig sets `typeRoots: ["./types"]`, which turns off the automatic `node_modules/@types` globals; the `cs2/*` declarations arrive as ordinary project files, and React's types still resolve because imports reach them through module resolution rather than `typeRoots`.
Source: `create-csii-ui-mod/template/webpack.config.js`, `template/tsconfig.json`.

## One host, every mod

`ModManager.InitializeUIModules` walks every `UIModuleAsset` in the database and registers the directory holding each `.mjs` as a location of the single resource host `ui-mods`; the module's URL is `coui://ui-mods/<filename>`.
The directory, not the mod root: a bundle written to a subfolder of the mod registers that subfolder as a host location like any other, so the module does not have to sit beside the `.dll`.
The module's lifecycle is its own: `InitializeUIModules` runs after the assembly loop whether or not the mod's C# loaded, and a playset toggle adds or removes a module's host location mid-session; `Modding.log` records each registration as a `Registered UI Module` line.
Source: `src/Game/Game.Modding/ModManager.cs`, `src/Game/Game.SceneFlow/GameManager.cs`, `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/UIModuleAsset.cs`.

**Every mod's `images/` output resolves under that one shared prefix, and which mod's file answers a colliding name is decided by nothing a mod controls.**
The resource handler tries the host's registered locations in list order and returns the first that answers; the list is sorted by a priority the mod manager never passes, so every mod sits at priority 0 and the order among equals is an unspecified binary-search insertion.
The `.mjs` and `.css` carry the mod id and never collide, but the scaffold emits images as `images/[name][ext][query]` under `publicPath: "coui://ui-mods/"`, so two mods each shipping `images/icon.svg` request the same URL and one gets the other's file.
The choice is the author's: rename the images output directory to something mod-prefixed — the asset rule's `generator.filename`, which the emitted URL follows — or keep the scaffold default and accept the shared namespace.
The rename settles only the mod-versus-mod race: a raster request — `.png`, `.jpg`/`.jpeg`, `.gif`, not `.svg` — is first looked up in the game's own `UI/SharedImages` by bare file name with the directory dropped, so a base name the game also ships is shadowed whatever the directory; `prefabs-and-assets` owns that branch.
Source: `src/Colossal.UI/Colossal.UI/DefaultResourceHandler.cs`, `UISystem.cs`, `create-csii-ui-mod/template/webpack.config.js`.

## The externals contract

The game defines its shared modules as read-only properties on `window` — `React`, `ReactDOM`, `ReactDOMClient`, `cs2/api`, `cs2/bindings`, `cs2/l10n`, `cs2/ui`, `cs2/utils`, `cs2/input`, `cs2/modding`, `cohtml/cohtml` and `chart.js` — and the scaffold's `externals` maps ten import specifiers onto them with `externalsType: "window"` (VOLATILE: the `window` module list and the externals map — the `Object.defineProperties(window, …)` block in `Cities2_Data/Content/Game/UI/index.js` and the template's `externals`).
An import that goes through the map costs the bundle nothing and uses the game's copy; everything else is bundled.
The properties are defined with bare `value` descriptors, which leaves them non-writable and non-configurable, so a mod cannot replace `window.React` or any of the others.
Source: `Cities2_Data/Content/Game/UI/index.js`, `create-csii-ui-mod/template/webpack.config.js`.

**Two of the game's modules are on `window` and missing from the scaffold's map: `ReactDOMClient` and `chart.js`.**
Importing `chart.js` as an ordinary dependency therefore bundles a second copy beside the one the game already ships — Chart.js 4.0.1 (VOLATILE: the Chart.js version — `window["chart.js"].Chart.version` in a running game).
Adding the missing mapping to `externals` yourself is the route to the game's copy (UNVERIFIED: whether an added `"chart.js": "chart.js"` external resolves at build time — building a mod that declares it and loading it would settle it).
Source: `Cities2_Data/Content/Game/UI/index.js`, `create-csii-ui-mod/template/webpack.config.js`.

The scaffold's own `types/bindings.d.ts` opens on an import from `chart.js`, a package the template never installs; the build survives because the template's tsconfig sets `skipLibCheck: true`, and turning that off surfaces an unresolved import in a declaration file you did not write.
Source: `create-csii-ui-mod/template/types/bindings.d.ts`, `template/tsconfig.json`, `template/package.json`.

## The manifest, and the identity it shares with the C# half

`mod.json` is four keys — `id`, `author`, `version`, `dependencies` — and the webpack config is what turns them into a manifest: it interpolates them into a comment banner with the capitalised keys `Id`, `Author`, `Version`, `Dependencies` and delivers it through Terser's `extractComments.banner` option, which is where to look when a built module carries no manifest.
Source: `create-csii-ui-mod/template/mod.json`, `template/webpack.config.js`.

The game reads the banner back with its own parser (VOLATILE: the sentinel line and the key set — `UIModuleAsset.ProcessLine`).
It scans for the first `/*` line and reads the `*`-prefixed lines from there: a blank line is skipped, a line opening on `Cities: Skylines II UI Module` passes only when it carries nothing else — extra text on that sentinel stops the parse — and any other line without a colon stops it too; the recognised keys are the four above, an unrecognised key is ignored and the parse continues, and the module is valid the moment `Id` parsed — the sentinel itself is not required.
**A semver pre-release in `Version` parses to nothing and is dropped silently**, because the value goes through `System.Version.TryParse`, which takes only numeric `major.minor[.build[.revision]]`.
`Dependencies` is inert: it is written into the asset's tags and nothing in the game reads them back, so it orders nothing and gates nothing.
Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/UIModuleAsset.cs`.

`cs2-mod-project` states the identity rule — `mod.json`'s `id` equals the csproj's `TargetName`; what holds it up is three derivations of one name that nothing reconciles:

- the C# half deploys to `$(LocalModsPath)\$(TargetName)`, with `LocalModsPath` read from the user-scope `CSII_LOCALMODSPATH`;
- the UI build writes to `${CSII_USERDATAPATH}\Mods\${MOD.id}`, composed from the process environment;
- the module's URL is `coui://ui-mods/<id>.mjs`, from the file name alone.

**The two halves' deploy paths come from two different environment variables, so repointing `CSII_LOCALMODSPATH` moves the C# half and strands the UI half.**
Source: `%CSII_TOOLPATH%/Mod.props`, `%CSII_TOOLPATH%/Mod.targets`, `create-csii-ui-mod/template/webpack.config.js`.

The scaffold is how the mismatch usually happens: it prompts for a name, creates a directory of exactly that name and writes the same string into `mod.json` as `id`, so scaffolding a folder called `UI` inside the C# project yields a mod whose id is `UI`.
It offers no way to get a differently named folder and id in one step; rename the directory or edit `mod.json` afterwards, and make the result the assembly name.
Its validator also rejects any character outside `[a-zA-Z0-9_-]`, but that is the scaffold's rule rather than the game's — nothing in the game's parser re-validates the id, and a module id carrying a space registers and serves.
Source: `create-csii-ui-mod/utils/create-mod-project.js`.

The same `id` is the natural binding group: import `mod.json` into the TypeScript and pass `mod.id` wherever a group name is needed, and the two halves cannot drift, because the group string derives from the same manifest field as the deploy folder.
Nothing in the game requires this; `binding-layer` owns what a group is.

## Refreshing the declarations after a game update

The globally installed scaffold is not a copy that ages: the toolchain installs it by running `npm link` inside `<install>/Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod`, so the npm-global package name is a junction into the game's own files, and a patch that ships new `types/*.d.ts` changes what `npx create-csii-ui-mod` hands out immediately, with no npm step.
That holds only while the entry is the junction: the toolchain's own up-to-date check reads nothing but the link's target — an entry with no link at all reports current forever — so what to verify is that the global entry links to the install path.
Source: `src/Game/Game.Modding.Toolchain.Dependencies/NpxModProjectDependency.cs`.

The project's own copy does age, and `npm run update` is the one refresh: it copies the template's `tsconfig.json`, `types/`, `tools/` and `webpack.config.js` over the project's, rewrites the pinned dependency versions and the `scripts` block into the project's `package.json`, and runs `npm install` (VOLATILE: the scaffold's file layout and the `update` copy list — `create-csii-ui-mod/template/types/` and `create-csii-ui-mod/utils/update-mod-template.js`).
The copy overwrites file by file and deletes nothing except one hard-coded legacy declaration file, so any other file a later template dropped survives in the project.
**`npm run update` overwrites `webpack.config.js` and `tsconfig.json`, taking any edit with them.**
A mod that diverges from the stock config — a renamed images directory, a source-map plugin, a loader option — re-applies the divergence after every update or keeps the config stock; there is no merge.
Source: `create-csii-ui-mod/utils/update-mod-template.js`, `utils/copy-async.js`.

**A vendored `types/` folder that is never refreshed fails silently.**
The mod compiles against whatever it vendored, so a binding payload whose shape changed in a game update reads as the old shape with no error anywhere.
The alternatives are the author's: run `npm run update` as part of the game-update ritual, or take the declarations out of the repository entirely — point the tsconfig `include` at a copy shipped as a versioned dependency, so the refresh becomes a version bump with a reviewable diff.
Source: `create-csii-ui-mod/template/types/`, the current shape a vendored copy is checked against.

**`clean` deletes the C# half too.**
The subcommand removes `<CSII_USERDATAPATH>/Mods/<id>` recursively, and that is the shared deploy folder — the `.dll`, the `.pdb` and the Burst libraries go with the bundle.
Source: `create-csii-ui-mod/utils/clean-installed-local-mod.js`.

## The stylesheet contract

A built module's contract with the page is two named exports, `default` and `hasCSS`; `frontend-and-injection` owns the loader that reads them.
The build side of the second is a text substitution: the scaffold's CSS-presence tool taps webpack's `processAssets`, checks whether any emitted asset ends `.css`, and splices `const hasCSS = <bool>; export { hasCSS, ` over the first literal `export {` in each `.mjs`.
**The splice works only because it runs before Terser** — the minified `export{` no longer matches the pattern, so a plugin reordered after minification injects nothing, silently.
The scaffold's `types/validateTypes.ts` comment claims only the default export is processed; the game's loader reads both, and the tool exists to emit the export the comment says is ignored.
Source: `create-csii-ui-mod/template/tools/css-presence.js`, `template/types/validateTypes.ts`, `Cities2_Data/Content/Game/UI/index.js`.

**A truthy `hasCSS` whose stylesheet does not answer takes down every mod's UI, not just this one's.**
The page derives the stylesheet URL by replacing `.mjs` with `.css` in the module's own URL, and its loader counts the stylesheet as a completion only a successful load delivers — so the names must match, `<id>.css` beside `<id>.mjs`, which is what the CSS extract plugin's default gives and a custom `filename` takes away.
Hand-writing `export const hasCSS = true;` in the entry module and dropping the presence tool is an equivalent build, under the same naming obligation.
Source: `Cities2_Data/Content/Game/UI/index.js`.

Every mod's stylesheet lands in the same document as the game's and every other mod's.
The scaffold's `localIdentName` is `[local]_[hash:base64:3]` — a three-character hash space shared by every mod; prefixing it with the mod's own tag removes the collision and makes the classes addressable from outside, at the price of an edit the next `npm run update` reverts (VOLATILE: `localIdentName` — the scaffold's `webpack.config.js`).
And css-loader resolves every `url()` as a module by default, so a stylesheet referencing one of the game's shipped images by its install path (`url(Media/…)`) fails the build; a `url.filter` that skips those prefixes leaves them for the game to resolve at runtime.
Source: `create-csii-ui-mod/template/webpack.config.js`.

## The inspector port

One launch flag turns on three switches at once: `--uiDeveloperMode` sets `enableDebugger`, `enableMemoryTracking` and `liveReload` on the UI manager, so the inspector and the file-watching reload arrive together and the command line offers no way to have one without the others.
The single-dash spelling parses identically; write the double dash.
The debugger listens on port 9444, and turning it on also forces `Application.runInBackground` to true — which is why a debugging session survives alt-tabbing away from the game.
`UI.log` confirms it at startup with an `Inspector initialized` line naming the port, and `SceneFlow.log` opens on the command line the game actually got, so the flag's presence is checkable after the fact.
Source: `src/Game/Game.SceneFlow/GameManager.cs`, `src/Colossal.UI/Colossal.UI/UIManager.cs`, `UISystem.cs`, `src/Cohtml.Runtime/cohtml/CohtmlSystemSettings.cs`.

The endpoint at `http://localhost:9444` is a Chrome-DevTools-protocol server exposing one page target, the game's whole UI at `assetdb://gameui/index.html`.
A browser's DevTools attach to it, and so does any other CDP client — the sibling `coherent-gameface` plugin, application-agnostic, drives the same port with its `game_*` tools; what a session there reaches (the live DOM, binding reads, the module registry) is `frontend-and-injection`'s.

## The reload loop

The dev loop is a game-side file watcher, not a dev server: with the flag on, the view constructs a `UILiveReload` that watches the directories behind its host locations, so a rebuild that rewrites files in a watched mod folder reloads the UI in place — `npm run dev` (whose invocation `cs2-mod-project` owns) is the same build re-running on source changes, with the game doing the reloading.
**Only a locally installed mod is watched.**
A watcher is created for a later-registered host location only when the registration asked for one, and the mod manager passes `asset.isLocal` as that flag — the copy under `%CSII_LOCALMODSPATH%` reloads on rebuild, and a Paradox-installed copy of the same mod never does.
Source: `src/Colossal.UI/Colossal.UI/UIView.cs`, `UILiveReload.cs`, `src/Game/Game.Modding/ModManager.cs`.

What a change triggers depends on the extension, and `.mjs` is not on the list you would expect (VOLATILE: the extension list, the two debounces and the `Reloading` log lines — `src/Colossal.UI/Colossal.UI/UILiveReload.cs`).
`IsPageFileExtension` is exactly `.js`, `.css` and `.html`: those debounce one second and end in a plain `View.Reload()`.
Everything else — the `.mjs` itself included — is classified Media: the view navigates away to a holding page at once, and half a second after the last event — the debounce re-arms on each — it sweeps the unused-image cache and navigates back, a full document teardown and fresh load.
A deletion is a change like any other, so wiping the folder reloads too, and a mixed batch takes the Media path, because a pending Media reload refuses the upgrade to Page.
The signature in `UI.log` is `Reloading media <viewId>` or `Reloading page <viewId>`, and the watcher is a native file-system watcher rather than a poll; each new event re-arms the debounce, so the reload lands one debounce after the last write a build makes.
A build that deletes the watched folder itself — the C# deploy's wipe — sends the watcher into a missing state that re-arms by a probe on a growing delay, and writes landing in that window are dropped without a reload; a further write after the folder is back is the recovery.
Source: `src/Colossal.UI/Colossal.UI/UILiveReload.cs`, `src/Colossal.IO/Colossal.IO.FileSystem/BufferedFileSystemWatcher.cs`.

The watcher also knows a webpack-dev-server mode — a `.dev-server-active` marker file plus a listener on TCP port 9000 switches the view to `http://localhost:9000`.
A mod cannot enter it: the marker path is derived from the host location of the view's own main document, which is the game's UI directory, so this is the loop the game's own developers use on the game UI and not one a mod reaches.
Source: `src/Colossal.UI/Colossal.UI/UILiveReload.cs`, `UIView.cs`.

What the inspector shows is minified: the template builds in production mode and sets no `devtool`, so the `.mjs` an author debugs has no mapping back to the source.
Setting `devtool: false` explicitly and adding webpack's `EvalSourceMapDevToolPlugin` builds mappings into the emitted code (UNVERIFIED: whether the inspector honours an eval source map — loading a mod built with one and opening its sources over the port would settle it).
Source: `create-csii-ui-mod/template/webpack.config.js`.

## The seam with the C# build

`cs2-mod-project` owns the hook — the `Exec` in the csproj and `AfterTargets="DeployWIP"` as its documented place.
`DeployWIP` is a `RemoveDir` of `$(DeployDir)` followed by the copy from the C# output, and the scaffold's webpack writes *into* `$(DeployDir)`, so the wipe is what a UI build has to land after.
**A UI build hooked earlier than the wipe is deleted by it, and nothing reports the loss**: the build stays green, and a running game's only evidence is the 404 it logs for the missing bundle — with the dev loop on, the watcher also reloads the view over the emptied folder.
Source: `%CSII_TOOLPATH%/Mod.targets`.

**MSBuild runs targets sharing an `AfterTargets` anchor in declaration order, so a UI target hooked `AfterTargets="AfterBuild"` survives only while it is declared below the toolchain imports.**
The official project template places the two imports immediately after the first property group — its own comment says they must come after it — which is why the earlier anchor appears to work; move an import below the target and the same target runs before `DeployWIP`, its output is wiped, and the build still reports success.
Two shapes do not depend on declaration order: hook the build `AfterTargets="DeployWIP"`, or build to a directory of your own and hook only a copy after `DeployWIP` — the shape the toolchain's own header comment demonstrates.
Source: `%CSII_TOOLPATH%/Mod.targets`, `content/ModTemplate.csproj` in `ColossalOrder.ModTemplate.1.0.0.nupkg` under `Cities2_Data/Content/Game/.ModdingToolchain/`.

**`UI.log` names the exact failing `coui://` URL when an artefact is missing** — a `ResourceHandler: HTTP/1.1 404 Not Found` line with the URL on the indented line under it — which makes it the first read for every missing-or-misnamed-output case (VOLATILE: the log text — the `RespondWithFailure` format string in `DefaultResourceHandler.cs`).
A missing `.mjs` is the benign case: the page's import rejection is caught and counted, so the rest of the UI comes up; the stylesheet branch is the one with no catch, per the contract above.
Source: `src/Colossal.UI/Colossal.UI/DefaultResourceHandler.cs`, `Cities2_Data/Content/Game/UI/index.js`.

## Generating the frontend's types from the C#

The binding names, enum values and payload shapes exist on both sides of the wire, and hand-writing them twice is the default failure: each half compiles alone while drifting from the other.
The mechanism that removes the drift is generation from the C#, workable with any typings generator or a reflection pass of your own; what matters is what the generation must do:

- run from the C# build, so the output is as current as the assembly;
- emit one TypeScript module into the UI source tree, where the frontend imports it like any other file;
- export the binding-name constant classes and localisation-key classes with their public fields, and the payload enums and structs as enums and interfaces;
- substitute the frontend's own types where an engine type crosses the wire — `Unity.Entities.Entity` becomes the `Entity` type the scaffold's `cs2/utils` declarations export, and `Game.Input.ProxyBinding` becomes the widget-identifier type `cs2/bindings` exports.

A stale earlier output sharing the stem — a `.d.ts` beside the generated `.ts` — is never what an extensionless import resolves to, since the config's `resolve.extensions` lists `.tsx`, `.ts` and `.js` only, yet it survives on disk to mislead the next reader; have the generation overwrite one file and delete anything it supersedes.

## What this reference hands to others

`cs2-mod-project` owns the hook that invokes this build; it gets from here the wipe window and the declaration-order fact behind the hook's placement.

`frontend-and-injection` owns the loaded module's life on the page; it gets the names the loader depends on — `<id>.mjs`, `<id>.css` derived by extension swap, `coui://ui-mods/` as the public path — the images-directory lever against the shared host, and the reload mechanics: `.mjs` is Media, a reload is a teardown plus image-cache sweep, and only local mods are watched.

`binding-layer` gets the identity chain that makes `mod.json`'s `id` the natural binding group, and the type-generation route that keeps its two ends in step.

`mod-lifecycle-and-ordering` — a UI module has a lifecycle apart from the C# half's: it is an asset created from the `.mjs` extension, registered when the mod manager initialises, and added or removed mid-session when a playset entry toggles, so a mod whose C# failed to load can still have its module registered from the same folder.

`mod-compatibility` — three collision surfaces are built here: the shared `images/` namespace, the three-character CSS class hash, and the loader's shared completion counter, where one mod's missing stylesheet silences every mod.

`settings-and-input` and `localization` — both reach the frontend through this same bundle, so both inherit the identity rule and the vendored-declarations staleness above.

`debug-menu` and `cs2-modding-setup` — the flag and its port also live with them: the trunk's debug-menu reference owns the developer command line, and the setup skill the launch-options row that turns it on, so a correction to the flag, its switches or the port lands in all three files.

`diagnostics` — this topic's evidence lives in `UI.log` (the inspector port, every reload line, and the 404 with its failing URL), `Modding.log` (the `Registered UI Module` lines) and `SceneFlow.log` (the launch flags); all three truncate per session, so the run that shows the problem is the run to read.
