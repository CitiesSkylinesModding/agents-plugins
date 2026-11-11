---
name: cs2-mod-project
description: 'The official Cities: Skylines II modding toolchain. Use when the user wants to start a CS2 mod project, when the toolchain needs setting up on Linux, when a mod build or its post-processing fails, when a mod they just built does not appear in the game, or when they are publishing or updating one.'
---

# Building and shipping a Cities: Skylines II mod

The official toolchain creates mod projects, builds them, installs them locally and publishes them.
Drive it rather than reproducing it: the shared build does work no csproj carries on its own, and a hand-written project drifts from it.
On Linux, where the game runs under Proton, the toolchain is set up by hand and every project needs csproj edits: [linux-toolchain.md](references/linux-toolchain.md) carries them, and without them a template project fails on an empty target framework. [symbol: none]
**The toolchain inverts several standard .NET idioms, and the familiar csproj shape builds cleanly, then fails inside the game: read this skill's settings and references before writing one.**
A trap you meet here that this skill misses goes through the `cs2-modding` skill's trap reports.

## Installing the toolchain

On Windows, in the game: Options → Modding, which also repairs and updates an installation and rewrites the environment variables the build reads.
It installs everything a mod build needs; read the versions it pinned from the `CSII_*` environment variables.

## Creating a project

`dotnet new csiimod -n MyMod` creates the C# half; `dotnet new csiimod --help` lists its options, settings and key bindings among them.
The project imports `Mod.props` and `Mod.targets` from `%CSII_TOOLPATH%`, which own the whole build.
Its `PublishConfiguration.xml` names a `Thumbnail.png` the template does not create: [publishing.md](references/publishing.md) says what publishes in its place.

### The UI half

`npx create-csii-ui-mod --name=<name> --author=<author>`, run inside the C# project's folder, creates a webpack project with a `mod.json` and the game's type declarations; `npm run build` builds once and `npm run dev` watches.

- Its `update` subcommand overwrites `webpack.config.js` and `tsconfig.json`, local edits included, and `clean` deletes the C# half's deployed files with the bundle. [symbol: none]
- On Linux the scaffolded `webpack.config.js` needs the deploy-path edit [linux-toolchain.md](references/linux-toolchain.md) carries, re-applied after every `update`, or the game never sees the UI half. [symbol: none]
- The `id` in `mod.json` must equal the C# assembly name: both halves deploy into one folder by that name, and a mismatch installs two half-mods. [symbol: none]
- To build both halves with one `dotnet build`, run the UI build from an `Exec` target with `AfterTargets="DeployWIP"`: deploying empties the shared folder, deleting a bundle written earlier. [symbol: none]

## Project settings that are not obvious

- Every game and Unity reference carries `<Private>false</Private>`, as the template's do: a copied game assembly is skipped by the game with a warning in `Modding.log`. [symbol: Colossal.IO.AssetDatabase.ExecutableAsset]
- Pin Harmony at `<PackageReference Include="Lib.Harmony" Version="2.2.2" />`, the version the community agreed: the game loads one copy of a library for every mod, so another version can become the copy every mod patches through. [symbol: Colossal.IO.AssetDatabase.ExecutableAsset]
- `Mod.props` fixes `net48`, the game's runtime, which stays as it is, and pins C# 9: set `<LangVersion>` after the two imports, or the import overwrites it and the project silently stays on 9. [symbol: none]
- A file declaring a system, an `IJobEntity` or an aspect takes a block namespace: with a file-scoped one the Entities source generators emit into the global namespace, and the build fails in generated code. [symbol: none]
- To see what a source generator does with a construct, read its source under `Unity.Entities/SourceGenerators/Source~/` in the package `CSII_UNITYMODPROJECTPATH` locates, rather than emitting its output into the project. [symbol: none]

## Testing locally

Building installs the mod into `%CSII_LOCALMODSPATH%/<assembly name>`, which the game reads at startup: stop, build and relaunch through the `cs2-modding` skill's game cycle.
A mod deployed there yet missing in game, or doing nothing, is a loader question the `cs2-modding` skill's traps answer.
To disable a mod without deleting it, rename its folder to start with `.` or `~`, which the game's asset scan skips. [symbol: Colossal.IO.AssetDatabase.FileSystemDataSource]
Breakpoints need the game patched for debugging: [debug-patching.md](../cs2-modding-setup/references/debug-patching.md).

## Publishing

[publishing.md](references/publishing.md) carries the publishing modes, the metadata file, and its traps, such as the one that publishes a second copy of an existing mod, and the one that turns a description into a code block.

## When a build fails

[build-pipeline.md](references/build-pipeline.md) names each stage of the build, what it does, what its failure looks like, and the environment variables every stage reads.
The wiki's modding toolchain page is older than the toolchain an install carries: read the installed one.
