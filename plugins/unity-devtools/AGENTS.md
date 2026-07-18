# AGENTS.md

## Plugin overview

`unity-devtools` is a generic plugin for driving a running **Unity Mono development build** from the outside, over the **Mono Soft Debugger protocol (SDB)**: no code injection, no game modification.
Cities: Skylines II is the reference/test target (a dev Mono build with the SDB agent live), mirroring how `coherent-gameface` is generic Gameface with CS2 as reference.
It ships the `unity` MCP server (process discovery, live type reflection, main-thread method invokes, ECS entity/component/buffer read-write) plus skills, registered in both marketplace files with dual harness manifests.
Windows-only for now (netstat-based discovery); users need the .NET 10 runtime.

## Tool surface and session model

The server exposes bare names for generic Unity tools and an `ecs_*` prefix for ECS tools (the plugin will grow beyond ECS):

- `status`: process/SDB-port discovery (no attach) + current session state.
- `detach`, `suspend`, `resume`: session lifecycle.
- `find_types`: live type resolution, optionally with members.
- `invoke`: static type methods (with out-param support) or managed ECS system methods, discriminated by `target`; text arguments are coerced against the resolved signature (same-arity overloads tried until one accepts), so tokens carry no type syntax.
- `ecs_query` (with the `label` annotation capability), `ecs_get_component`, `ecs_set_component`, `ecs_get_buffer`, `ecs_buffer_edit` (add / remove_at behind an `op` discriminator).

Session model (implemented by `UnitySession` in `sdb/`):

- ONE persistent session per server process; every tool that needs the VM attaches lazily (endpoint discovered via `SdbDiscovery` unless `UNITY_MCP_PORT` pins it) and reattaches once, against a fresh discovery, when the connection drops.
- The game keeps running between calls: each operation opens its own counted suspend window (suspend, act, resume).
- The `suspend`/`resume` tools hold an extra counted suspension across calls, freezing the game entirely, for consistency windows spanning multiple reads/writes.
- `detach` (and server shutdown, via container disposal) resumes everything and frees the single debugger slot; the invariant "always resume + detach, even on failure" lives in `SdbSession.Dispose`, with the closed-socket auto-resume as the safety net (verified).
- Env config (all optional; empty strings from harness passthrough count as unset): `UNITY_MCP_HOST`, `UNITY_MCP_PORT`, `UNITY_MCP_PROCESS` (process-name prefix; unset = auto-discover by SDB-port signature).

## Verified capabilities (live against CS2, 2026-07)

Everything below was proven end-to-end by the retired PoC CLI (see git history for `poc/`) whose logic now lives in `sdb/`:

- Attach to the game's SDB port, inspect, resume, detach cleanly; the game keeps running. Reattach across sessions works.
- Resolve types by name live, list their members via mirrors (no Mono.Cecil).
- Query ECS entities generically: `World.All` → `EntityManager` (boxed struct invokes) → `ComponentType.ReadWrite(Type)` → `CreateEntityQuery(ComponentType[])` → `CalculateEntityCount` / `ToEntityArray(AllocatorHandle)` → `NativeArray.ToArray()` → managed `Entity[]` mirror.
- Read AND write a component on one entity via `EntityManager.Get/SetComponentData<T>` instantiated live with `MethodMirror.MakeGenericMethod` (protocol 2.24+; CS2 answers 2.58).
  Writes persist in the running simulation.
  No debuggee-side reflection fallback was needed.
- A compound live scenario was verified E2E on CS2 (2026-07-17): moved a citizen between households by rewriting `HouseholdMember` + both `HouseholdCitizen` buffers, with addresses resolved via `BuildingUtils.GetAddress` and names via `NameSystem.GetRenderedLabelName`.
- CS2 ships **Unity Entities 1.3.10** (modern API); the assembly version metadata is all zeros, the embedded `com.unity.entities@1.3.10` string is the authoritative marker.

## Project layout and commands

Two .NET projects plus the vendored submodule, grouped by `agents-plugins.slnx` at the repo root (build both with `dotnet build agents-plugins.slnx`; the repo has no other .NET code).
Both set `TreatWarningsAsErrors`, so a plain build doubles as the C# typecheck/lint.
Formatting is `jb cleanupcode` (ReSharper CLI, pinned in `.config/dotnet-tools.json` as `JetBrains.ReSharper.GlobalTools`; standalone, no Rider install needed) honoring the root `.editorconfig` (same-line braces, 2-space, Stroustrup else/catch, file-scoped namespaces, and the `resharper_*` wrapping keys such as dangling `)` that `dotnet format` cannot do): run `mise fix:cs` (after `dotnet tool restore`), which excludes the vendored tree and the generated `obj/` patch. There is no `check:cs`: jb has no read-only/dry-run mode and C# is inert to CI checks, so format C# by running `fix:cs` and committing (Rider uses the same engine + `.editorconfig` live).
Nullable policy splits along the vendored line: `sdb/` is `Nullable=disable` (the vendored client is nullable-oblivious, its known warnings silenced via `NoWarn`); `mcp/` is nullable-clean (see `.agents/rules/cs-code-style.md`).

- `package.json`: private release-please version anchor; NOT a bun workspace package.
- `.claude-plugin/plugin.json` + `.mcp.json`: Claude Code manifest and server wiring (`${CLAUDE_PLUGIN_ROOT}`-based exe path, `UNITY_MCP_*` env passthrough).
- `.codex-plugin/plugin.json` + `.codex-plugin/mcp.json`: Codex CLI manifest pair (relative `cwd`, no env block; the server falls back to its built-in defaults there).
- `skills/`: the plugin's skills (`unity-driving`).
- `sdb/` (`UnityDevtools.Sdb`): the SDB client library and the PUBLIC surface consumers use, so no other project touches vendored code.
  It compiles the vendored `Mono.Debugger.Soft` sources (via the `../vendor` globs), the `Locale`/`AsyncResult` shims, and the build-time `PatchVendoredConnection` target, plus the plumbing: `SdbSession` (synchronous attach via the internal `TcpConnection`, running-state normalization, guaranteed resume+detach on dispose), `Invoker` (mirror-level type/method resolution, main-thread invokes, value formatting), `Ecs` (world selection, entity queries, component/buffer read-write, value parsing), `UnitySession` (the persistent lazy-attach session model above), and `SdbDiscovery` (process + SDB-port scan).
  Kept `Nullable=disable` precisely so the vendored sources compile and consumers can be nullable-clean.
- `mcp/` (`unity-devtools-mcp`): net10.0 MCP server, referencing `sdb/`.
  Uses the official `ModelContextProtocol` C# SDK (stdio transport, generic-host builder, attribute-based instance tool classes taking the shared `UnitySession` via DI) with `Microsoft.Extensions.Hosting`.
  Tool implementations live in `SessionTools.cs`, `TypeTools.cs`, and `EcsTools.cs`; `ToolGuard` wraps bodies in `McpException` so error messages reach the client verbatim.
  Nullable-clean, warnings as errors.
  `mcp/package.json` is that unit's private release-please anchor; the csproj `<Version>` is synced from it and reaches the MCP handshake through the assembly version.
  All logs go to stderr so they never corrupt the stdio stream.
- `mcp/dist/unity-devtools-mcp.exe`: the shipped single-file framework-dependent exe. COMMITTED on purpose (zero-build plugin installs; the git submodule does not ship through marketplace installs).
  `mise build:unity:mcp` (via `scripts/build-unity-mcp.ps1`) publishes it; when a running MCP server locks the exe, the script rename-swaps it aside (`.exe.stale`, gitignored, cleaned on the next run) so the publish never fails, and reconnecting via `/mcp` picks up the new file.
  A lefthook pre-commit rebuilds and stages the exe whenever staged files touch the C# sources; CI builds the solution but does NOT diff the exe (publish output is not assumed byte-reproducible).
  For live use in a Claude Code session, the root `.mcp.json` (LOCAL DEV ONLY) registers it as the `unity` server, launching the committed exe directly.
- `vendor/unity-mono/`: Unity's mono fork, pinned to branch `unity-6000.6-mbe`, as a **sparse, shallow, blob-filtered clone** containing only `mcs/class/Mono.Debugger.Soft/Mono.Debugger.Soft/` (~75 files, MIT).
  The branch choice is provenance only: the `mcs/class/Mono.Debugger.Soft` tree hash is IDENTICAL across `unity-2022.3-mbe` (CS2's Unity is 2022.3.71f1), `unity-6000.6-mbe`, and `unity-main` (verified 2026-07-17); Unity only evolves the agent side.
  We track the newest release branch so the pin follows any future client fixes; the SDB wire protocol is version-negotiated at attach, so one client serves all Mono-era Unity agents.
  The `sdb/` library compiles these sources into its assembly, so `SdbSession` reaches the internal `TcpConnection` directly.
  After a fresh submodule init, restore the sparse checkout with:
  `git -C plugins/unity-devtools/vendor/unity-mono sparse-checkout set mcs/class/Mono.Debugger.Soft/Mono.Debugger.Soft`

## SDB gotchas (verified, do not relearn the hard way)

- Discover the SDB port by scanning the game process's listen ports in 56000-56999 (`status` does this); the agent picks it dynamically (no fixed formula) and the port drifts between runs.
  If nothing lands in that range, `SdbDiscovery.PickSdbPort` falls back to the highest listen port at or above 56000, so a further drift still resolves.
  CS2 also listens on 9444 (Gameface CDP; both channels coexist) and 55000 (PlayerConnection), both well below the SDB range.
- The agent pushes a `VM_START` composite event at attach; pump it before touching the suspend state (see `SdbSession.Connect`).
  Suspends are **counted**: resume in a loop until "not suspended" to guarantee the game runs (see `SdbSession`).
  A closed socket auto-resumes the VM (safety net, verified).
- Invokes require a suspended thread and run on the **main thread only** (ECS thread-safety).
  Right after suspend, the main thread may still be in native engine code and reports `NOT_SUSPENDED` until it reaches a managed safepoint; retry with a short sleep (see `Invoker.Retrying`).
  It reaches one within a frame.
- Modern .NET removed delegate `BeginInvoke`: the vendored `VirtualMachineManager.Begin*` paths are dead ends (connect synchronously via the internal `TcpConnection` instead), and `Connection.cs`'s reply dispatch needs the build-time patch in the csproj (`PatchVendoredConnection` target; patches into `obj/`, vendored tree stays pristine).
- All Cecil-dependent vendored code is behind `#if ENABLE_CECIL`; do not define it (no Mono.Cecil dependency, live mirrors and invokes suffice).
- Unity Entities specifics: `World.All` throws if enumerated via IEnumerable (use Count + indexer); `ToEntityArray` takes `AllocatorManager.AllocatorHandle` (build via `op_Implicit(Allocator)`; Temp=2 needs no Dispose); `Get/SetComponentData<T>` require `T : unmanaged, IComponentData` (managed components are out of reach); the non-generic `*Raw` accessors are `internal` and traffic in `void*`, useless over SDB.
- Single debugger client at a time: Rider/dnSpy/VS cannot be attached while this tool is, and vice versa; the `detach` tool frees the slot.
- More verified invoke capabilities: out parameters work via `InvokeOptions.ReturnOutArgs` (`EndInvokeMethodWithResult(...).OutArgs` returns every argument post-call, out values updated); `DynamicBuffer<T>` mutation works through boxed-struct invokes (`get_Item`/`Add`/`RemoveAt` hit the live chunk data, not a copy); an Entity value can be built client-side by cloning the `Entity.Null` StructMirror and overwriting `Index`/`Version` (no debuggee allocation needed); managed systems are reachable via `World.GetExistingSystemManaged(Type)`.
  When consistency between several reads/writes matters, hold a suspend window with the `suspend` tool (the whole game freezes) instead of pausing only the simulation game-side.

## Preferred agent behavior

- After changing `mcp/` or `sdb/` sources, run `mise build:unity:mcp`; the running `unity` MCP server keeps serving the old (rename-swapped) exe, so ask the user to hit Reconnect in `/mcp` whenever you need the new build. Ask in plain text and end your turn: the user cannot run `/mcp` while an AskUserQuestion prompt is pending.
- Store hard-won facts about SDB/Unity internals in memory.

## Boundaries

- The tool must always resume + detach even on failure (`SdbSession.Dispose`, `UnitySession`).
- Keep it generic: no game-specific type names or behavior hardcoded in the tool; CS2 appears only in docs/examples (discovery is by SDB-port signature, with `UNITY_MCP_PROCESS` as the user's narrowing knob).
- Writes mutate live game state; assume a throwaway save when testing.
