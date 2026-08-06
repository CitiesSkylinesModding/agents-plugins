---
date: 2026-08-06
area: docs/research (the cs2-modding pipeline, any claim about the runtime rather than the game)
symptoms:
  - 'a claim ships behind UNVERIFIED: because "the experiment needs the running game"'
  - 'a marker is overturned as "standard .NET, never a question about this game" and the replacement prose is also wrong'
  - 'prose says a missing member "throws at that call site" and a try/catch around the call never fires'
tags: [research, verification, runtime, mono, unity, unverified-marker, experiment]
---

# A runtime question was answered from first principles, twice, and both answers were wrong

## Problem

Whether a mod compiled against one version of a shipped library runs correctly against a
different loaded copy went through three states, and only the last was right.
The research stage wrote `UNVERIFIED:` and asked for a two-mod install to settle it.
The maintainer overturned that as standard .NET needing no experiment, and the shipped prose
then stated a mechanism from CLR reasoning alone.

Both the marker and its replacement were wrong, in opposite directions: the replacement
promised a fault "at that call site" and "never silent wrong behaviour", and neither holds.

## What didn't work

**The `UNVERIFIED:` marker.** It named an experiment nobody would run — install two mods that
ship different versions of one library — so it bought a reader doubt and bought the next
maintainer a sweep entry that could never be closed.

**Reasoning about the runtime.** "It is ordinary .NET" is true and does not answer the
question. Two agents and a maintainer all reached the same confident, wrong granularity,
because the correct answer is not what the language spec makes salient.

**Waiting for the game.** The question was scoped to the running game from the start, and the
game was never running. It never needed the game: it needed the runtime the game embeds.

## Root cause

Two facts that decompile reading and language reasoning both miss.

- **Resolution is method-granular.** A memberref is resolved while the _containing method_ is
  JIT-compiled, not when control reaches the call. So a `try`/`catch` written around the call
  never runs — the whole method throws before its first statement. The guard has to be an
  isolated `[MethodImpl(MethodImplOptions.NoInlining)]` method, or a reflection lookup where a
  member that went away is a `null` you can test. Without `NoInlining` it may still work,
  because the inliner declines an unresolvable callee, but that is discretion rather than a
  guarantee.
- **Three constructs are copied into the calling assembly at compile time**: a `const`, an enum
  member's value, and an optional parameter's default. A change to any of them keeps running
  with the stale value, with no exception and no log line. A `static readonly` on the same type
  correctly reads the new value, which is what proves the mechanism is compile-time baking
  rather than staleness.

## Fix

Run it locally, in the runtime the game embeds, in about five minutes:

1. Compile a small library with the member, and an app that calls it.
2. Recompile the library without the member; swap the DLL beside the app.
3. Run under Unity's own Mono rather than the system runtime —
   `<Unity install>/Editor/Data/MonoBleedingEdge/bin/mono.exe`.

Put a write as the method's _first_ statement and the guarded call as its second: if the write
never prints, resolution happened at JIT time and the granularity question is answered. Print a
`const`, an enum value and a defaulted parameter beside a `static readonly` control to see which
ones went stale silently.

## Prevention

A question about the _runtime_ is not a question about the game, and does not wait on the game.
The plugin's own rule already says to run a cheap experiment instead of writing a marker
(`plugins/cs2-modding/AGENTS.md`, "Where the experiment is cheap, run it instead of writing the
marker") — what this cost was not knowing the experiment was cheap, because the marker had
framed it as a two-mod install.

When a marker names an impractical experiment, ask what the claim is actually about before
dropping it. Here the answer moved the experiment from an unreproducible player setup to two
`csc` invocations and a file copy.
