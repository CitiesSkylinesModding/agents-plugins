# Unity discovery pair: entity archetype dump and type search

## Problem Statement

An agent driving a live Unity game through the `unity` tools can read anything it can name, and
nothing it cannot. Both halves of "naming" are currently dead ends.

**Finding a type.** `find_types` resolves an exact fully-qualified name and nothing else. An agent
that knows a concept ("attractiveness", "household") but not the namespace has no way to ask the
running game what types exist. The `unity-driving` skill documents the workaround honestly: harvest
candidate names offline from the game's source or a decompiler, then confirm them live. That sends
the agent out of the session to answer a question the running process could answer directly.

**Finding where state lives.** Once an agent holds an entity, discovering what that entity carries
means playing twenty questions with `HasComponent<T>` through `eval`, one guess per call. The
motivating case: hunting a building's attractiveness, which turned out to live not on the building
but on its prefab, several failed guesses later. Nothing in the toolset answers "what does this
entity actually carry".

A third, smaller problem surfaced while designing the fix. The ECS tools disagree with each other
about how an entity is named: the component tools treat a bare `index` as "any version" and scan a
query to resolve it, while the buffer tools silently default the version to `1`. The skill documents
the inconsistency rather than resolving it, and a bare index handed to a buffer tool can describe a
different entity than the caller meant.

## Solution

Two discovery capabilities, shipped together, plus the entity-naming cleanup they force.

**`ecs_list_components`** takes an entity and reports every component type on it, each annotated
with its kind — component, tag, buffer, shared, chunk, or managed — so the agent knows which tool
can read it, including when the answer is "none of them". Enableable components additionally report
whether they are currently enabled, since a present-but-disabled component is invisible to the
simulation while `HasComponent` still says yes. With `values=true` the unmanaged components inline
their field values, turning one call into a full picture of the entity. A `follow` parameter chases
one Entity-typed field to a second entity and dumps its archetype in the same response, which
answers the prefab case directly.

**`find_types` gains a `search` parameter** taking a regular expression matched against every type
name in the running process. The type list is harvested once per session, one invoke per assembly,
and cached client-side, so searching is free after the first call. The existing `fullName` parameter
is untouched: exact and cheap for names the agent already holds.

**Entity naming converges on one rule.** Every ECS tool accepts a bare `index` and resolves it to
the live entity in a single invoke, or accepts an explicit `index:version` and verifies it. The
query scan and the default-to-1 both disappear.

**In-game exceptions get their message back.** An invoke that throws inside the game reports the
game's own exception type and message on every tool, not only through the evaluator that happens to
own the unwrapping code today. Surfaced while verifying the entity work: the same
`NullReferenceException` was legible through `eval` and opaque through `ecs_get_component`.

**The buffer tools stop reading buffers the entity does not have.** `GetBuffer<T>` on a missing `T`
does not throw where the collections checks are compiled out; it returns a buffer over unowned
memory. Left unchecked it answers `length 0` and, because the tools ask for write access, takes the
game down. Also surfaced while verifying the entity work, twice.

## User Stories

1. As an agent with no domain knowledge of the target game, I want to search live types by pattern, so that I can find candidate names without leaving the session for a decompiler.
2. As an agent, I want the search to run against the actually-running process, so that the names I find are guaranteed to exist rather than harvested from a possibly-stale source tree.
3. As an agent, I want search results to name the assembly each type came from, so that I can tell a game type from an engine type from a mod type at a glance.
4. As an agent, I want search results to report the type's kind (struct, class, interface), so that I can tell a component from a system without a follow-up call.
5. As an agent, I want short-name matches ranked above namespace matches, so that searching a concept name puts the type named after that concept first.
6. As an agent, I want the search to be case-insensitive by default, so that I do not have to guess the target's casing convention.
7. As an agent, I want to write a full regular expression when I need one, so that I can express anchoring and alternation that a substring search cannot.
8. As an agent, I want an invalid pattern to fail with the runtime's own regex message, so that I can correct the syntax without guessing what the server disliked.
9. As an agent, I want a pathological pattern to fail on a timeout rather than hang, so that a bad backtracking case costs me one error instead of the session.
10. As an agent, I want the exact number of matches reported even when the listing is capped, so that I know whether to narrow the pattern.
11. As an agent, I want the number of omitted matches reported, so that I can distinguish "these are all of them" from "there are more".
12. As an agent, I want `members=true` refused on a broad search rather than served, so that a loose pattern cannot flood my context with thousands of method signatures.
13. As an agent, I want that refusal to name the way forward — a tighter pattern, or `fullName` — so that the error teaches me the tool.
14. As an agent, I want the first search to pay the harvest and later searches to be free, so that iterating on a pattern is cheap.
15. As an agent, I want types from an assembly loaded after my first search to be findable, so that a mod loaded mid-session does not become invisible.
16. As a mod author, I want types from my own assembly to remain searchable even when some of its types fail to load, so that a half-broken build against a changed game version is still explorable.
17. As an agent, I want a "type not found" error on `fullName` to point me at `search`, so that the dead end that used to send me to a decompiler now names its own escape.
18. As an agent, I want `fullName` to stay exact and cheap, so that resolving a name I already hold does not trigger a multi-second harvest.
19. As an agent landing on an unknown entity, I want to list every component it carries, so that I can see what state it holds without guessing type names one at a time.
20. As an agent, I want each listed component annotated with its kind, so that I know whether to read it with `ecs_get_component`, `ecs_get_buffer`, or `eval`.
21. As an agent, I want components no tool can read to be listed anyway and marked as such, so that I learn the state exists rather than concluding it does not.
22. As an agent, I want enableable components to report their enabled state, so that I do not mistake a disabled component for one the simulation is acting on.
23. As an agent, I want component values inlined on request, so that a single call answers both what the entity carries and what is in it.
24. As an agent, I want values off by default, so that a cheap structural question stays cheap.
25. As an agent, I want a component whose value read fails to record the failure on its own entry, so that one unreadable component does not fail the whole listing.
26. As an agent chasing state that lives on a referenced entity, I want to follow an Entity-typed field in the same call, so that the prefab case resolves without a manual second lookup.
27. As an agent, I want `follow` to take a component name alone when that component has exactly one Entity field, so that the common case needs no ceremony.
28. As an agent, I want an ambiguous `follow` to fail with the candidate fields listed, so that I can disambiguate without inspecting the type separately.
29. As an agent, I want the followed entry to record which component and field led to it, so that the provenance of the second archetype is explicit.
30. As an agent, I want `follow` to stop at one level, so that a response cannot fan out unboundedly and cycles cannot arise.
31. As an agent, I want to name an entity by bare index and get the live entity, so that an index read from a log or a UI is directly usable.
32. As an agent, I want every ECS tool to interpret an entity the same way, so that I do not have to remember which tools default the version and which scan for it.
33. As an agent, I want an explicit `index:version` to be verified rather than trusted, so that a stale version fails loudly instead of reading a recycled entity.
34. As an agent, I want an out-of-range index to produce a real error naming the valid range, so that I am not handed an opaque in-game null reference.
35. As an agent driving a game on an older Entities version, I want the tools to degrade rather than break, so that the plugin stays useful beyond the reference target.
36. As an agent, I want an unavailable capability reported explicitly in the response, so that a missing enabled-state column is never mistaken for "nothing is disabled".
37. As a plugin maintainer, I want capability detection to probe the exact members the code calls, so that support is decided by what actually exists rather than by a version number the runtime does not expose.
38. As a plugin maintainer, I want the type catalog covered by the integration suite, so that harvest, cache refresh, ranking, and limits are verified without a running game.
39. As a plugin maintainer, I want the partial-load recovery path covered by a deliberately broken fixture assembly, so that the one path the reference target cannot exercise is still tested.
40. As a plugin maintainer, I want the ECS additions verified live against the reference target and the steps recorded, so that the existing `ecs_*` convention is followed rather than quietly broken.
41. As a plugin maintainer, I want the entity-naming change called out as a behavior change, so that users of the shipped tools are not surprised by it.
42. As a user, I want the driving skill updated to describe one entity-naming rule, so that the documented inconsistency disappears along with the code that caused it.
43. As an agent whose call throws inside the game, I want the game's own exception type and message, so that I can tell a mistake in my call from a fault in the plugin.
44. As a plugin maintainer, I want that unwrapping to live where every tool reaches it, so that a tool added later reports in-game failures without reimplementing anything.
45. As an agent, I want a buffer the entity does not carry to be refused by name, so that I learn the state lives elsewhere instead of reading "empty" and abandoning the search.
46. As a user, I want the buffer tools to establish the buffer exists before asking for it, so that a mistyped element type costs me an error rather than the running game.
47. As a plugin maintainer, I want read paths to ask for read-only access, so that inspecting state never takes a write lock on the simulation.

## Implementation Decisions

### Packaging

Both capabilities ship as one feature commit. They are independent in code but form one story, and
release-please bumps both units of the plugin from any releasable commit under it either way.

### Entity naming, converged

`EntityManager.GetEntityByEntityIndex(int)` resolves a bare index to the live entity in one invoke.
This is the single rule for every ECS tool:

- A bare `index` resolves through that call.
- An explicit `index:version` is built client-side and verified with `Exists`, unchanged.

Consequences, all deliberate:

- The query-scan branch that resolved a bare index on the component tools is deleted, along with the
  entity-search helper on the ECS module that only it used.
- The buffer tools stop defaulting the version to `1`.
- This is a behavior change on four shipped tools. Pre-1.0 it is a minor bump; it must be called out
  in the commit and reflected in the driving skill.

An out-of-range index throws an in-game `NullReferenceException`, so the index is range-checked
client-side against `EntityManager.HighestEntityIndex()` first and reported as a real error.

### `ecs_list_components`

A new ECS tool, narrow by design: it reports the shape of an entity and composes with the existing
read tools for anything deeper.

- Component types come from `EntityManager.GetComponentTypes(entity, Temp)`, one invoke returning
  the whole `ComponentType[]`.
- Each type's name comes from `ComponentType.GetManagedType().FullName`. The debug-name paths are
  unusable: `ComponentType.ToString()` returns null and `EntityManager.Debug.GetEntityInfo` returns
  placeholder names, because the reference target's build strips the `TypeManager` debug-name table.
- Kind is classified from the `ComponentType` flags — `IsBuffer`, `IsSharedComponent`,
  `IsChunkComponent`, `IsZeroSized`, `IsManagedComponent` — into one of: component, tag, buffer,
  shared, chunk, managed. `TypeManager.GetTypeInfo(typeIndex)` returns the whole `TypeInfo` struct
  inline in a single invoke and is the cheaper source if the per-flag round trips prove costly.
- `IsEnableable` types additionally report enabled state via the non-generic
  `EntityManager.IsComponentEnabled(entity, componentType)` overload. Measured on the reference
  target, roughly one entity component in five is enableable, so this is a minor cost.
- `values=false` by default. When true, unmanaged components inline their field values at the
  existing formatting depth; kinds no tool can read report their kind in place of a value; a read
  that throws records the error on that entry alone and does not fail the call.
- `follow` takes `<componentTypeFullName>` with an optional `:<field>` suffix, mirroring the
  `<systemTypeFullName>:<method>` shape `ecs_query`'s `label` already uses. With no suffix, the
  component's single Entity-typed field is chased; several candidates fail with the field list, in
  the style of the existing missing-field error. Exactly one level is followed, so cycles cannot
  arise. The followed block carries the target entity, the component and field that led there, and
  its own component list under the same `values` setting.
- No game-specific type name appears in the implementation; the caller supplies the component to
  chase, keeping the plugin's genericity boundary intact.

### Capability probing

The Entities version is not readable from assembly metadata — `Unity.Entities` reports
`Version=0.0.0.0`. An embedded `com.unity.entities@<version>` string exists and is recorded in the
ECS solutions note, but probing the exact members the code calls is stricter than any version
inference and is what this feature uses.

`Invoker` gains non-throwing lookup variants alongside the existing throwing ones. Three call sites
consume them, each degrading by omission and saying so in the response:

- `IsComponentEnabled` absent → no enabled state reported, with a note that the capability is
  unavailable on this target.
- `ComponentType.IsEnableable` absent → same.
- `GetEntityByEntityIndex` absent → bare indices are refused with an error asking for an explicit
  version.

Probe results are cached for the session.

### Type search

`find_types` gains a `search` parameter. `fullName` keeps its exact debuggee-side lookup and its
low cost; the two parameters have distinct cost profiles and distinct descriptions, so an agent
cannot stumble into the harvest by typo. The "type not found" error on `fullName` names `search` as
the way forward.

Acquisition, verified live against the reference target:

- The domain's assemblies come from the client-side domain mirror; per assembly, one invoke of
  `string.Join(separator, assembly.GetTypes())` returns every type name as a single string.
- Measured: 165 assemblies, 38191 types, roughly 1.5–2 MB in total, no load failures, and a full
  harvest on the order of a second or two.
- An assembly whose `GetTypes()` throws `ReflectionTypeLoadException` is recovered rather than
  skipped: the thrown object is reachable as a mirror on the invocation failure, and one further
  invoke joins its partial `Types` array. Null slots render as empty entries and are dropped
  client-side. The assembly is marked partial in the response. This matters because a mod compiled
  against a different game version is both the likeliest thing to throw and the thing its author
  most wants searchable.

Caching, in a session-scoped type catalog owned by the session:

- Every search spends one invoke fetching the assembly-name list and harvests only assemblies not
  already held. Steady-state cost is that single invoke.
- The catalog dies with the session, so a reattach re-harvests.

Matching:

- .NET regex, unanchored `IsMatch`, so a plain fragment still behaves as a substring search.
- Case-insensitive by default, consistent with `fullName`'s case-insensitive resolve. `(?i)` and
  friends remain available for explicit control.
- Matched against the full type name. Results whose short name (after the last dot) matches rank
  above results that matched only in the namespace.
- A match timeout on the order of a second turns a pathological pattern into a clean error.
- An invalid pattern surfaces the runtime's regex message verbatim.
- Type names carry regex metacharacters of their own — nested types render with `+`, generics with
  a backtick-arity and bracketed arguments — so the parameter descriptions state that `search` is
  for authored patterns and `fullName` is for pasted names.

Bounding, mirroring `ecs_query`'s established contract:

- Exact `count` always; listing capped by `limit`, default 50; `omitted` reported.
- Each hit reuses the existing type-description shape (full name, assembly, kind), so no new result
  type is introduced.
- `members=true` is refused when the match count exceeds a small threshold, with an error naming
  `fullName` or a tighter pattern.
- No assembly-scoping parameter. Namespaces correlate with assemblies in practice, and `count` /
  `omitted` guide narrowing. It stays a small addition if field use demands it.

### In-game exception messages

`EvalInterpreter.Fail` already walks the exception chain for an `InvocationException` and reads the
thrown mirror's type and `Message`. It is the evaluator's private helper, so `ToolGuard` — the path
every other tool takes — forwards `InvocationException.Message`, which is the framework default and
names nothing.

The unwrap lifts to where every tool reaches it, either as a typed failure `Invoker` raises or as the
same walk performed in `ToolGuard`. The evaluator's richer report (statement source, position,
locals) then builds on it instead of carrying a second copy.

### Buffer presence

`Ecs.GetBuffer` invokes `EntityManager.GetBuffer<T>` with `isReadOnly: false`, so both buffer tools
take write access on a component the entity may not carry. Where the collections safety checks are
compiled out, the call does not throw: it returns a `DynamicBuffer<T>` over unowned memory, reporting
`length 0` and degrading the entity store until the process dies. Read-only access on the same
absent types is harmless, which is what isolates the access mode as the mechanism.

The precondition moves ahead of the invoke, shared by both tools, and `ecs_get_buffer` drops to
read-only access it never needed. `ecs_buffer_edit`'s empty-buffer guard stops standing in for a
presence check it was never able to perform, since it runs after the invoke it would have to prevent.

### Matching conventions elsewhere

`signatureContains` on the debug tools stays a case-insensitive substring. Formatted signatures are
dense with parentheses and brackets, and the natural narrowing idiom would become an unterminated
group under regex semantics. The process-name filter stays a prefix match. The convention recorded
in the skill is: authored patterns are regexes, pasted fragments are substrings.

### Documentation

The driving skill loses the passage telling agents to harvest names offline and the passage
documenting the entity-naming inconsistency; it gains the one entity rule, the search workflow, the
archetype-dump workflow including the two-call follow idiom, and the note that a harvest freezes the
game briefly on first search.

Everything else this feature learns routes by the root convention in "Where knowledge goes". The
entity rule and the capability probe are each owned by the one function that implements them, so
they are explained there and nowhere else. What the live probing was expensive to establish — how
the by-index lookup behaves off the end of the entity store, what a free slot answers — extends the
ECS solutions note.

## Test Seams

### The type catalog (new seam)

The session-scoped catalog owning harvest, cache, and search, exposed on the integration fixture
through an accessor mirroring the existing debug-controller accessor, and driven against the real
Mono debuggee.

Behavior verified through it: regex matching against full names with the case-insensitive default;
short-name-match ranking; exact count against limit and omitted; an invalid pattern surfacing the
runtime's regex message; a pathological pattern hitting the match timeout; incremental refresh, by
loading an assembly into the debuggee mid-session and confirming a later search sees its types while
spending one invoke; and partial-load recovery, using a fixture assembly whose dependency is
deliberately absent so `GetTypes()` throws and the recovered partial list is asserted.

Prior art: the eval-session and debug-toolset integration suites drive the SDB library's public
surfaces through the same fixture. The fixture's discipline carries over unchanged — one debuggee
per suite run, skip rather than fail when no Mono runtime resolves, tests own what they mutate, and
the traps recorded in the Mono fixture solutions note.

### `Invoker` capability probing (existing seam)

The non-throwing lookup variants are verified in the same suite: a member that exists resolves, a
member that does not returns absent without throwing. This is already-tested public surface, not a
new seam.

### No seam for the ECS work

`ecs_list_components`, the kind and enabled annotations, `follow`, and the entity-naming convergence
are live-verified against the reference target, following the convention every existing `ecs_*` tool
sets: the ECS module has no automated coverage because the integration debuggee has no Entities.
The verification steps are recorded in the commit. The MCP tool classes stay untested by the same
convention — they are thin attribute wrappers over the SDB library.

## Out of Scope

- Reading managed (class) `IComponentData`. It is listed and marked, never read; the object-based
  `EntityManager` APIs remain unreachable over mirrors and belong to the injected-helper tier.
- Dedicated shared-component and chunk-component read tools. Those kinds are listed and marked; the
  path to their values remains `eval`.
- Recursive or multi-component `follow`.
- An assembly-scoping parameter on search.
- Converting `signatureContains` or the process-name filter to regex.
- Stubbing Entities shapes in the integration fixture to give the ECS module automated coverage.
- Any growth of the eval grammar. The contract stays frozen.
- Cross-platform discovery, the injected in-game helper, and GameObject/MonoBehaviour tools — all
  separate roadmap facets.

## Further Notes

Every measurement in this spec was taken live against the reference target during the design
interview: the assembly and type counts, the absence of load failures, the archetype size and
enableable proportion on a sample entity, the null debug names, and the behavior of
`GetEntityByEntityIndex` at and beyond the valid range.

Two premises stated early in that interview were disproved by those probes and are recorded here so
they are not re-derived: there is no cheap way to resolve a bare index (there is —
`GetEntityByEntityIndex`), and `GetTypes()` load failures are common enough to design around as the
primary case (they did not occur at all on the reference target, though the mod scenario keeps the
recovery path worth having).

The harvest runs as invokes on the game's main thread, so the first search of a session freezes the
game for its duration. That is normal for every tool here, but it is the longest single freeze the
plugin will introduce, and the skill should say so.
