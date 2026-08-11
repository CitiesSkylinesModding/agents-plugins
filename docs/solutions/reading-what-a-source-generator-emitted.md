---
date: 2026-08-11
area: plugins/cs2-modding (a mod build running the Entities source generators)
symptoms:
  - "error CS0579: Duplicate 'global::System.Runtime.Versioning.TargetFrameworkAttribute' attribute"
  - 'error SGJE0008: You have defined 2 Execute() method(s)'
  - "error CS0234: The type or namespace name 'CSharp' does not exist in the namespace 'Microsoft.CodeAnalysis'"
  - "Generator 'SystemGenerator' failed to generate source ... 'No Ancestor T found.'"
tags: [cs2-modding, entities, source-generators, msbuild, build-pipeline]
---

# Reading what a source generator emitted

## Problem

A build fails inside generated code and the obvious move — emit the generated files and read them —
breaks the next build instead.

## What didn't work

**`EmitCompilerGeneratedFiles=true` pointed inside the project.** The emitted `.g.cs` land under the
project directory, where the SDK's default compile glob sweeps them into the *next* build. Every
type is then declared twice: duplicate assembly attributes, and `SGJE0008` for an `IJobEntity` whose
generated `Execute` now exists in two copies. Delete the output directory between runs, and never
point it at a path the glob reaches — `obj/` is excluded, the project root is not.

**A build tool kept inside the mod project.** `tools/**/*.cs` and that tool's own `obj/` are compiled
into the mod, which surfaces as unrelated `CS0234` and duplicate attributes, and made `SystemGenerator`
throw `No Ancestor T found.` on a file it should never have seen. Exclude it explicitly.

## Fix

Read the generators' own source instead. They ship as C# under
`Unity.Entities/SourceGenerators/Source~/` in the Entities package that `CSII_UNITYMODPROJECTPATH`
locates, so what a generator does with a construct is readable without emitting anything.

Where the emitted file is genuinely needed, send it outside the project directory and delete it
afterwards.

## Prevention

A rewriting layer under these generators cannot be made transparent, so do not reach for one: they
copy user code into their own partials and stamp `#line` directives from the syntax tree's path,
ignoring any mapping underneath them. Diagnostics then double up and a debugger steps into the
rewritten copy rather than the file on disk.
