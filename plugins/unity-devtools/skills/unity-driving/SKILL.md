---
name: unity-driving
description: 'Driving a live Unity Mono development build with the unity MCP tools. Load before first use of any unity tool, when planning multi-step live state edits, or when a unity tool call fails or returns puzzling results.'
---

# Driving a Unity game over SDB

The `unity` tools drive a running Unity Mono development build over the Mono Soft Debugger protocol (SDB); this skill is the procedure the tool schemas cannot carry.
Everything holds for any dev-Mono Unity game; the type names in examples are placeholders (`MyGame.…`), so substitute the target's own.
A retail build exposes no SDB port; only a development Mono build is drivable.

## Session lifecycle

There is no attach tool: the first tool that needs the VM attaches lazily, the session persists, and a dropped connection reattaches on the next call, so just call the tool you need.
`status` is the read-only orient step: candidate processes with their SDB port, plus session state (attached, held suspensions).
The SDB port drifts between game runs; discovery re-resolves it on every (re)attach, so a game restart costs nothing.
Discovery that finds several candidates fails with the list; narrow with the process-name prefix or the `UNITY_MCP_PROCESS` / `UNITY_MCP_PORT` env config.
The debugger slot is exclusive: while attached, an IDE debugger (Rider/dnSpy/VS) cannot attach to the game, and vice versa; `detach` frees the slot, and the next unity call reattaches on its own.
An attach failure while an IDE holds the slot looks like a connection refusal, not "slot taken".

## Suspend windows

Between calls the game runs; each operation freezes it briefly around itself, so single reads and writes need no ceremony.
When several calls must see one consistent state (read-decide-write, multi-write edits), open a window: `suspend`, act, `resume`.
A held window freezes the game entirely (simulation AND rendering); keep it short, and treat a long-held window as a bug in your plan.
Suspensions are counted: one `resume` per `suspend`; `status` shows the held count.
`detach`, a dropped connection, and server shutdown all resume the game fully, as last-resort safety nets.

## Names and types

Every type parameter wants a fully qualified name (`MyGame.Citizens.Citizen`, not `Citizen`).
`find_types` answers naming from either end: `search` turns a concept you can only describe (a mechanic, a system's job) into names harvested from the running process, and `fullName` resolves one you already hold.
Start broad and narrow on `count`: only the first search pays the harvest, which is the longest single freeze these tools cause, so iterating on a pattern afterwards costs only your reading.
The convention across the toolset: a pattern you author is a regex (`search`), a fragment you paste is a substring (`signatureContains` on the debug tools, the process-name prefix).
Before writing, run `find_types` with `members`: live field names and types are the ground truth for `ecs_set_component` and buffer edits.

## Entities and ECS

An entity is `index[:version]`, read identically by every ECS tool: a bare `index` resolves to whatever is live at that index, an explicit `index:version` is verified and fails loudly when stale rather than reading the entity that recycled the index.
Carry the version when you have it: it is what catches a recycle between reading an index and acting on it.
`ecs_query` counts and lists entities having ALL the given components; the count is always exact, `limit` caps only the listing.
`label` attaches human-readable identity to raw entities via a one-Entity-arg method on a managed system, typically the game's name system (`MyGame.UI.NameSystem:GetRenderedLabelName`).
State on a prefab or a disabled entity is invisible to `ecs_query` (the engine's own `EntityQuery` exclusion), so chase those by following a reference into the tool below rather than by querying for them.
`ecs_list_components` is the orient step on an unknown entity: one call lists every component type it carries, so a read starts from what is there instead of from a guess.
Each entry's `kind` says what can read it — `component` → `ecs_get_component`, `buffer` → `ecs_get_buffer`, `tag` → no fields to read (presence, plus `enabled` where it carries one, is the state), `shared` and `chunk` → `eval` only, `managed` (class `IComponentData`) → out of reach over SDB, listed so you know the state is there.
Enableable components carry `enabled`: read it before concluding a system should have acted on the entity.
Orient on the shape first; `values=true` adds each `component` entry's contents when the shape is not enough to act on.
State the entity you hold does not carry often lives on one it references, and `follow` chases that reference in the same call, naming the component that holds it: a placed instance whose data sits on its prefab costs one call, not two.
`ecs_set_component` is a whole-component read-modify-write overriding one field, reporting before and after read back from the game: verification is built in.
`ecs_buffer_edit` `add` clones element 0 as its template (an empty buffer cannot seed a new element) and overrides one field via `set`.
An entity you WRITE is never resolved: a component field, a buffer `set`, and eval's `entity(index)` all take the value literally and default the version to 1, so spell those `index:version`.

## Evaluating C# in the game

`eval` runs a C# statement sequence on the game's main thread, like an IDE debugger: `var` declarations, expression statements, and assignments; the final expression's value is the result (its trailing semicolon is optional).
Roots are fully-qualified type names plus the builtins `em` (the selected world's EntityManager), `world` (the World), `entity(index, version)` (an Entity value), and `_` (the previous successful eval's result; a heap result may be garbage-collected once the game resumes, and using it then fails with a "re-evaluate" error).
Generic methods take explicit type arguments: `em.GetComponentData<MyGame.Citizens.HouseholdMember>(entity(123, 1))`.
Managed systems are plain C#: `world.GetExistingSystemManaged(typeof(MyGame.UI.NameSystem)).SetCustomName(entity(123, 1), "New Name")`.
Structs build with initializer syntax (`new MyGame.Citizens.HouseholdMember { m_Household = h }`), and struct writes follow honest C# copy semantics: mutating a component copy does not persist it, finish with `em.SetComponentData(entity(...), copy)`.
`out var x` declares a local the call writes; later statements can read it: `MyGame.Buildings.BuildingUtils.GetAddress(em, e, out var road, out var number)`.
Excluded by design: lambdas, LINQ, loops, and control flow (ternary, `?.`, and `??` do work); unsupported constructs are rejected up front with an "unsupported: ..." parse error.
One eval runs in one suspend window; hold `suspend`/`resume` around several evals when they must see one consistent state.
Methods match by name, arity, and argument compatibility; "method not found" usually means wrong arity or wrong declaring type, and `find_types` with `members` settles both.
On failure the error reports the failing statement, the in-game exception, and the locals evaluated so far; on success only the final value returns, nested structs formatted to a fixed depth with anything deeper elided as `TypeName {...}`, so end with an interpolation like `$"{a} | {b}"` to read several values at once.

## Seeing the game

One `eval` of `UnityEngine.ScreenCapture.CaptureScreenshot(path)` writes a PNG of the composited frame, 3D scene and UI together; the API is engine-level, so every Unity build carries it.
Build an absolute path from `UnityEngine.Application.persistentDataPath`, since a relative one resolves per platform.
The call is a request rather than a capture: it returns `null` at once and a rendered frame is what fulfils it.
A game that renders nothing therefore produces no file — under a held `suspend`, or when the window is minimized and the game does not run in background — and the image, once a frame arrives, shows that later frame rather than the moment you asked.
`advance` is the way to spend a frame without giving up a held window.
The file lands on the machine running the game and no tool here reads it back, so opening it is your own filesystem's job and only works when you share that machine.

## Debugging with breakpoints

Where `eval` reads the game from outside, the `debug_*` tools stop it from inside: arm (`debug_set_breakpoint` / `debug_break_on_exception`), trigger the behavior in game, catch the hit (`debug_wait`), inspect (`debug_pause_state`, `debug_evaluate`), move (`debug_step`), release (`debug_step action=resume`).
Burst decides what is debuggable: Burst-compiled jobs are native code the Mono debugger cannot see, no frames and no hits on any thread; managed code hits fine, worker threads included.
Mod code is normally non-Burst, so it just works; to reach the game's own Burst-compiled systems, run the game with Burst compilation disabled (Unity games generally take a `-burst-disable-compilation` launch option) and everything becomes managed and visible.
A hit freezes the whole game until released, but every other unity tool keeps working against the frozen state (suspends are counted), so inspect at leisure; the UI stops rendering, which is normal, not a crash.
Arm hot-path breakpoints with a `condition` gating on the instance you care about (an entity index, a parameter value, a field of `this`): false hits release the game automatically, so a method called every frame costs little until YOUR case arrives.
A condition that fails to evaluate pauses with the error recorded in the pause state; fix the expression and re-arm rather than guessing.
`debug_evaluate` is `eval` plus the frame: locals, parameters, and `this` resolve and assign like C# variables, `frameIndex` climbs the stack, and `_` is shared with `eval`.
Method entry (no line) is the only anchor that works without debug info; `debug_locations` is the substitute for source text (SDB carries none), mapping lines to IL offsets and telling you which lines can host a breakpoint.
Breakpoints die with the connection (game restart, detach): `debug_status` is the ground truth for what is armed, re-arm after any reconnect.
The cross-plugin loop: the unity and gameface tools drive the same process over separate channels, so arm a breakpoint on a handler, drive the UI to trigger it with the gameface tools (`game_click`, ...), and `debug_wait` catches the hit; while the game sits paused at it, gameface calls that need a live frame loop will stall, so inspect over SDB, resume, then return to the UI.

## Advancing time deterministically

"Let the simulation react, then verify" is `advance`: under a held `suspend` window it releases the game for N real seconds and re-freezes it, replacing the unpause-sleep-repause dance.
The game's OWN pause (a simulation-speed or pause flag in game state) is game logic no debugger operation can lift; pass `before`/`after` eval snippets to flip it, keeping the per-game recipe on your side.
With breakpoints armed, `advance` doubles as "run until the next hit, at most N seconds": a hit during the window holds after it returns (`pausedDuringAdvance`), so inspect it before resuming.

## Writes are live

Every write hits the running simulation immediately and persists; there is no undo.
Assume a throwaway save, and read state back after each mutation rather than chaining blind writes.

## When a call fails

Error messages come from the server verbatim and usually name the fix (unknown type, missing field with the field list, entity not found).
"No dev-Mono Unity game found" means the game is not running, or is not a development Mono build; `status` settles which.
A mid-call connection drop retries once against fresh discovery, so transient drops self-heal; repeated connection failures mean the game is gone, so report it and wait for a relaunch.
