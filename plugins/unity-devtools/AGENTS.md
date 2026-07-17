# AGENTS.md

## Plugin overview

`unity-devtools` is a (future) generic plugin for driving a running **Unity Mono development build** from the outside, over the **Mono Soft Debugger protocol (SDB)**: no code injection, no game modification.
Cities: Skylines II is the reference/test target (a dev Mono build with the SDB agent live), mirroring how `coherent-gameface` is generic Gameface with CS2 as reference.

Current state: **pre-plugin**.
The vendored SDB client and the attach/invoke plumbing are isolated in a class library (`sdb/`); a verified PoC CLI (`poc/`) exercises the full ECS toolset; and a demo MCP server (`mcp/`) exposes attach-level tools over stdio.
Not registered in either marketplace file; no plugin manifests yet.
The shipping checklist lives in `docs/ROADMAP.md` at the repo root.

## What the PoC proves (verified live against CS2, 2026-07-16)

- Attach to the game's SDB port, inspect, resume, detach cleanly; the game keeps running.
  **Reattach across invocations works**, so the session model is attach-per-CLI-call (no daemon).
- Resolve types by name live (`find-types`), list their members via mirrors (no Mono.Cecil).
- Query ECS entities generically: `World.All` → `EntityManager` (boxed struct invokes) → `ComponentType.ReadWrite(Type)` → `CreateEntityQuery(ComponentType[])` → `CalculateEntityCount` / `ToEntityArray(AllocatorHandle)` → `NativeArray.ToArray()` → managed `Entity[]` mirror.
- Read AND write a component on one entity via `EntityManager.Get/SetComponentData<T>` instantiated live with `MethodMirror.MakeGenericMethod` (protocol 2.24+; CS2 answers 2.58).
  Writes persist in the running simulation.
  No debuggee-side reflection fallback was needed.
- CS2 ships **Unity Entities 1.3.10** (modern API); the assembly version metadata is all zeros, the embedded `com.unity.entities@1.3.10` string is the authoritative marker.

## Project layout and commands

Three .NET projects plus the vendored submodule, grouped by `agents-plugins.slnx` at the repo root (build all three with `dotnet build agents-plugins.slnx`; the repo has no other .NET code).
All three set `TreatWarningsAsErrors`, so a plain build doubles as the C# typecheck/lint.
Formatting is `jb cleanupcode` (ReSharper CLI, pinned in `.config/dotnet-tools.json` as `JetBrains.ReSharper.GlobalTools`; standalone, no Rider install needed) honoring the root `.editorconfig` (same-line braces, 2-space, Stroustrup else/catch, file-scoped namespaces, and the `resharper_*` wrapping keys such as dangling `)` that `dotnet format` cannot do): run `mise fix:cs` (after `dotnet tool restore`), which excludes the vendored tree and the generated `obj/` patch. There is no `check:cs`: jb has no read-only/dry-run mode and C# is inert to CI, so format C# by running `fix:cs` and committing (Rider uses the same engine + `.editorconfig` live).
mise tasks are verb-first: `build:unity:poc` / `build:unity:mcp` and `run:unity:poc` / `run:unity:mcp`; `mise build` (no arg) builds everything including the gameface bundle.
Nullable policy splits along the vendored line: `sdb/` is `Nullable=disable` (the vendored client is nullable-oblivious, its known warnings silenced via `NoWarn`); `mcp/` is nullable-clean (see `.agents/rules/cs-code-style.md`).
The `poc/` CLI stays `Nullable=disable` too, its mirror plumbing being nullable-oblivious.

- `sdb/` (`UnityDevtools.Sdb`): the SDB client library and the PUBLIC surface consumers use, so no other project touches vendored code.
  It compiles the vendored `Mono.Debugger.Soft` sources (via the `../vendor` globs), the `Locale`/`AsyncResult` shims, and the build-time `PatchVendoredConnection` target, plus the plumbing: `SdbSession` (synchronous attach via the internal `TcpConnection`, suspend, guaranteed resume+detach on dispose), `Invoker` (mirror-level type/method resolution, main-thread invokes, value formatting), and `SdbDiscovery` (process + SDB-port scan).
  Kept `Nullable=disable` precisely so the vendored sources compile and consumers can be nullable-clean.
- `poc/` (`unity-devtools-poc`): net10.0 console CLI, Windows-targeted (netstat parsing), referencing `sdb/`.
  Carries the ECS commands (`Ecs.cs`) and the CLI shell (`Program.cs`) only.
  Build: `mise build:unity:poc`.
  Run: `mise run:unity:poc <command>`, e.g. `mise run:unity:poc status` then `mise run:unity:poc query Game.Citizens.Citizen --port <sdb-port>`.
  Subcommands: `status`, `attach-check`, `find-types`, `query` (with `--label` to annotate entities via a system call, e.g. a name system), `get-component`, `set-component` (supports Entity-typed fields as `index:version`), `call-system`, `call-static` (supports out params), `get-buffer`, `buffer-add`, `buffer-remove-at` (run with no args for usage).
  A compound live scenario was verified E2E on CS2 (2026-07-17): moved a citizen between households by rewriting `HouseholdMember` + both `HouseholdCitizen` buffers, with addresses resolved via `BuildingUtils.GetAddress` and names via `NameSystem.GetRenderedLabelName`.
- `mcp/` (`unity-devtools-mcp`): net10.0 demo MCP server, referencing `sdb/`.
  Uses the official `ModelContextProtocol` C# SDK (stdio transport, generic-host builder, attribute-based tools) with `Microsoft.Extensions.Hosting` for the host builder.
  Exposes attach-level tools ONLY, on purpose (a walking skeleton proving a C# MCP server can reach the game): `status` (find the game process and its SDB port, no attach) and `attach_check` (attach, report VM/protocol/thread info, resume, detach; the game is only briefly suspended).
  No ECS/inspection tools yet; that is the next roadmap step.
  Nullable-clean, warnings as errors.
  `mise build:unity:mcp` publishes it as a single framework-dependent exe to `mcp/dist/` (gitignored, not committed); `mise run:unity:mcp` runs it over stdio for a manual smoke test.
  For live use in a Claude Code session, the root `.mcp.json` (LOCAL DEV ONLY) registers it as the `unity` server, launching `mcp/dist/unity-devtools-mcp.exe` directly; rebuild with `mise build:unity:mcp` then reconnect via `/mcp`.
  All logs go to stderr so they never corrupt the stdio stream (verified: an immediate stdin EOF races shutdown and swallows responses, but real clients keep stdin open).
  Verified live end-to-end against CS2 (2026-07-17): `status` found the SDB port and `attach_check` reported the VM (`mono 6.13.0`, protocol 2.58) then resumed and detached, game unharmed.
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
- The agent pushes a `VM_START` composite event at attach; pump it before suspending.
  Suspends are **counted**: resume in a loop until "not suspended" before detaching (see `SdbSession.Dispose`).
  A closed socket auto-resumes the VM (safety net, verified).
- Invokes require a suspended thread and run on the **main thread only** (ECS thread-safety).
  Right after suspend, the main thread may still be in native engine code and reports `NOT_SUSPENDED` until it reaches a managed safepoint; retry with a short sleep (see `Invoker.Retrying`).
  It reaches one within a frame.
- Modern .NET removed delegate `BeginInvoke`: the vendored `VirtualMachineManager.Begin*` paths are dead ends (connect synchronously via the internal `TcpConnection` instead), and `Connection.cs`'s reply dispatch needs the build-time patch in the csproj (`PatchVendoredConnection` target; patches into `obj/`, vendored tree stays pristine).
- All Cecil-dependent vendored code is behind `#if ENABLE_CECIL`; do not define it (no Mono.Cecil dependency, live mirrors and invokes suffice).
- Unity Entities specifics: `World.All` throws if enumerated via IEnumerable (use Count + indexer); `ToEntityArray` takes `AllocatorManager.AllocatorHandle` (build via `op_Implicit(Allocator)`; Temp=2 needs no Dispose); `Get/SetComponentData<T>` require `T : unmanaged, IComponentData` (managed components are out of reach); the non-generic `*Raw` accessors are `internal` and traffic in `void*`, useless over SDB.
- Single debugger client at a time: Rider/dnSpy/VS cannot be attached while this tool is, and vice versa.
- More verified invoke capabilities: out parameters work via `InvokeOptions.ReturnOutArgs` (`EndInvokeMethodWithResult(...).OutArgs` returns every argument post-call, out values updated); `DynamicBuffer<T>` mutation works through boxed-struct invokes (`get_Item`/`Add`/`RemoveAt` hit the live chunk data, not a copy); an Entity value can be built client-side by cloning the `Entity.Null` StructMirror and overwriting `Index`/`Version` (no debuggee allocation needed); managed systems are reachable via `World.GetExistingSystemManaged(Type)`.
  Multi-step state edits span multiple attach sessions, so pause the game's simulation first when consistency between writes matters (on CS2: `SimulationSystem.selectedSpeed`).

## Boundaries

- The tool must always resume + detach even on failure (try/finally; `SdbSession.Dispose`).
- Keep it generic: no CS2-specific type names or behavior hardcoded in the tool; CS2 appears only in docs/examples and the default process name (`Cities2`, overridable via `--process`).
- Writes mutate live game state; assume a throwaway save when testing.
