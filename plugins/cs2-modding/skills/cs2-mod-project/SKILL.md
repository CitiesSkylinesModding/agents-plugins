---
name: cs2-mod-project
description: 'The official Cities: Skylines II modding toolchain. Use when the user wants to start a CS2 mod project, when a mod build or its post-processing fails, when a mod they just built does not appear in the game, or when they are publishing or updating one.'
---

# Building and shipping a Cities: Skylines II mod

Verified against game version 1.6.0f1.
Paths and commands throughout are Windows.

The official toolchain creates mod projects, builds them, installs them locally and publishes them.
Drive it rather than reproducing it: a hand-written project drifts from the shared build the first time the toolchain moves, and the build does work no csproj can carry on its own.

## Installing the toolchain

In the game: Options → Modding.
The same page repairs a broken installation, updates an outdated one, and rewrites the environment variables the build reads.

It pulls in everything a mod build needs, so nothing here is installed by hand: a Unity editor, a Unity project carrying the Entities and Burst packages, the .NET SDK, Node.js, both project templates, and integration for Visual Studio, VS Code or Rider.
Read the versions it pinned from the `CSII_*` environment variables rather than from a number written down.

## Creating a project

### The C# half

```powershell
dotnet new csiimod -n MyMod
```

The IDE's "Cities Skylines II mod" template and the toolchain's own project generator both end at that same template, so any of the three is the same project.
`dotnet new csiimod --help` lists its options: `IncludeSetting` adds a settings class registered in the options screen, `IncludeKeyBindings` adds rebindable actions to it, and `ShortDescription` and `LongDescription` seed the publish configuration.

What lands in the folder:

- `MyMod.csproj`, importing `Mod.props` and `Mod.targets` from `%CSII_TOOLPATH%` — those two files own the whole build.
- `Mod.cs`, a class implementing `IMod`, which is what makes the assembly a mod: the game scans each shipped assembly for a type implementing that interface and ignores the ones that carry none.
- `Setting.cs`, when settings were asked for.
- `Properties/PublishConfiguration.xml` and `Properties/Thumbnail.png`, the publishing metadata.
- `Properties/PublishProfiles/`, three profiles that are the three publishing modes.

### The UI half

```powershell
npx create-csii-ui-mod
```

Run it inside the C# project's folder; it prompts for a project name and an author, takes `--name=` and `--author=` to skip the prompts, and creates a subfolder with a webpack build, a `mod.json` and the game's TypeScript type declarations.
Its `update` subcommand refreshes those declarations after a game update, and `clean` deletes the local install so the game stops seeing the mod.

The `id` in `mod.json` must equal the C# project's assembly name.
Both halves deploy by that name into one folder — the C# build to `%CSII_LOCALMODSPATH%\<assembly name>` and the UI build to `%CSII_USERDATAPATH%\Mods\<mod.json id>`, which is the same directory — so a mismatch installs two half-mods instead of one whole one.

`npm run build` builds once and `npm run dev` watches.
To make one `dotnet build` do both, run the UI build from an `Exec` target hooked `AfterTargets="DeployWIP"`: the deploy stage empties that shared folder before refilling it from the C# output, so a UI bundle written any earlier is deleted rather than installed.

Everything past the project layout — the binding between C# and the frontend, and the frontend itself — is a separate discipline; the sibling `coherent-gameface` plugin drives the UI engine underneath it, for anyone who has it installed.

## Project settings that are not obvious

**Every game and Unity reference carries `<Private>false</Private>`.**
The template sets it on each reference it declares; keep it on the ones you add.
Without it MSBuild copies that assembly into the build output, the deploy stage copies the output into the local mods folder, and the mod ships the game's own assemblies inside itself.
The game notices, skips the offending file and logs `Assembly "X" is in-game assembly and it should NOT be shipped with mod "Y"`.
Packages the mod genuinely depends on are the opposite case: they have to land next to the mod assembly to resolve at runtime, so leave those copying.

**Harmony, the patching library, is pinned by community agreement.**

```xml
<PackageReference Include="Lib.Harmony" Version="2.2.2" />
```

The game ships no patching library and the toolchain references none, so every mod that patches ships its own copy — and the game then collapses them into one.
Before loading anything it groups every assembly shipped by every mod by name, treats same-named copies as duplicates, and loads the group's winner in place of the copy a mod shipped: at boot a local build over a subscribed one, then the highest version, then the asset id.
A copy already in the process outranks all three, so a local build deployed and enabled mid-session loses to the subscribed copy that won at boot, and only a restart puts your build in front.
So a mod pinning a different version does not get that version to itself — it can become the copy every other mod patches through, and nothing warns anyone, because the duplicate warning the game raises covers mod assemblies rather than the libraries they reference.
That shared fate is why the version is agreed rather than chosen per project.

Patching is a last resort in this game rather than the default technique, so a project that never needs this reference is the better outcome.

**The framework and language version come from the toolchain.**
`Mod.props` fixes `net48` and C# 9 to match the game's runtime, and a project does not override them.
C# 10 syntax an agent writes by habit — file-scoped namespaces, global usings — fails to compile here; `Mod.props` is where to check when a modern construct is rejected.

## Testing locally

Building installs the mod into `%CSII_LOCALMODSPATH%\<assembly name>`, so there is no separate install step.
The game reads that folder at startup and lists what it finds there as local mods.

To disable one without deleting it, rename its folder so the name starts with `.` or `~`.
The game's asset scan skips every file and folder whose name begins with either character, so the mod stops existing as far as the game is concerned, and renaming it back brings it right back.
Restart the game after either change.

Breakpoints in that mod need the game patched for debugging, which the `cs2-modding-setup` skill of this plugin does: [debug-patching.md](../cs2-modding-setup/references/debug-patching.md).

## When a mod's code first runs

Mods load late, and that bounds what a mod can do.
By the time any `IMod.OnLoad` runs, the ECS world exists, the game's own systems have been created, and the update, prefab and save/load systems are already in place; prefabs load immediately after.
Nothing runs earlier, so a technique that depends on injecting itself before the world is built is unavailable here, and ordering is arranged from inside `OnLoad` instead.

## Publishing

[publishing.md](references/publishing.md) carries the three modes, the metadata file, and two traps — the one that publishes a second copy of an existing mod, and the one that turns a description into a code block.

## When a build fails

[build-pipeline.md](references/build-pipeline.md) names each stage of the build, what it does, and what its failure looks like, plus the environment variables every stage reads.

## The wiki, and its age

The wiki's [modding toolchain page](https://cs2.paradoxwikis.com/Modding_Toolchain) is the process source of record, and its own banner verifies it against game version 1.1.12f1 — several releases behind this skill's baseline, with dependency versions that no longer match an installation.
Everything above was read from an installed toolchain instead.
