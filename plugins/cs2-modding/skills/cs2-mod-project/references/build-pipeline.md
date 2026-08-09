# What a mod build does

Verified against game version 1.6.0f1.
Paths and commands are Windows.

`Mod.props` and `Mod.targets`, which every mod csproj imports from `%CSII_TOOLPATH%`, own everything below.
They are shared by every mod project on the machine, so a project changes the build through the hooks at the bottom of this file rather than by editing them.

## The environment variables

The toolchain writes these as **user** environment variables when it installs, and the game's Options → Modding page rewrites them on request.
The C# side reads the user scope directly, so a rewritten value reaches the next build however long the shell or IDE has been open.
The UI build is the exception: it reads the inherited process environment, so a rewritten path only reaches it from a shell opened afterwards.

| Variable | Points at | Why it matters |
| --- | --- | --- |
| `CSII_INSTALLATIONPATH` | the game folder | the root the managed and mscorlib paths are derived from |
| `CSII_MANAGEDPATH` | the game's managed assemblies | every `Game`, `Colossal.*` and `Unity.*` reference resolves from here |
| `CSII_MSCORLIBPATH` | the game's `mscorlib.dll` | referenced explicitly so the mod compiles against the game's runtime, not the SDK's |
| `CSII_USERDATAPATH` | the user data folder | holds `Player.log`, the settings, and the local mods folder |
| `CSII_LOCALMODSPATH` | `<user data>\Mods` | where every build installs the mod |
| `CSII_TOOLPATH` | the per-user toolchain folder | holds `Mod.props`, `Mod.targets` and the Unity mod project |
| `CSII_UNITYMODPROJECTPATH` | that Unity project | source of the Entities source generators and of the packages Burst compiles against |
| `CSII_ENTITIESVERSION` | the Entities package version | selects the source-generator folder inside the package cache |
| `CSII_UNITYVERSION` | the editor version installed for compiling mods | locates the editor whose IL post-processor the post-processing stage runs |
| `CSII_MODPOSTPROCESSORPATH` | the post-processor executable | the post-processing stage |
| `CSII_MODPUBLISHERPATH` | the publisher executable | what the publish path runs |
| `CSII_PDXCACHEPATH` | the mod platform's cache | the publisher reads it to sign in |
| `CSII_PDXMODSPATH` | downloaded platform mods | the publisher's mod root |
| `CSII_ASSEMBLYSEARCHPATH` | extra assembly search paths, empty by default | the supported hook for referencing assemblies outside the game folder |

## The stages

Each is an MSBuild target, so a failure attributes to exactly one.

**1. Path checks.**
Six paths are verified to exist before anything is compiled, in this order: the managed assemblies, mscorlib, the user data folder, the Unity mod project, the post-processor, and the Entities source-generator folder.
A failure names the variable behind the path: `User environment variable 'CSII_MANAGEDPATH' has incorrect path(s) '...' set. Please update the Modding toolchain in-game to reset its value or modify its value to a suitable path`.
The mscorlib check is the one to read twice, because it names `CSII_MSCORLIB` while the variable it read is `CSII_MSCORLIBPATH` — setting the name the message gives fixes nothing.
The publisher path is checked too, but only when publishing.

Everything else in the table goes unchecked, and `CSII_LOCALMODSPATH` is the one that matters: it is not verified, so an empty or wrong value deploys the mod to a path nobody looks at while the build still reports success.
That is the shape of "it built, and the game does not list it".

**2. Output cleanup.**
The output folder is deleted outright, so nothing from a previous build can survive into this one.

**3. Compilation.**
Ordinary C# compilation to `net48` at C# 9, with one addition: the Entities source generators from the Unity mod project's package cache are registered as analyzers.
Errors about generated job or system code come from those generators and belong to this stage, not to the post-processor.

**4. Post-processing.**
The post-processor runs over the compiled assembly for three platforms, and does two things in order.
It first runs Unity's IL post-processors — the same ones a Unity build would run — in a helper process holding the Entities, Collections and Burst packages, rewriting the assembly.
It then runs the Burst compiler once per platform over whatever the assembly marks for Burst compilation, producing `<assembly>_win_x86_64.dll` and its macOS and Linux counterparts beside the assembly.
(Jobs are reached through their schedule sites, so a `[BurstCompile]` job nothing schedules is compiled to nothing and the build still passes; the pass's own `containing N methods` line is what says otherwise.)
A mod that marks nothing still gets those files, and its own code stays managed.
A failure here reads `Failed to compile Burst dll for <platform>` or reports an error from the post-processor, and neither is a compilation error, so re-reading the C# is wasted effort.
The game loads the Windows library beside a mod assembly when it is there, and the other two ship against a port that has not happened.

**5. Deployment.**
The deploy folder `%CSII_LOCALMODSPATH%\<assembly name>` is removed and the whole build output copied into it.
The removal is what makes anything written into that folder earlier in the build disappear.

Publishing runs its own path checks and then the publisher, over the folder this stage filled; [publishing.md](publishing.md) covers the modes and the metadata they read.

## Hooking into the build

`Mod.targets` documents three supported moves in its own header, and all three go in the csproj **after** the two imports:

- Redefine a target with the same name to replace it outright.
- Add a target with `BeforeTargets` or `AfterTargets` to insert work — `AfterTargets="DeployWIP"` is the documented place to copy extra files into the deploy folder, and where a UI build or any other extra artifact belongs.
- Add a target after `ModPostProcessorConfig` or `ModPublisherConfig` to rewrite the arguments those stages pass, starting from the defaults they built.

(VOLATILE: the target names above — `Mod.targets` in `%CSII_TOOLPATH%`, which the toolchain owns rather than the game.)
