---
date: 2026-07-30
area: plugins/unity-devtools/sdb
symptoms:
  - 'a null check on an invoke result never fires'
  - 'string.Join over a mirrored array returns nothing'
  - "'Retrieve the LoaderExceptions property for more information'"
tags: [sdb, mono, unity, invoke, null-handling, reflection]
---

# What a Mono debuggee hands back is not what .NET would

## Problem

Code that reads a value or an exception back from the debuggee gets answers shaped by Unity's Mono
fork and by the SDB value model, not by the .NET a reader is picturing. Each divergence below cost
real time, and two of them were argued as bugs from the .NET reference source before being measured.

## Root cause

**A debuggee null is a MIRRORED null.** An invoke returning null answers
`PrimitiveValue { Value: null }`, not a C# null, so `is {}` and `is not null` both pass and the null
travels on into the next invoke, where the game throws on it. The predicate the evaluator uses is
`EvalInterpreter.IsNull` (`value is null or PrimitiveValue { Value: null }`), and it is private, so
every new call site rediscovers this.

**`string.Join(string, object[])` does not short-circuit on a leading null.** The .NET Framework
reference implementation opens with `if (values.Length == 0 || values[0] == null) return
String.Empty;`, so a sparse array whose FIRST slot is null renders as the empty string. Mono's does
not: it renders every null slot as an empty entry wherever it sits, first included. Joining a sparse
array debuggee-side is therefore safe here, and the gaps arrive as empty entries to drop
client-side.

**`ReflectionTypeLoadException.Message` already names the missing assemblies.** Where .NET Framework
gives only "Unable to load one or more of the requested types. Retrieve the LoaderExceptions
property for more information.", this fork appends the loader exceptions' own messages, so the
message reads "... Could not load file or assembly 'Some.Dependency, Version=1.0.0.0, ...'". Reading
the `LoaderExceptions` array over the wire buys nothing. It repeats one line per failed type, so a
badly broken assembly carries a long message.

## Prevention

Measure against a live debuggee before trusting reference-source behaviour: the .NET reference
implementation is evidence about .NET, not about this fork, and reasoning from it produced two
confident bug reports about code that was correct.

The Editor's MonoBleedingEdge is close enough to reproduce these, and a green integration suite is
still not the last word — the divergences above were confirmed against the reference game itself.
