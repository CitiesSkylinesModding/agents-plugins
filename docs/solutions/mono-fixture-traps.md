---
date: 2026-07-29
area: plugins/unity-devtools/tests-integration
symptoms:
  - 'static fixture roots read as null'
  - 'invokes hang against the parked main thread'
  - 'every debug test reports AbsentInformation'
  - 'the whole suite hangs after one debug test'
tags: [integration-tests, mono, sdb, xunit]
---

# Traps in the Mono integration debuggee

## Problem

The evaluator's integration suite launches a net472 fixture under a Mono runtime with the SDB agent
(`suspend=y`, free port picked test-side) and evaluates raw C# through the production scope chain. Four
distinct ways the debuggee looks broken when the fixture is at fault.

## Root cause and fix

- **Mirror reads never trigger class constructors.** A static root read before anything touched its
  class comes back empty. `Main` warms the static roots and prints `READY` before the harness evaluates.
- **Invokes need a managed safepoint.** A thread blocked forever in a native wait never reaches one, so
  the parked main thread loops through short _managed_ sleeps.
- **Symbols need both halves.** The fixture must build a portable PDB (`DebugType=portable`) _and_ mono
  must be launched with `--debug`; miss either and everything reports `AbsentInformation`.
- **Armed state leaks across tests.** Every debug test releases what it armed (`fx.ReleaseDebugger()` in
  a `finally`), or the frozen debuggee hangs every later test.

The debug-toolset tests ride the same loop: `Main` calls `Ticker.Tick(n)` each iteration (armed
breakpoints hit within milliseconds) and periodically throws-and-catches a `FormatException` for
exception-break coverage.

## Prevention

ONE debuggee per suite run (xUnit collection fixture): tests own what they mutate by creating per-test
instances inside the evaluated expressions; shared static roots stay read-only.

Mono resolves via `UNITY_DEVTOOLS_MONO` (test-infra config, deliberately not `UNITY_MCP_*`) → `mono` on
PATH → well-known Windows Unity Editor locations. Tests SKIP (`Xunit.SkippableFact`) rather than fail
when none resolves. CI installs `mono-devel`; `mono-runtime` alone lacks the net4x facade assemblies.

Upstream Mono's agent is not byte-for-byte Unity's fork, so a green suite is not the last word: a live
Unity game stays the fidelity gate for fork-specific behavior.
