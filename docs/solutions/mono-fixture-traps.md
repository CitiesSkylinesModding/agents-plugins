---
date: 2026-07-29
updated: 2026-10-20
area: plugins/unity-devtools/tests-integration
symptoms:
  - 'static fixture roots read as null, or refused because a static constructor has not run'
  - 'invokes hang against the parked main thread'
  - 'every debug test reports AbsentInformation'
  - 'the whole suite hangs after one debug test'
  - 'the attach dies with "sent no Mono debugger greeting"'
  - 'an object built for a collected-mirror test survives GC.Collect'
tags: [integration-tests, mono, sdb, xunit]
---

# Traps in the Mono integration debuggee

## Problem

The evaluator's integration suite launches a net472 fixture under a Mono runtime with the SDB agent
(`suspend=y`, free port picked test-side) and evaluates raw C# through the production scope chain.
Distinct ways the debuggee looks broken when the fixture is at fault.

## Root cause and fix

- **Mirror reads never trigger class constructors.** A static root read before anything touched its
  class is refused when the class has a static constructor, and comes back empty when it has none.
  `Main` warms the static roots and prints `READY` before the harness evaluates.
- **Invokes need a managed safepoint.** A thread blocked forever in a native wait never reaches one, so
  the parked main thread loops through short _managed_ sleeps.
- **Symbols need both halves.** The fixture must build a portable PDB (`DebugType=portable`) _and_ mono
  must be launched with `--debug`; miss either and everything reports `AbsentInformation`.
- **Armed state leaks across tests.** Every debug test releases what it armed (`fx.ReleaseDebugger()` in
  a `finally`), or the frozen debuggee hangs every later test.
- **Picking a free port reserves nothing.** Only the agent's own bind does, and xUnit runs collections
  in parallel: two suites picking at the same moment were handed the same number, so the losing agent
  never bound while the winner's -- already holding its ONE client -- accepted the second connection and
  never greeted it. `MonoDebuggee.PickFreePort` hands each caller a distinct port, taken from outside
  the range the OS itself allocates from.
- **A fixture type's constructor takes no optional parameter.** xUnit builds a class or collection
  fixture by satisfying every parameter, and a defaulted one it cannot supply fails the whole class at
  run time with `had one or more unresolved constructor arguments`. Sharing a type whose constructor
  carries a test-only default therefore costs a wrapper, which is usually dearer than constructing it
  per test.
- **An object the debugger touched in the current suspend window survives `GC.Collect`.** A test
  needing a collected mirror builds the object in one eval and collects it in the next, binding it
  from `_`: any other bind is a round trip that touches it again.

The debug-toolset tests ride the same loop: `Main` calls `Ticker.Tick(n)` each iteration (armed
breakpoints hit within milliseconds) and periodically throws-and-catches a `FormatException` for
exception-break coverage.

## Prevention

ONE debuggee per suite run (xUnit collection fixture): tests own what they mutate by creating per-test
instances inside the evaluated expressions; shared static roots stay read-only.

Mono resolves via `UNITY_DEVTOOLS_MONO` (test infrastructure only; the server itself reads no
environment) → `mono` on PATH → well-known Windows Unity Editor locations. Tests SKIP
(`Xunit.SkippableFact`) rather than fail
when none resolves. CI installs `mono-devel`; `mono-runtime` alone lacks the net4x facade assemblies.

Upstream Mono's agent is not byte-for-byte Unity's fork, so a green suite is not the last word: a live
Unity game stays the fidelity gate for fork-specific behavior.
