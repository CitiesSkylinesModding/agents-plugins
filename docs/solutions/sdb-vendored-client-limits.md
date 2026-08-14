---
date: 2026-07-29
area: plugins/unity-devtools/sdb
symptoms:
  - 'PlatformNotSupportedException on delegate BeginInvoke under modern .NET'
  - 'replies never dispatch when connecting through VirtualMachineManager'
  - 'an assembly loaded after the first read never appears in the domain assembly list'
  - 'a mirror accessor that looks like a field read turns out to be a round trip per call'
  - 'a session reports itself attached long after the debuggee exited'
  - 'a stack trace appears in the MCP stdio stream when a debuggee dies'
  - 'an attach hangs forever against a port some other program is listening on'
tags: [sdb, mono-debugger-soft, vendoring, net10, caching, liveness, timeouts]
updated: 2026-08-14
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

Which accessors cost the wire is not evident from their neighbours, and two of them read as free
while charging per call:

- `TypeMirror.GetTypeObject()` re-issues `Type_GetObject` every single time, where the surrounding
  `GetFields` / `GetMethods` / `GetInterfaces` memoize on the mirror. Every API taking a `System.Type`
  goes through it, so an unmemoized call inside a per-component loop pays a round trip per component.
- `EnumMirror.StringValue` walks the enum's static fields one `Type.GetValue` at a time, so naming
  the k-th member costs k round trips and a value matching no single member costs a full scan before
  falling back to the number -- which is what every flags combination does. The whole member table
  instead reads in ONE `GetValues` over the static fields, `const` literals included: they have no
  storage, and the agent answers them anyway.

One accessor reads the other way round, free where it looks expensive: **reading a mirror's own type
costs no round trip.** `ObjectMirror.Type` looks like it might, since its getter falls back to
`Object_GetInfo` when the type is unset -- but that branch is unreachable for any mirror the decode
path produced. An object value arrives carrying only its object id, so `VirtualMachine.GetObject`
issues `Object_GetInfo` EAGERLY while interning the mirror and constructs it with the type already
set. The cost is paid once per object, at first sight, whether or not anything ever reads the type.

So resolving a getter off a fresh mirror's type is a client-side lookup, and memoizing one across
mirrors buys nothing -- verified by wire counters, which showed byte-identical traffic with and
without such a memo.

That batched read is ALL-OR-NOTHING. `TypeMirror.GetValues` maps `ErrorCode.INVALID_FIELDID` to an
`ArgumentException` covering the whole call, so one field the agent will not hand over fails every
other field in the batch. A caller that reads a table this way catches around it, rather than letting
one unreadable member fail the value the caller actually asked for.

That surface also answers **whether the connection is still up**, which nothing public does.
`Connection.DisconnectedEvent` is a manual-reset event the receiver thread sets on EOF, on a VM crash
and on any receive failure, before it closes the transport, so `!DisconnectedEvent.WaitOne(0)` is a
liveness read costing no wire traffic and no lock. Polling the socket instead is wrong in a way that
looks right: the receiver reads that same socket, so a live packet makes it readable, the receiver
drains it, and the poller then finds nothing available and calls a healthy connection dead.

That receiver also reports a failed receive with `Console.WriteLine`, i.e. on **stdout**, which is
the MCP transport: an abruptly dying debuggee would write a stack trace into the JSON-RPC stream.
`patch-connection.ps1` redirects it to stderr. A vendor update reinstates it, and the patch fails
loudly on a missing anchor rather than silently.

Before the receiver exists at all, **the handshake read is fixed-length and untimed** (upstream marks
the gap with a FIXME), so a peer that accepts and says nothing blocks the caller forever. A poll for
readability before it bounds only total silence: one byte satisfies the poll, and a peer that writes
a prefix and stalls blocks in the same read. The derived port sits in the ephemeral range where an
unrelated program owning it is ordinary, so this is a live case, not a hypothetical. Both windows
therefore get their own bound, and the handshake's is enforced by closing the socket from outside --
a receive timeout cannot serve, since it stays on the socket for the receiver thread, which then
calls a healthy idle connection dead. Disarm that deadline the moment the handshake lands, or it
closes a connection that just succeeded.

That surface is also how you invalidate the client's OWN caches, and one of them goes stale silently.
`AppDomainMirror.GetAssemblies()` memoizes the domain's assembly list and drops it only on an
`AssemblyLoad` event — which this client never requests — so every read after the first returns the
first read's list, forever. `TypeCatalog.Refresh` calls the internal
`VirtualMachine.InvalidateAssemblyCaches()` before enumerating. Nothing at the call site hints that
it must.

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

A recursive init also costs gigabytes that no `deinit` reclaims: mono's own submodules keep their
object stores under `.git/modules` even once their working trees are gone. The reset deletes them.
