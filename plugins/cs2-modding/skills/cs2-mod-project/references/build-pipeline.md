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
Ordinary C# compilation to `net48`, with one addition: the Entities source generators from the Unity mod project's package cache are registered as analyzers.
Errors about generated job or system code come from those generators and belong to this stage, not to the post-processor.
They ship as source in that same cache, under `Unity.Entities/SourceGenerators/Source~/` in the Entities package, so what one does with a construct is readable rather than inferable.

`Mod.props` pins C# 9, and a `<LangVersion>` in a property group after the two imports raises it as far as the installed .NET SDK's compiler reaches.
What the raise costs is the runtime types `net48` never shipped — `IsExternalInit` for `init` and records, `System.Index` for list patterns, `CompilerFeatureRequiredAttribute` for user-defined compound assignment — each a compile error naming the type it wants, closed by the PolySharp package the parent skill names, or by declaring the type yourself.
A hand-declared one is bound by full name and by shape, not by the name alone: it goes in the type's own namespace and carries the members the compiler calls, which the next error names when they are missing.
`IsExternalInit` is an empty static class in `System.Runtime.CompilerServices`; `CompilerFeatureRequiredAttribute` sits beside it, derives from `Attribute` and takes a `string`, since one that does not derive is rejected as not an attribute class; `System.Index` carries a `(int value, bool fromEnd = false)` constructor, an implicit conversion from `int`, and `GetOffset(int)`.
`internal` is accessible enough for all three.
Get a namespace or a shape wrong and the original error persists unchanged, which is the trap the package exists to skip.
Where the answer is not a type at all, no package helps either: a list pattern that slices reaches for `RuntimeHelpers.GetSubArray`, a member missing from a class `net48` already ships, so nothing can add it and the pattern has to be written another way.
A feature the runtime itself has to support — `static abstract` interface members, `ref` fields — is the other kind, and no declared type closes it, so the fix there is to write the construct another way.
Burst is unaffected either way: it consumes the IL, which carries no language version.

**A file the generators emit a partial for takes a block namespace, and this has no version to wait for.**
Declaring an `IJobEntity` or an aspect is enough on its own, with nothing scheduling it; a system needs a `SystemAPI` call or a schedule site, so a system file with neither survives under a file-scoped namespace until someone adds the first one.
The generators walk the syntax ancestors of the declaration and test each for `SyntaxKind.NamespaceDeclaration`, and C# 10 gave a file-scoped declaration its own kind, `FileScopedNamespaceDeclaration`, whose syntax type is a sibling of `NamespaceDeclarationSyntax` rather than that type.
So the ancestor is right there and the test rejects it, the walk ends before it begins, and the partial is emitted into the global namespace.
It is then a different type from the one you declared, and the errors land inside generated code, naming members you never wrote: a system reports `no suitable method found to override` on `OnCreateForCompiler`, while a job reports whatever its generated plumbing reaches for first, which varies with what else in the project uses it.
The `using` directives survive, which is what makes the generated file look almost right.

An `.editorconfig` rule catches the namespace while the file still compiles, which is early enough for an editor to flag it as it is typed:

```ini
[*.cs]
csharp_style_namespace_declarations = block_scoped
dotnet_diagnostic.IDE0160.severity = error
```

Add `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`, off by default, to fail the build on it as well; an editor needs nothing beyond the rule itself.
Reach for `EnableNETAnalyzers` only if you want the CA rules too: it gates those and not this, and turning it on lights up a rule set the project has never seen.
Either way it is a guard rather than a diagnostic, so it does nothing for a build already broken — once the generator has failed, that build reports only the generator's own error.

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

`Mod.targets` documents three supported moves in its own header, and all three go in the csproj **after** the two imports — declared above them, a redefined target is overwritten by the import, and a target sharing `DeployWIP`'s own `AfterTargets="AfterBuild"` anchor runs before the wipe, its output deleted with the build still green:

- Redefine a target with the same name to replace it outright.
- Add a target with `BeforeTargets` or `AfterTargets` to insert work — `AfterTargets="DeployWIP"` is the documented place to copy extra files into the deploy folder, and where a UI build or any other extra artifact belongs.
- Add a target after `ModPostProcessorConfig` or `ModPublisherConfig` to rewrite the arguments those stages pass, starting from the defaults they built.

(VOLATILE: the target names above — `Mod.targets` in `%CSII_TOOLPATH%`, which the toolchain owns rather than the game.)
