# AGENTS.md

## Plugin overview

`unity-devtools` drives a running **Unity Mono development build** from the outside over the **Mono Soft Debugger protocol (SDB)**: no code injection, no game modification.
It ships the `unity` MCP server (beacon discovery, live type reflection, C# expression evaluation on the main thread, breakpoints and stepping, ECS entity/component/buffer read-write) plus the `unity-driving` skill.
It is generic: any dev Mono build with the SDB agent live is drivable, and it is developed against one such build as the reference target.
Verified on Windows only, and the server-lifetime watchdogs are the sole Windows-bound code; users need the .NET 10 SDK, since the server ships as the `UnityDevtools.Mcp` NuGet dotnet tool launched via `dotnet dnx`.

## Tool surface

Bare names for generic Unity tools (`status`, `find_types`, `eval`, `debug_*`, `advance`, session lifecycle), an `ecs_*` prefix for ECS tools — the plugin will grow beyond ECS.
The tool schemas are the reference for behavior; they are in context whenever the server is connected.

Two semantics span the whole toolset and no single schema owns them:

- "Paused" means ANY VM suspension, so the frame tools (`debug_pause_state`, `debug_evaluate`, `debug_step`) work under a plain `suspend` hold — main thread — as well as under an event pause, which uses the event thread.
- Every INVOKE runs on the main thread, event-thread frames included, preserving the ECS thread-safety invariant. Frame slot reads and writes are thread-agnostic wire operations and do not.

## Session model

- ONE persistent session per server process (`UnitySession`). Tools attach lazily to the endpoint the PlayerConnection beacon advertises, and reattach once, resolving from the beacon again, when the connection drops.
- The game keeps running between calls: each operation opens its own counted suspend window. `suspend`/`resume` hold an extra one across calls for consistency windows spanning several reads and writes.
- Counted suspends are what make the debug pump and those per-operation windows commutative: eval and ecs tools keep working while stopped at a breakpoint.
- `detach` and server shutdown always resume and free the exclusive debugger slot; the "resume + detach even on failure" invariant lives in `SdbSession.Dispose`, with a closed socket auto-resuming the VM as the safety net.
- The server has NO configuration surface, environment or otherwise: `BeaconListener` receives what the game advertises, and `attach` is the one in-band override, for a debuggee no beacon describes.

## Project layout

The .NET projects plus the vendored SDB client, grouped by `agents-plugins.slnx` at the repo root (`dotnet build agents-plugins.slnx`; the repo has no other .NET code).

- `package.json`: private release-please version anchor; NOT a bun workspace package.
- `.claude-plugin/plugin.json` + `.mcp.json`, `.codex-plugin/plugin.json` + `.codex-plugin/mcp.json`: the two harness manifest sets, both launching `dotnet dnx UnityDevtools.Mcp --version <pin> --yes`. The command is `dotnet`, never the bare `dnx` shim — that is a `.cmd` script MCP hosts cannot spawn on Windows. The version pin is a standalone args element so release-please can update it (`$.mcpServers.unity.args[3]`, checked by `check:plugin-sync`).
- `sdb/` (`UnityDevtools.Sdb`): the SDB client library, and the surface the repo's other projects build on so that none of them touches vendored code. It ships only inside the `mcp/` tool and is never packed on its own, so its types are internal to this repo however `public` they are declared: changing one is a matter of updating the call sites in the same commit, not a break anyone downstream can feel. It compiles the vendored `Mono.Debugger.Soft` sources — read [`docs/solutions/sdb-vendored-client-limits.md`](../../docs/solutions/sdb-vendored-client-limits.md) before changing anything around them. Its own plumbing: `SdbSession`, `Invoker`, `Ecs`, `UnitySession`, `BeaconListener` + `PlayerConnectionBeacon`, `DebugController` + `DebugModel`, `TypeCatalog`.
- `sdb/Eval/`: the expression evaluator (Roslyn parse-only into an owned AST, then a client-side walker over `Invoker`; operators delegate to the C# runtime binder, so promotion and concat semantics are exactly the language's).
- `tests/` (`mise test`): offline parser/AST and operator-semantics suite, also in CI and the pre-commit.
- `tests-integration/`: the evaluator and debug toolset against a real net472 debuggee under Mono — traps in [`docs/solutions/mono-fixture-traps.md`](../../docs/solutions/mono-fixture-traps.md).
  A test touching the PlayerConnection beacon joins `BeaconGroupCollection`, whose docblock says why.
- `mcp/` (`UnityDevtools.Mcp`): the stdio MCP server on the official `ModelContextProtocol` C# SDK — generic-host builder, attribute-based tool classes taking the shared `UnitySession` via DI, `ToolGuard` wrapping bodies in `McpException` so messages reach the client verbatim. All logs go to stderr so they never corrupt the stdio stream.
- `vendor/mono-debugger-soft/`: the SDB client sources from Unity's mono fork, committed verbatim rather than submoduled. Its `VENDOR.md` states the pin, why the copy, and the rules around updating it (`mise vendor:unity:update`); never hand-edit a file there.

## The eval contract

The grammar is FROZEN: literals, member access, calls with explicit generic type args, indexers, `new` + object initializers, casts, operators, assignments, ternary/`?.`/`??`, `typeof`, string interpolation, `out var`. Lambdas, LINQ, loops and control flow are rejected at parse time; array-creation expressions and the `as` operator sit outside the grammar too, and `params` expansion fails at overload matching, which wants exact arity.

The semantic boundary is a contract, not a node list: common agent workflows evaluate exactly as C# would; edge semantics may diverge but must fail loudly with an actionable message, never succeed silently wrong.
Deliberate divergences stay documented — today: numeric-to-enum convenience, in-range integral narrowing, enum/numeric operator mixing, `entity(index)` version defaulting.
New evaluator effort goes to enforcing that contract through `tests-integration/`, not to growing the grammar. Anything needing debuggee-side execution belongs to the injected-helper roadmap tier.

## C# project settings

All C# projects live here, none is covered by a `Directory.Build.props`, all set `TreatWarningsAsErrors` (a plain build is therefore the typecheck and lint), and NONE enables `ImplicitUsings`: every file declares its own `using` directives, `System` included, `System.*` first, then the rest alphabetical, aliases last.

- `sdb/`: net10.0, `Nullable=disable` (so the vendored sources compile), no analyzers, `AllowUnsafeBlocks`, `NoWarn` on SYSLIB0001/SYSLIB0050/CS9258.
- `mcp/`: net10.0, `Nullable=enable`, `EnforceCodeStyleInBuild` + `AnalysisMode=Recommended`.
- `tests/`, `tests-integration/`: net10.0 xUnit, `Nullable=enable`, no analyzers.
- `tests-integration/fixture/`: net472 console debuggee, `LangVersion=latest`, `Nullable=enable`, no analyzers.
- `tests-integration/broken/`: net472 class library, `LangVersion=10.0`, `Nullable=disable`, no analyzers.
- `tests-integration/missing/`: net472 class library, `LangVersion=latest`, `Nullable=enable`, no analyzers.

## Distribution

The server is a NuGet **dotnet tool** (`PackAsTool`, framework-dependent, platform-agnostic) that both harness configs launch with `dotnet dnx ... --version <pin> --yes` — downloaded on first launch, cached after. `--version` placed after the package id is consumed by dnx, not forwarded to the tool.

`mise build:unity:pack` packs the nupkg into `mcp/dist/` (gitignored); `mise publish:unity:nuget` pushes it, MANUAL, no CI publish.
Release-day ordering: merging the release PR bumps the dnx pins in git, so publish the nupkg right after — installs and reconnects resolve the pinned version from NuGet and fail until it exists (`check:plugin-sync` verifies the pins offline, not their publication).

There is NO committed artifact and no local exe: the root `.mcp.json` (LOCAL DEV ONLY) runs the server from sources via `dotnet run --project`, so every `/mcp` reconnect rebuilds and serves the current code. dnx is deliberately not used for dev — it caches the extracted tool by version, so a rebuilt nupkg under an unchanged version would keep serving stale bits.

## Preferred agent behavior

- After changing `mcp/` or `sdb/`, the running server keeps serving the old build. Ask the user in plain text to hit Reconnect in `/mcp` (that rebuilds from sources), then end your turn — they cannot run `/mcp` while an AskUserQuestion prompt is pending.
- After changing MCP **config**, expect one orphaned server per reconnect, still holding the SDB slot and build locks: kill the old `dotnet run` wrapper by hand. Background in [`docs/solutions/unity-mcp-server-stranded-on-reconnect.md`](../../docs/solutions/unity-mcp-server-stranded-on-reconnect.md).
- ECS traps that cost hours: [`docs/solutions/unity-entities-over-sdb.md`](../../docs/solutions/unity-entities-over-sdb.md).
- Cost work counts INVOKES, not round trips: an invoke wakes the game's main thread and costs ~26x a bare wire command, so trading one for twenty is break-even. A memo pays against a whole CALL, not against a round trip, so justify one with the freeze time of the smallest call that exercises it. Measurements, the per-call figures, and the rule for memoizing a failure: [`docs/solutions/sdb-round-trips-are-not-equal-cost.md`](../../docs/solutions/sdb-round-trips-are-not-equal-cost.md).
- Before reading a value or a throw back from the debuggee, read [`docs/solutions/mono-debuggee-answers-over-sdb.md`](../../docs/solutions/mono-debuggee-answers-over-sdb.md): nulls, sparse arrays and exception messages all answer differently than .NET does, and the .NET reference source is evidence about .NET rather than about this fork.

## Boundaries

- Always resume + detach, even on failure.
- Invoke through `Invoker`, never a mirror directly: a direct invoke opts out of the NOT_SUSPENDED retry and of the in-game-throw unwrap that gives every tool the game's own exception message.
- `Invoker.Retrying` exists because NOT_SUSPENDED is a normal transient state right after attach, not a fault. Anything caching debuggee state caches only what is a property of the thing itself; a failure that describes the moment propagates, so the next call retries.
- Memos live in one of two tiers and die with their owner: **per-attach** (`Invoker`, `EcsCatalog`) or **per-operation** (`Ecs`, one suspend window). Anything that can change BETWEEN operations is re-established on every one rather than cached — and where re-establishing it costs no more than checking it, prefer that: there is then no rule about when the cache is still good to get wrong. Each tier states its own reasoning; keep it there.
- Keep it generic: no game-specific type names or behavior in the tool. Discovery goes by the PlayerConnection beacon, with `attach` as the agent's escape hatch for a debuggee it does not describe.
- An escape hatch stays reachable when the mechanism it bypasses fails. `attach` ships inside the server that discovery lives in, so `BeaconListener` records why it cannot listen and the server still serves; a listener that threw would take the recovery down with the thing it recovers from.
- Writes mutate live game state: verify a write tool on a scratch entity built through `eval` (`em.CreateEntity` + `em.AddBuffer<T>`, `em.DestroyEntity` when done), and assume a throwaway save otherwise.
