---
date: 2026-08-05
area: docs/research (the cs2-modding discovery pipeline, against any stripped Unity player build)
symptoms:
  - 'a Unity package assembly reports AssemblyVersion("0.0.0.0") and nothing else states its version'
  - 'the toolchain declares a package version that the shipped assembly does not match'
  - 'a member is missing from the decompile and the pass concludes the build predates it'
  - 'a version bound derived from one marker turns out to admit two answers'
tags: [research, decompile, unity, versioning, stripping, false-absence, verification]
updated: 2026-08-06
---

# Dating a shipped Unity package when the assembly carries no version

## Problem

Entities, Collections, Burst and Mathematics all report `AssemblyVersion("0.0.0.0")`, so a shipped
game states nowhere which of them it was built against. Read the assembly's own version first: not
every Unity package zeroes it, and where it is real it ends the question outright. The toolchain's
declared version answers a different question — what a mod compiles against — and the two can
disagree.

## What settles it

Unity mirrors package sources with a tag per release at
`https://github.com/needle-mirror/com.unity.<package>`. Clone a tag and compare it against the
shipped assembly. Bounds come from members whose presence, shape or absence-within-a-present-body
changed at a known release.

Start on the machine rather than on the network. The toolchain's Unity project resolves the declared
version into `Library/PackageCache/` (`SOURCES.md` entry 15), and that copy carries the package's
whole `CHANGELOG.md` — every release below the declared one, the candidates included. Reading it
picks the markers; cloning a tag is then how you learn the shape a marker took, on the releases the
changelog leaves you undecided between.

For this game the chain ran: `EntityExists` is present, added in 1.3.2; `EntityManager.CopyEntitiesFrom` builds a
query against both worlds and calls no `EntityRemapUtility.GetTargets`, the shape 1.3.5 replaced
1.3.2's with; the exception message `TypeManager.BuildComponentType` gained in 1.3.9 is absent from
the shipped binary; and `GetDescendantIndex`, which 1.4 removed, is present in it. That brackets the
shipped `Unity.Entities.dll` to 1.3.5-1.3.8 against a declared 1.3.10. The two survivors differ only
in the `IJobEntity` generator's handling of `WithPresent`, which this game uses nowhere, so nothing
it ships can separate them.

## The four traps, in the order they cost time

**Bound on presence, not on absence.** A member can be missing because the release predates it,
because a later release removed it, or because it was compiled out — and the binary does not say
which. Presence and a changed shape each admit one reading. An absence inside a body that is itself
present rules out only the compiled-out case and still admits two, which is what the next trap is
about; a bare absence admits all three.

**A marker is not monotonic.** Check it across every candidate release rather than assuming it stays
once added. The 1.3.9 guard above is gone again by 1.4, so its absence alone admitted two answers,
and the pass that assumed otherwise reached a confident wrong bound.

**`[Conditional]` members are the reverse trap.** `CheckComponentType` is compiled out of this build
entirely, so reading its absence as a version signal dates nothing. Check the attribute before
using a member as a marker.

**Prefer a string literal, and grep the `.dll` directly.** It skips the decompile, so it also rules
out reading a stale checkout — which is the one failure that makes every other step's answer wrong
without looking wrong. Search UTF-16LE, not ASCII: .NET user strings are UTF-16, so a plain `grep`
reports every literal absent while method names still hit, which makes the recipe look like it
works. Here it would have inverted the answer. 1.3.9 moved two messages in opposite directions: it
gained `Cannot build component types after the type manager has finished initializing`, genuinely
absent here in both encodings, and it removed `ComponentType … cannot be initialized more than
once`, which IS in the binary and only the UTF-16 read finds. Lead with that second one: a present
string needs no argument about why it might be missing.

## What the comparison also settles

The same clone answers whether the assembly is stock. Diff the decompiled type-name set against the
mirror's: for `Unity.Entities` five names are absent from Unity's source, four of them build
artifacts, and the fifth is `Colossal.CORuntimeApplication` — a Colossal-authored type
compiled inside the assembly, calling the `internal` `RuntimeApplication.InvokePostFrameUpdate`,
which is only reachable from within it.

That scan finds added types. A modified body inside an existing type would not appear in it, so
"additive only" is well supported rather than proven.
