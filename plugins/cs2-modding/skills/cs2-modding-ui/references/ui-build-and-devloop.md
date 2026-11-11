# The UI build and the dev loop

How a UI mod's frontend is built and iterated on.
Hooking this build into the C# build is `cs2-mod-project`'s — its skill body owns the scaffold invocation and the hook, and its build-pipeline reference the supported hook moves; this reference starts at the build the hook invokes.

## What `npm run build` produces, and where it lands

The scaffold's webpack config is the whole build; the MSBuild toolchain ships no UI stage at all.
Its output path is `${CSII_USERDATAPATH}\Mods\${MOD.id}` — the installed mod folder itself, with no intermediate `dist/`, so `npm run build` *is* the install.
The config throws before webpack starts when `CSII_USERDATAPATH` is unset, naming the missing variable.
**On Linux that path's Windows separators create one folder whose name holds the backslashes, beside the user data folder rather than in `Mods`, so the game never sees the UI half**; `path.join(CSII_USERDATAPATH, "Mods", MOD.id)` fixes it, and `npm run update` puts the original back.

Four kinds of file come out, and every name is load-bearing:

- `<id>.mjs`, the entry bundle — the entry key is `mod.json`'s `id`, and the `.mjs` extension is webpack's own default once `experiments.outputModule` is on;
- `<id>.css`, only when the mod imported any style — the CSS extract plugin's default filename is the same entry key;
- `images/[name][ext][query]`, every imported `.png`, `.jpg`/`.jpeg`, `.gif` and `.svg`, emitted as `asset/resource`;
- `<id>.mjs.LICENSE.txt`, Terser's extracted third-party licence comments — the mod's own manifest banner is not in it, that is prepended to the `.mjs`.

`npm run build` also type-checks: the template compiles `.tsx?` through ts-loader with no `transpileOnly`, so a type error fails the build rather than shipping.
The template's tsconfig sets `typeRoots: ["./types"]`, which turns off the automatic `node_modules/@types` globals; the `cs2/*` declarations arrive as ordinary project files, and React's types still resolve because imports reach them through module resolution rather than `typeRoots`.

## The externals contract

The scaffold's `externals` maps the game's shared modules, which the game defines as properties on `window`, onto their import specifiers with `externalsType: "window"`.
An import that goes through the map costs the bundle nothing and uses the game's copy; everything else is bundled.

The scaffold's own `types/bindings.d.ts` opens on an import from `chart.js`, a package the template never installs; the build survives because the template's tsconfig sets `skipLibCheck: true`, and turning that off surfaces an unresolved import in a declaration file you did not write.

## The manifest, and the identity it shares with the C# half

`mod.json` is four keys — `id`, `author`, `version`, `dependencies` — and the webpack config is what turns them into a manifest: it interpolates them into a comment banner with the capitalised keys `Id`, `Author`, `Version`, `Dependencies` and delivers it through Terser's `extractComments.banner` option, which is where to look when a built module carries no manifest.

`cs2-mod-project` states the identity rule — `mod.json`'s `id` equals the csproj's `TargetName`; what holds it up is three derivations of one name that nothing reconciles:

- the C# half deploys to `$(LocalModsPath)\$(TargetName)`, with `LocalModsPath` read from the user-scope `CSII_LOCALMODSPATH`;
- the UI build writes to `${CSII_USERDATAPATH}\Mods\${MOD.id}`, composed from the process environment;
- the module's URL is `coui://ui-mods/<id>.mjs`, from the file name alone.

**The two halves' deploy paths come from two different environment variables, so repointing `CSII_LOCALMODSPATH` moves the C# half and strands the UI half.**

The scaffold is how the mismatch usually happens: it prompts for a name, creates a directory of exactly that name and writes the same string into `mod.json` as `id`, so scaffolding a folder called `UI` inside the C# project yields a mod whose id is `UI`.
It offers no way to get a differently named folder and id in one step; rename the directory or edit `mod.json` afterwards, and make the result the assembly name.
Its validator also rejects any character outside `[a-zA-Z0-9_-]`.

The same `id` is the natural binding group: import `mod.json` into the TypeScript and pass `mod.id` wherever a group name is needed, and the two halves cannot drift, because the group string derives from the same manifest field as the deploy folder.
Nothing in the game requires this.

## Refreshing the declarations after a game update

The globally installed scaffold is not a copy that ages: the toolchain installs it by running `npm link` inside `<install>/Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod`, so the npm-global package name is a junction into the game's own files, and a patch that ships new `types/*.d.ts` changes what `npx create-csii-ui-mod` hands out immediately, with no npm step.
That holds only while the global entry links to the install path, so verify that it does.

The project's own copy does age, and `npm run update` is the one refresh: it copies the template's `tsconfig.json`, `types/`, `tools/` and `webpack.config.js` over the project's, rewrites the pinned dependency versions and the `scripts` block into the project's `package.json`, and runs `npm install`.
The copy overwrites file by file and deletes nothing except one hard-coded legacy declaration file, so any other file a later template dropped survives in the project.
**`npm run update` overwrites `webpack.config.js` and `tsconfig.json`, taking any edit with them.**
A mod that diverges from the stock config — a renamed images directory, a source-map plugin, a loader option — re-applies the divergence after every update or keeps the config stock; there is no merge.

**A vendored `types/` folder that is never refreshed fails silently.**
The mod compiles against whatever it vendored, so a binding payload whose shape changed in a game update reads as the old shape with no error anywhere.
The alternatives are the author's: run `npm run update` as part of the game-update ritual, or take the declarations out of the repository entirely — point the tsconfig `include` at a copy shipped as a versioned dependency, so the refresh becomes a version bump with a reviewable diff.

**`clean` deletes the C# half too.**
The subcommand removes `<CSII_USERDATAPATH>/Mods/<id>` recursively, and that is the shared deploy folder — the `.dll`, the `.pdb` and the Burst libraries go with the bundle.

## The stylesheet contract

A built module's contract with the page is two named exports, `default` and `hasCSS`.
The second is built by a text substitution: the scaffold's CSS-presence tool taps webpack's `processAssets`, checks whether any emitted asset ends `.css`, and splices `const hasCSS = <bool>; export { hasCSS, ` over the first literal `export {` in each `.mjs`.
**The splice works only because it runs before Terser** — the minified `export{` no longer matches the pattern, so a plugin reordered after minification injects nothing, silently.

Every mod's stylesheet lands in the same document as the game's and every other mod's.
The scaffold's `localIdentName` is `[local]_[hash:base64:3]` — a three-character hash space shared by every mod; prefixing it with the mod's own tag removes the collision and makes the classes addressable from outside, at the price of an edit the next `npm run update` reverts.
And css-loader resolves every `url()` as a module by default, so a stylesheet referencing one of the game's shipped images by its install path (`url(Media/…)`) fails the build; a `url.filter` that skips those prefixes leaves them for the game to resolve at runtime.

## The inspector port

The `--uiDeveloperMode` launch flag, which `cs2-modding-setup`'s launch options set, turns on the inspector and the file-watching reload together.

The endpoint at `http://localhost:9444` is a Chrome-DevTools-protocol server exposing one page target, the game's whole UI at `assetdb://gameui/index.html`.
A browser's DevTools attach to it, and so does any other CDP client — the sibling `coherent-gameface` plugin, application-agnostic, drives the same port with its `game_*` tools.

## The reload loop

The dev loop is a game-side file watcher, not a dev server: with the flag on, a rebuild that rewrites files in a watched mod folder reloads the UI in place — `npm run dev` (whose invocation `cs2-mod-project` owns) is the same build re-running on source changes, with the game doing the reloading.

What the inspector shows is minified: the template builds in production mode and sets no `devtool`, so the `.mjs` an author debugs has no mapping back to the source.
Setting `devtool: false` explicitly and adding webpack's `EvalSourceMapDevToolPlugin` builds mappings into the emitted code; whether the inspector honours an eval source map is untested.

## The seam with the C# build

`cs2-mod-project` owns the hook — the `Exec` in the csproj and `AfterTargets="DeployWIP"` as its documented place.
`DeployWIP` is a `RemoveDir` of `$(DeployDir)` followed by the copy from the C# output, and the scaffold's webpack writes *into* `$(DeployDir)`, so the wipe is what a UI build has to land after.
**A UI build hooked earlier than the wipe is deleted by it, and nothing reports the loss**: the build stays green, and a running game's only evidence is the 404 it logs for the missing bundle — with the dev loop on, the watcher also reloads the view over the emptied folder.

**MSBuild runs targets sharing an `AfterTargets` anchor in declaration order, so a UI target hooked `AfterTargets="AfterBuild"` survives only while it is declared below the toolchain imports.**
The official project template places the two imports immediately after the first property group — its own comment says they must come after it — which is why the earlier anchor appears to work; move an import below the target and the same target runs before `DeployWIP`, its output is wiped, and the build still reports success.
Two shapes do not depend on declaration order: hook the build `AfterTargets="DeployWIP"`, or build to a directory of your own and hook only a copy after `DeployWIP` — the shape the toolchain's own header comment demonstrates.

**`UI.log` names the exact failing `coui://` URL when an artefact is missing** — a `ResourceHandler: HTTP/1.1 404 Not Found` line with the URL on the indented line under it — which makes it the first read for every missing-or-misnamed-output case.

## Generating the frontend's types from the C#

The binding names, enum values and payload shapes exist on both sides of the wire, and hand-writing them twice is the default failure: each half compiles alone while drifting from the other.
The mechanism that removes the drift is generation from the C#, workable with any typings generator or a reflection pass of your own; what matters is what the generation must do:

- run from the C# build, so the output is as current as the assembly;
- emit one TypeScript module into the UI source tree, where the frontend imports it like any other file;
- export the binding-name constant classes and localisation-key classes with their public fields, and the payload enums and structs as enums and interfaces;
- substitute the frontend's own types where an engine type crosses the wire — `Unity.Entities.Entity` becomes the `Entity` type the scaffold's `cs2/utils` declarations export, and `Game.Input.ProxyBinding` becomes the widget-identifier type `cs2/bindings` exports.

A stale earlier output sharing the stem — a `.d.ts` beside the generated `.ts` — is never what an extensionless import resolves to, since the config's `resolve.extensions` lists `.tsx`, `.ts` and `.js` only, yet it survives on disk to mislead the next reader; have the generation overwrite one file and delete anything it supersedes.
