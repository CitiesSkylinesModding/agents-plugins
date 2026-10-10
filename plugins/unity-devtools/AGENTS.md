# AGENTS.md

## Plugin overview

`unity-devtools` drives a running **Unity Mono development build** from the outside over the **Mono Soft Debugger protocol (SDB)**: no code injection, no game modification.
It ships the `unity` MCP server (beacon discovery, live type reflection, C# expression evaluation on the main thread, breakpoints and stepping, ECS entity/component/buffer read-write, screen capture) plus the `unity-driving` skill.
It is generic: any dev Mono build with the SDB agent live is drivable, and it is developed against one such build as the reference target.
Verified on Windows and on Linux, there attaching from the host to a Windows game running under Proton; the server-lifetime watchdogs are the only code with a branch per platform (the same tie, told apart by what each OS can observe), and users need the .NET 10 SDK, since the server ships as the `UnityDevtools.Mcp` NuGet dotnet tool launched via `dotnet dnx`.

## Tool surface

Bare names for generic Unity tools (`status`, `find_types`, `eval`, `screenshot`, `debug_*`, `advance`, session lifecycle), an `ecs_*` prefix for ECS tools — the plugin will grow beyond ECS.
The tool schemas are the reference for behavior; they are in context whenever the server is connected.

Two semantics span the whole toolset and no single schema owns them:

- "Paused" means ANY VM suspension, so the frame tools (`debug_pause_state`, `debug_evaluate`, `debug_step`) work under a plain `suspend` hold — main thread — as well as under an event pause, which uses the event thread.
- Every INVOKE runs on the main thread, event-thread frames included, preserving the ECS thread-safety invariant. Frame slot reads and writes are thread-agnostic wire operations and do not.

## Session model

- ONE persistent session per server process (`UnitySession`). Tools attach lazily to the endpoint the PlayerConnection beacon advertises. A drop found as an operation opens its suspend window is retried once against a freshly resolved endpoint; one found mid-operation is NOT retried, since the operation may already have applied — it surfaces, and the next call reattaches.
- The game keeps running between calls: each operation opens its own counted suspend window. `suspend`/`resume` hold an extra one across calls for consistency windows spanning several reads and writes.
- Counted suspends are what make the debug pump and those per-operation windows commutative: eval and ecs tools keep working while stopped at a breakpoint.
- The host dispatches tool calls CONCURRENTLY, and `Run` serializes one operation rather than one call, so a tool built from several wraps them in `UnitySession.Exclusive`, whose docblock states which gaps bite. A hold such a sequence takes for itself goes through `SuspendHold(reported: false)`: it freezes the game like any other, and `status` keeps reporting the caller's own windows rather than one they never opened and would resume.
- `detach` and server shutdown always resume and free the exclusive debugger slot; the "resume + detach even on failure" invariant lives in `SdbSession.Dispose`, with a closed socket auto-resuming the VM as the safety net.
- The server has NO configuration surface, environment or otherwise: `BeaconListener` receives what the game advertises, and `attach` is the one in-band override, for a debuggee no beacon describes.

## Project layout

The .NET projects plus the vendored SDB client, grouped by `agents-plugins.slnx` at the repo root (`dotnet build agents-plugins.slnx`; the repo has no other .NET code).

- `package.json`: private release-please version anchor; NOT a bun workspace package.
- `.claude-plugin/plugin.json` + `.mcp.json`, `.codex-plugin/plugin.json` + `.codex-plugin/mcp.json`: the two harness manifest sets, both launching `dotnet dnx UnityDevtools.Mcp --version <pin> --yes`. The command is `dotnet`, never the bare `dnx` shim — that is a `.cmd` script MCP hosts cannot spawn on Windows. The version pin is a standalone args element so release-please can update it (`$.mcpServers.unity.args[3]`, checked by `check:plugin-sync`).
- `sdb/` (`UnityDevtools.Sdb`): the SDB client library, and the surface the repo's other projects build on so that none of them touches vendored code. It ships only inside the `mcp/` tool and is never packed on its own, so its types are internal to this repo however `public` they are declared: changing one is a matter of updating the call sites in the same commit, not a break anyone downstream can feel. It compiles the vendored `Mono.Debugger.Soft` sources — read [`docs/solutions/sdb-vendored-client-limits.md`](../../docs/solutions/sdb-vendored-client-limits.md) before changing anything around them. Its own plumbing: `SdbSession`, `Invoker`, `Ecs`, `UnitySession`, `BeaconListener` + `PlayerConnectionBeacon`, `DebugController` + `DebugModel`, `TypeCatalog`, `MissDiagnosis` + `MissSuggestions`, `Screenshot`.
- `sdb/Eval/`: the expression evaluator (Roslyn parse-only into an owned AST, then a client-side walker over `Invoker`; operators delegate to the C# runtime binder, so promotion and concat semantics are exactly the language's).
- `tests/` (`mise test`): offline parser/AST and operator-semantics suite, also in CI and the pre-commit.
- `tests-integration/`: the evaluator and debug toolset against a real net472 debuggee under Mono — traps in [`docs/solutions/mono-fixture-traps.md`](../../docs/solutions/mono-fixture-traps.md).
  A test touching the PlayerConnection beacon joins `BeaconGroupCollection`, whose docblock says why.
- `mcp/` (`UnityDevtools.Mcp`): the stdio MCP server on the official `ModelContextProtocol` C# SDK — generic-host builder, attribute-based tool classes taking the shared `UnitySession` via DI, `ToolGuard` wrapping bodies in `McpException` so messages reach the client verbatim. All logs go to stderr so they never corrupt the stdio stream.
- `vendor/mono-debugger-soft/`: the SDB client sources from Unity's mono fork, committed verbatim rather than submoduled. Its `VENDOR.md` states the pin, why the copy, and the rules around updating it (`mise vendor:unity:update`); never hand-edit a file there.

## The eval contract

The grammar is FROZEN: literals, member access, calls with explicit generic type args, indexers, `new` + object initializers, casts, operators, assignments, ternary/`?.`/`??`, `typeof`, string interpolation, `out var`. Lambdas, LINQ, loops and control flow are rejected at parse time; array-creation expressions and the `as` operator sit outside the grammar too, and `params` expansion fails at overload matching, which wants exact arity.

The semantic boundary is a contract, not a node list: common agent workflows evaluate exactly as C# would; edge semantics may diverge but must fail loudly with an actionable message, never succeed silently wrong.
Deliberate divergences stay documented — today:

- Numeric-to-enum convenience.
- In-range integral narrowing, admitted for any integral value where C# admits it for an int constant only, so a call can bind a narrower overload than C# does, a derived type's included.
- Enum/numeric operator mixing.
- `entity(index)` version defaulting.
- Type-argument inference takes the argument's runtime type rather than its declared one and reads only a parameter typed as the type parameter itself (not `T[]` or `List<T>`). It refuses loudly two arguments fixing a type parameter to different types, and a null beside one fixing it to a type taking null or to a struct a box may hide, unless a nearer tier's overload binds or a sibling takes every argument as is (a null at a slot no less specific than the generic instance's); C# fixes the type parameter to the non-null argument's type or to a type every argument converts to, and binds the generic method wherever that makes it applicable.
- Overload binding reads a boxed argument's runtime type rather than its declared `object`, so a box holding an int binds `M(int)` where C# binds `M(object)`.
- A mutating call through a struct boxed in a slot typed object or as an interface, or a member write through one typed as an interface, stores a new box, which other references to the old box do not see: the wire carries no box identity. A member write through one typed object is refused.
- A write to or through a `readonly` field, or a frame write through a read-only slot (an `in` parameter, or `this` in a `readonly` member or `readonly struct`) or to that `this` itself, lands in that storage, where C# refuses the assignment and makes a mutating call change a defensive copy. Code the JIT compiled before such a write may keep a `static readonly` field's old value, though a read returns the new one.
- A frame write refuses a number or bool into an interface slot, and a client number or bool, or a debuggee one of another type, into a nullable slot, where C# boxes or converts and wraps it.
- A frame write to a local the JIT folded from a constant never reaches the resumed code, though the evaluation reads it back.
- A frame local written by name reads back that write for the rest of the pause, missing a later write that reached it through another frame's `ref` parameter or struct `this`; under a held suspend, which marks no pause, a local written before an invoke reads back its old value in `debug_pause_state` and from the next evaluation on.
- A static field of a type whose static constructor has not run is refused until the caller runs it, where C# runs it on first access; an evaluation on the thread running the constructor reads the statics it already assigned.
- On an agent below protocol 2.44, a mutating call on a struct's `this` loses its write, since that agent cannot store `this` back.

New evaluator effort goes to enforcing that contract through `tests-integration/`, not to growing the grammar. Anything needing debuggee-side execution belongs to the injected-helper roadmap tier.

A result comes back FORMATTED for a reader — a string quoted, a bool as `True` — and locals live on the interpreter rather than the state, so only `_` survives a call. Code building on `eval` therefore parses that rendering back and re-embeds whatever the next program needs, quoting a path through Roslyn's own `SymbolDisplay.FormatLiteral` so the literal it emits is one the parser accepts.

## C# project settings

All C# projects live here, none is covered by a `Directory.Build.props`, all set `TreatWarningsAsErrors` (a plain build is therefore the typecheck and lint), and NONE enables `ImplicitUsings`: every file declares its own `using` directives, `System` included, `System.*` first, then the rest alphabetical, aliases last.

- `sdb/`: net10.0, `Nullable=disable` (so the vendored sources compile), no analyzers, `AllowUnsafeBlocks`, `NoWarn` on SYSLIB0001/SYSLIB0050/CS9258.
- `mcp/`: net10.0, `Nullable=enable`, `EnforceCodeStyleInBuild` + `AnalysisMode=Recommended`.
- `tests/`, `tests-integration/`: net10.0 xUnit, `Nullable=enable`, no analyzers.
- `tests-integration/fixture/`: net472 console debuggee, `LangVersion=latest`, `Nullable=enable`, no analyzers, `AllowUnsafeBlocks`.
- `tests-integration/broken/`: net472 class library, `LangVersion=10.0`, `Nullable=disable`, no analyzers.
- `tests-integration/missing/`: net472 class library, `LangVersion=latest`, `Nullable=enable`, no analyzers.

## Distribution

The server is a NuGet **dotnet tool** (`PackAsTool`, framework-dependent, platform-agnostic) that both harness configs launch with `dotnet dnx ... --version <pin> --yes` — downloaded on first launch, cached after. `--version` placed after the package id is consumed by dnx, not forwarded to the tool.

`mise build:unity:pack` packs the nupkg into `mcp/dist/` (gitignored); the release workflow packs and pushes it when release-please cuts the mcp release.
Release-day ordering: merging the release PR bumps the dnx pins in git before that push lands, so wait for the publish job before reconnecting or announcing — installs and reconnects resolve the pinned version from NuGet and fail until it exists (`check:plugin-sync` verifies the pins offline, not their publication).

There is NO committed artifact and no local exe: the root `.mcp.json` (LOCAL DEV ONLY) runs the server from sources via `dotnet run --project`, so every `/mcp` reconnect rebuilds and serves the current code. dnx is deliberately not used for dev — it caches the extracted tool by version, so a rebuilt nupkg under an unchanged version would keep serving stale bits.

## Preferred agent behavior

- After changing `mcp/` or `sdb/`, the running server keeps serving the old build. Ask the user in plain text to hit Reconnect in `/mcp` (that rebuilds from sources), then end your turn — they cannot run `/mcp` while an AskUserQuestion prompt is pending.
- After changing MCP **config**, expect one orphaned server per reconnect, still holding the SDB slot and build locks: kill the old `dotnet run` wrapper by hand. Background in [`docs/solutions/unity-mcp-server-stranded-on-reconnect.md`](../../docs/solutions/unity-mcp-server-stranded-on-reconnect.md).
- ECS traps that cost hours: [`docs/solutions/unity-entities-over-sdb.md`](../../docs/solutions/unity-entities-over-sdb.md).
- Attach churn is CONSUMABLE against a game under Wine/Proton, which leaks descriptors per debugger connection: spend enough and its agent stops accepting anyone until the game restarts, with no warning and no count to watch, with no client-side cure. Measurements and how to read the state from outside: [`docs/solutions/a-debugger-agent-that-stopped-accepting.md`](../../docs/solutions/a-debugger-agent-that-stopped-accepting.md). Probes and test runs that attach repeatedly spend the user's session, so bound them.
- Cost work counts INVOKES, not round trips: an invoke wakes the game's main thread and costs ~26x a bare wire command, so trading one for twenty is break-even. A memo pays against a whole CALL, not against a round trip, so justify one with the freeze time of the smallest call that exercises it. Measurements, the per-call figures, and the rule for memoizing a failure: [`docs/solutions/sdb-round-trips-are-not-equal-cost.md`](../../docs/solutions/sdb-round-trips-are-not-equal-cost.md).
- Before reading a value, a type's value-type flag or a throw back from the debuggee, passing a pointer argument, or writing a nullable field, read [`docs/solutions/mono-debuggee-answers-over-sdb.md`](../../docs/solutions/mono-debuggee-answers-over-sdb.md): nulls, sparse arrays, `TypeMirror.IsValueType`, pointers and exception messages all answer differently than .NET does, and the .NET reference source is evidence about .NET rather than about this fork.

## Boundaries

- Always resume + detach, even on failure.
- Every wait on the debuggee that we own carries a bound, and the bound lives where the wait is: the invoke wait in `Invoker`, the reply wait in the vendored `SendReceive`, the session gate in `UnitySession.Holding` — [`docs/solutions/a-wedged-session-that-swallowed-its-own-escape-hatch.md`](../../docs/solutions/a-wedged-session-that-swallowed-its-own-escape-hatch.md). The vendored client's ASYNC reply path is the exception and is NOT bounded, so do not read the ladder as total; that doc names what is still open and why severing remains the only cure for it. An escape hatch never waits on the mechanism it recovers from: `Detach` breaks the gate rather than queueing on it, and rations that break, since severing a live connection costs the debuggee a descriptor forever.
- Invoke through `Invoker`, never a mirror directly: a direct invoke opts out of the park wait and of the in-game-throw unwrap that gives every tool the game's own exception message.
- `Invoker.Retrying` exists because a suspend that returns has FLAGGED the main thread, not parked it: a thread flagged inside native code runs that code on and parks where an invoke can run only when it re-enters managed code, so NOT_SUSPENDED is a wait rather than a fault — [`docs/solutions/a-suspend-that-landed-on-an-unparked-thread.md`](../../docs/solutions/a-suspend-that-landed-on-an-unparked-thread.md) has the measured latencies and what shortening the wait for one tool costs. Anything caching debuggee state caches only what is a property of the thing itself; a failure that describes the moment propagates, so the next call retries.
- Memos live in one of two tiers and die with their owner: **per-attach** (`Invoker`, `EcsCatalog`) or **per-operation** (`Ecs`, one suspend window). Anything that can change BETWEEN operations is re-established on every one rather than cached — and where re-establishing it costs no more than checking it, prefer that: there is then no rule about when the cache is still good to get wrong. Each tier states its own reasoning; keep it there.
- Keep it generic: no game-specific type names or behavior in the tool. Discovery goes by the PlayerConnection beacon, with `attach` as the agent's escape hatch for a debuggee it does not describe.
- An escape hatch stays reachable when the mechanism it bypasses fails. `attach` ships inside the server that discovery lives in, so `BeaconListener` records why it cannot listen and the server still serves; a listener that threw would take the recovery down with the thing it recovers from.
- Writes mutate live game state: verify a write tool on a scratch entity built through `eval` (`em.CreateEntity` + `em.AddBuffer<T>`, `em.DestroyEntity` when done), and assume a throwaway save otherwise.
