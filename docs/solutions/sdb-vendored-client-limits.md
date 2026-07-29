---
date: 2026-07-29
area: plugins/unity-devtools/sdb
symptoms:
  - 'PlatformNotSupportedException on delegate BeginInvoke under modern .NET'
  - 'replies never dispatch when connecting through VirtualMachineManager'
tags: [sdb, mono-debugger-soft, vendoring, net10]
---

# Running Mono's vendored SDB client on .NET 10

## Problem

`Mono.Debugger.Soft` was written for the Mono runtime. Compiled into a net10.0 library, parts of its
public connection surface are dead on arrival.

## What didn't work

`VirtualMachineManager.Begin*` — modern .NET removed delegate `BeginInvoke`, so every async connect
path throws. `Connection.cs`'s reply dispatch fails for the same reason.

## Root cause

The vendored sources predate .NET Core's removal of remoting-era APIs.

## Fix

- Connect **synchronously** through the internal `TcpConnection` (`SdbSession`), never the `Begin*`
  helpers.
- Patch `Connection.cs`'s reply dispatch at build time: the `PatchVendoredConnection` target in
  `sdb.csproj` patches into `obj/`, leaving the vendored tree pristine.
- `LocaleShim`/`RemotingShims` supply the missing runtime pieces.
- Leave `ENABLE_CECIL` undefined: live mirrors and invokes cover everything, and defining it would drag
  in Mono.Cecil.
- `Nullable=disable` on `sdb/` exists precisely so these sources compile; consumers stay nullable-clean.

Compiling the vendored sources **into** this assembly also opens their `internal` surface, which the
evaluator depends on: default `StructMirror`s are built entirely client-side through the internal
`StructMirror(vm, type, fields)` ctor.

## Prevention

Only `sdb/` touches vendored code; it is the public surface every other project consumes.

The submodule is a sparse, shallow, blob-filtered clone of `mcs/class/Mono.Debugger.Soft/` (~75 files,
MIT), pinned to `unity-6000.6-mbe`. The branch is provenance only — that tree hash is identical across
`unity-2022.3-mbe`, `unity-6000.6-mbe` and `unity-main`; Unity only evolves the **agent** side, and the
wire protocol is version-negotiated at attach, so one client serves every Mono-era Unity agent.

`mise vendor:unity:reset` restores the lean checkout in place (`git sparse-checkout set --cone` with
`core.longpaths`, preserving the submodule gitlink a re-clone would break). Run it after a submodule
init, or to recover from a plain `git submodule update` that materialized the FULL tree — that state
breaks `mise check`, because oxlint then scans the vendored JS/TS.
