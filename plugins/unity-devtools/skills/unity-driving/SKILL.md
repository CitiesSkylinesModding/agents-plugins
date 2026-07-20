---
name: unity-driving
description: 'Driving a live Unity Mono development build with the unity MCP tools. Load before first use of any unity tool, when planning multi-step live state edits, or when a unity tool call fails or returns puzzling results.'
---

# Driving a Unity game over SDB

The `unity` tools drive a running Unity Mono development build over the Mono Soft Debugger protocol (SDB); this skill is the field-verified procedure the tool schemas cannot carry.
Everything holds for any dev-Mono Unity game; game-specific facts are labeled (verified on Cities: Skylines II, "CS2").
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

Every type parameter wants a fully qualified name (`Game.Citizens.Citizen`, not `Citizen`).
`find_types` resolves one live, case-insensitively; it cannot search by fragment, so harvest candidate names offline from the game's source code and confirm live.
Before writing, run `find_types` with `members`: live field names and types are the ground truth for `ecs_set_component` and buffer edits.

## Entities and ECS

An entity is `index:version`; the version disambiguates recycled indices, so carry it when you have it (a bare `index` matches any version in component tools, and defaults to version 1 in buffer tools).
`ecs_query` counts and lists entities having ALL the given components; the count is always exact, `limit` caps only the listing.
`label` attaches human-readable identity to raw entities via a one-Entity-arg method on a managed system (verified on CS2: `Game.UI.NameSystem:GetRenderedLabelName`).
Component access covers unmanaged `IComponentData` only; managed (class) components are out of reach over SDB.
`ecs_set_component` is a whole-component read-modify-write overriding one field, reporting before and after read back from the game: verification is built in.
`ecs_buffer_edit` `add` clones element 0 as its template (an empty buffer cannot seed a new element) and overrides one field via `set`.
Entity-typed fields and arguments are written as `index:version` text.

## Evaluating C# in the game

`eval` runs a C# statement sequence on the game's main thread, like an IDE debugger: `var` declarations, expression statements, and assignments; the final expression's value is the result (its trailing semicolon is optional).
Roots are fully-qualified type names plus the builtins `em` (the selected world's EntityManager), `world` (the World), `entity(index, version)` (an Entity value), and `_` (the previous successful eval's result; a heap result may be garbage-collected once the game resumes, and using it then fails with a "re-evaluate" error).
Generic methods take explicit type arguments: `em.GetComponentData<Game.Citizens.HouseholdMember>(entity(123, 1))`.
Managed systems are plain C#: `world.GetExistingSystemManaged(typeof(Game.UI.NameSystem)).SetCustomName(entity(123, 1), "New Name")`.
Structs build with initializer syntax (`new Game.Citizens.HouseholdMember { m_Household = h }`), and struct writes follow honest C# copy semantics: mutating a component copy does not persist it, finish with `em.SetComponentData(entity(...), copy)`.
`out var x` declares a local the call writes; later statements can read it (verified pattern on CS2: `Game.Buildings.BuildingUtils.GetAddress(em, e, out var road, out var number)`).
Excluded by design: lambdas, LINQ, loops, and control flow (ternary, `?.`, and `??` do work); unsupported constructs are rejected up front with an "unsupported: ..." parse error.
One eval runs in one suspend window; hold `suspend`/`resume` around several evals when they must see one consistent state.
Methods match by name, arity, and argument compatibility; "method not found" usually means wrong arity or wrong declaring type, and `find_types` with `members` settles both.
On failure the error reports the failing statement, the in-game exception, and the locals evaluated so far; on success only the final value returns (depth-3 formatting), so end with an interpolation like `$"{a} | {b}"` to read several values at once.

## Writes are live

Every write hits the running simulation immediately and persists; there is no undo.
Assume a throwaway save, and read state back after each mutation rather than chaining blind writes.

## When a call fails

Error messages come from the server verbatim and usually name the fix (unknown type, missing field with the field list, entity not found).
"No dev-Mono Unity game found" means the game is not running, or is not a development Mono build; `status` settles which.
A mid-call connection drop retries once against fresh discovery, so transient drops self-heal; repeated connection failures mean the game is gone, so report it and wait for a relaunch.
