---
date: 2026-08-26
area: docs/research (the cs2-modding pipeline, against any decompile) and any claim decided by a grep hit
symptoms:
  - 'two passes read the same tree and reach opposite conclusions about what a call does'
  - 'a method body found by grep does not match the behaviour observed at runtime'
  - 'a correction is derived from an overload the call site cannot bind to'
tags: [research, decompile, grep, overload-resolution, verification, dead-code]
---

# A grep hit that resolved to dead code

## Problem

`EntityManager.Debug.GetComponentBoxed` boxes an unmanaged component.
A review pass grepped `ConstructComponentFromBuffer`, landed on a generated `switch` of plain pointer dereferences, and concluded the component path does no pinning — so a shipped trap about the call failing on some components was deleted as unfounded.
A later pass in the same session, from the same tree, found the opposite.

## What didn't work

Grepping the identifier and reading the body that came back.
Two overloads carry that name, and the grep returned the wrong one — with a body that answers the question plausibly and in the wrong direction.

A verifier then confirmed the first reading. It was handed the pattern the claim was derived from rather than the question, so it inherited the same blind spot and returned a verdict on it.

## Root cause

`TypeManager.ConstructComponentFromBuffer` has two overloads: `(TypeIndex, void*)` at `TypeManager.cs:1662`, whose body is `GCHandle.Alloc(obj, GCHandleType.Pinned)` plus a `MemCpy`, and `(void*, int)` at `:2488`, which fronts the codegen registry's generated `switch`.

The call site passes `(TypeIndex, byte*)` (`DebuggerDataAccess.cs:180`), which **cannot** bind to the second: `TypeIndex` converts only to and from `int` (`TypeIndex.cs:212`, `:217`) and never to a pointer, and `byte*` has no implicit conversion to `int`.

The `(void*, int)` overload has **zero call sites in the build**. Every one of the ten call sites of that name passes a `TypeIndex` first. The switch the grep found is unreachable code, and nothing in the file says so.

## Fix

Resolve the overload from the call site's argument types before reading any body, and check the candidate is called at all — a member with no call sites cannot be the live path, whatever its body says.

## Prevention

Where a grep hit decides a claim, two checks before the body is read: **does this member's signature accept what the call site passes**, and **does anything call it**.

A decompile is full of generated fallbacks and retired paths that survive as compilable dead code, so a plausible body is weak evidence that it is the body that runs.

Brief a verifier on the question, never on the pattern the claim came from — one handed the pattern re-walks the same wrong hit and returns a verdict on it. Related: [a search taken for a census](empty-grep-read-as-proof-of-absence.md) for the absence case, and [a read that stopped where the code agreed](decompile-read-stopped-at-the-confirming-line.md) for the read-depth case; this is the third shape, where the symbol itself was wrong.
