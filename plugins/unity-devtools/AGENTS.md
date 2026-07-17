# AGENTS.md

## Plugin overview

`unity-devtools` is a (future) generic plugin for driving a running **Unity Mono development
build** from the outside, over the **Mono Soft Debugger protocol (SDB)**: no code injection, no
game modification. Cities: Skylines II is the reference/test target (a dev Mono build with the SDB
agent live), mirroring how `coherent-gameface` is generic Gameface with CS2 as reference.

Current state: **proof of concept only** (`poc/`). Not registered in either marketplace file; no
plugin manifests yet. The shipping checklist lives in `docs/ROADMAP.md` at the repo root.

## What the PoC proves (verified live against CS2, 2026-07-16)

- Attach to the game's SDB port, inspect, resume, detach cleanly; the game keeps running.
  **Reattach across invocations works**, so the session model is attach-per-CLI-call (no daemon).
- Resolve types by name live (`find-types`), list their members via mirrors (no Mono.Cecil).
- Query ECS entities generically: `World.All` → `EntityManager` (boxed struct invokes) →
  `ComponentType.ReadWrite(Type)` → `CreateEntityQuery(ComponentType[])` → `CalculateEntityCount`
  / `ToEntityArray(AllocatorHandle)` → `NativeArray.ToArray()` → managed `Entity[]` mirror.
- Read AND write a component on one entity via `EntityManager.Get/SetComponentData<T>` instantiated
  live with `MethodMirror.MakeGenericMethod` (protocol 2.24+; CS2 answers 2.58). Writes persist in
  the running simulation. No debuggee-side reflection fallback was needed.
- CS2 ships **Unity Entities 1.3.10** (modern API); the assembly version metadata is all zeros, the
  embedded `com.unity.entities@1.3.10` string is the authoritative marker.

## PoC layout and commands

- `poc/`: net10.0 console CLI (`unity-devtools-poc`), Windows-targeted (netstat parsing).
  Build: `mise poc:unity:build`. Run: `mise poc:unity:run <command>`, e.g.
  `mise poc:unity:run status` then
  `mise poc:unity:run query Game.Citizens.Citizen --port <sdb-port>`.
  Subcommands: `status`, `attach-check`, `find-types`, `query` (with `--label` to annotate
  entities via a system call, e.g. a name system), `get-component`, `set-component` (supports
  Entity-typed fields as `index:version`), `call-system`, `call-static` (supports out params),
  `get-buffer`, `buffer-add`, `buffer-remove-at` (run with no args for usage).
  A compound live scenario was verified E2E on CS2 (2026-07-17): moved a citizen between
  households by rewriting `HouseholdMember` + both `HouseholdCitizen` buffers, with addresses
  resolved via `BuildingUtils.GetAddress` and names via `NameSystem.GetRenderedLabelName`.
- `vendor/unity-mono/`: Unity's mono fork, pinned to branch `unity-6000.6-mbe`, as a **sparse,
  shallow, blob-filtered clone** containing only
  `mcs/class/Mono.Debugger.Soft/Mono.Debugger.Soft/` (~75 files, MIT). The branch choice is
  provenance only: the `mcs/class/Mono.Debugger.Soft` tree hash is IDENTICAL across
  `unity-2022.3-mbe` (CS2's Unity is 2022.3.71f1), `unity-6000.6-mbe`, and `unity-main`
  (verified 2026-07-17); Unity only evolves the agent side. We track the newest release branch so
  the pin follows any future client fixes; the SDB wire protocol is version-negotiated at attach,
  so one client serves all Mono-era Unity agents. The PoC csproj compiles
  these sources directly into the CLI (internal types like `TcpConnection` are therefore
  accessible). After a fresh submodule init, restore the sparse checkout with:
  `git -C plugins/unity-devtools/vendor/unity-mono sparse-checkout set mcs/class/Mono.Debugger.Soft/Mono.Debugger.Soft`

## SDB gotchas (verified, do not relearn the hard way)

- Discover the SDB port by scanning the game process's listen ports in 56000-56511 (`status` does
  this); the port is not a fixed formula. CS2 also listens on 9444 (Gameface CDP; both channels
  coexist) and 55000 (PlayerConnection).
- The agent pushes a `VM_START` composite event at attach; pump it before suspending. Suspends are
  **counted**: resume in a loop until "not suspended" before detaching (see `SdbSession.Dispose`).
  A closed socket auto-resumes the VM (safety net, verified).
- Invokes require a suspended thread and run on the **main thread only** (ECS thread-safety).
  Right after suspend, the main thread may still be in native engine code and reports
  `NOT_SUSPENDED` until it reaches a managed safepoint; retry with a short sleep
  (see `Invoker.Retrying`). It reaches one within a frame.
- Modern .NET removed delegate `BeginInvoke`: the vendored `VirtualMachineManager.Begin*` paths
  are dead ends (connect synchronously via the internal `TcpConnection` instead), and
  `Connection.cs`'s reply dispatch needs the build-time patch in the csproj
  (`PatchVendoredConnection` target; patches into `obj/`, vendored tree stays pristine).
- All Cecil-dependent vendored code is behind `#if ENABLE_CECIL`; do not define it (no Mono.Cecil
  dependency, live mirrors and invokes suffice).
- Unity Entities specifics: `World.All` throws if enumerated via IEnumerable (use Count + indexer);
  `ToEntityArray` takes `AllocatorManager.AllocatorHandle` (build via `op_Implicit(Allocator)`;
  Temp=2 needs no Dispose); `Get/SetComponentData<T>` require `T : unmanaged, IComponentData`
  (managed components are out of reach); the non-generic `*Raw` accessors are `internal` and
  traffic in `void*`, useless over SDB.
- Single debugger client at a time: Rider/dnSpy/VS cannot be attached while this tool is, and vice
  versa.
- More verified invoke capabilities: out parameters work via `InvokeOptions.ReturnOutArgs`
  (`EndInvokeMethodWithResult(...).OutArgs` returns every argument post-call, out values updated);
  `DynamicBuffer<T>` mutation works through boxed-struct invokes (`get_Item`/`Add`/`RemoveAt` hit
  the live chunk data, not a copy); an Entity value can be built client-side by cloning the
  `Entity.Null` StructMirror and overwriting `Index`/`Version` (no debuggee allocation needed);
  managed systems are reachable via `World.GetExistingSystemManaged(Type)`. Multi-step state
  edits span multiple attach sessions, so pause the game's simulation first when consistency
  between writes matters (on CS2: `SimulationSystem.selectedSpeed`).

## Boundaries

- The tool must always resume + detach even on failure (try/finally; `SdbSession.Dispose`).
- Keep it generic: no CS2-specific type names or behavior hardcoded in the tool; CS2 appears only
  in docs/examples and the default process name (`Cities2`, overridable via `--process`).
- Writes mutate live game state; assume a throwaway save when testing.
