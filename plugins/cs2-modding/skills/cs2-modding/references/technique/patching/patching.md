# Patching

Verified against game version 1.6.0f1.

How to change the behaviour of code you did not write, when the game offers no seam for the change you want.

Adding code of your own — a system, a phase registration, a fork of a vanilla system — is `mod-lifecycle-and-ordering`, and it is what you should be doing instead most of the time.
This reference covers the other case: a vanilla method whose behaviour has to differ, reached by rewriting it at runtime.

**Harmony is the runtime that does it**, and it is the only one this ecosystem has, so its vocabulary — prefix, postfix, injected parameter, patch id — is the vocabulary below.
`cs2-mod-project` owns the package id and the version every mod agrees to pin; do not choose one per project.

## Patching is the exception here, not the default

Three independent readings say the same thing, and none of them is a majority.

**Practice, in the mods read closely.** Nine of twenty-two repositories apply any patch at all, and a tenth declares one it never wires up.
Where they do patch, most patch narrowly: four of the nine touch three vanilla methods or fewer.
The rest do not, and the spread is wide enough that no single number describes it — a tail of three sits above a dozen targets each.

**Prevalence, across the mods a player actually has installed.** Of the code mods in one machine's downloaded-mods cache, roughly a fifth ship Harmony beside their assembly.
That is a count of files shipped rather than a reading of code, so it can only over-count: a mod can declare the dependency, never call it, and ship Harmony anyway, because the build deploys the whole output directory (`cs2-mod-project`).
So about a fifth of code mods carry the runtime, and fewer than that use it.

**The published modding wiki ranks it third.** Its guidance for refreshing runtime values after a prefab edit gives three remedies in order — trigger the player action that makes the game's own job run, then copy that job and run it yourself, then patch — and attaches the tradeoff to the third: patching may be easier than finding the hook, and is brittle on game patch days.
Its tool guidance mentions patching once more, for suppressing a vanilla tool's UI activation, with the same caution attached: only while your own tool is active.

(VOLATILE: every count in this section — the mod corpus, at the root the record `cs2-modding-setup` owns, and the installed-mods cache under the user-data path the toolchain names.)

One sample is small and hand-picked, the other is unselected but only counts files, and they agree — which is the strongest thing that can be said at this size.
What survives is the ordering, not the ratio: reach for a patch after the four alternatives below, not before.

## The four alternatives, in the order to try them

The first three are used at scale by mods that ship zero patches; the fourth avoids _adding_ a patch rather than avoiding the dependency, since three of its four idioms are Harmony's own API.

**One: insert a system.** The ordinary way to add behaviour, and the reason the ecosystem patches so little.
`mod-lifecycle-and-ordering` owns it entirely.
The negative is the part that belongs here: **a behaviour reachable from a phase your own system can occupy does not need a patch.**

**Two: disable a vanilla system and register your fork in its slot.**

```csharp
updateSystem.World.GetOrCreateSystemManaged<Game.Simulation.SomeVanillaSystem>().Enabled = false;
updateSystem.UpdateBefore<MySubstituteSystem, Game.Simulation.SomeVanillaSystem>(SystemUpdatePhase.GameSimulation);
```

**Anchor the fork against the system it replaces, rather than with `UpdateAt`.**
Position within a phase is registration order and every mod registers after all of vanilla, so `UpdateAt` puts the fork at the end of the phase instead of in the slot the original held — where it reads state the systems that used to follow it have not produced yet.
`GetOrCreateSystemManaged` also creates the system when the world does not already have it, rather than failing, so naming a type the game no longer registers disables nothing and reports nothing; `mod-lifecycle-and-ordering` owns both rules.

Whole mods are built this way — a substitute zoning check behind its own tagging systems, a substitute lane system registered ahead of the vanilla one, a substituted geometry system — with no patches anywhere.
Substitution and patching are not alternatives at the level of a mod, only at the level of a behaviour: one mod disables roughly a dozen simulation and UI systems and still carries the widest patch set of any read.

**Three: rewrite a vanilla system's own `EntityQuery` from outside**, so stock code skips your entities without any of its code changing.
Two independent mods carry the same routine near-verbatim, and neither needs a patch for it.
The shape is fixed:

1. Reflect the private query field off the target system instance.
2. Guard on `originalQuery.GetHashCode() == 0`, which is how a system whose `OnCreate` has not run yet is detected.
3. Call `EntityQuery.GetEntityQueryDescs()`, and append your own `ComponentType` to each desc's `None`, skipping descs that already carry it.
4. Reflect `ComponentSystemBase.GetEntityQuery(params EntityQueryDesc[])` — it is `protected internal`, so a system can call it on itself but not on another system's instance — and **invoke it on the target system**, so the new query is owned by the right system.
5. Write the result back into the field, and call the public `RequireForUpdate(query)`.

`ecs-in-this-game` owns `EntityQueryDesc` and what `None` means to a query.

**Four: cache a reflection accessor** rather than patching just to reach a private member.
Four idioms exist and they differ in what each access costs:

| Accessor                                                                                        | Cost per access                                                                  | Where it works                                                                                                                      |
| ----------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `Traverse.Create(obj).Field<T>("name").Value`                                                   | Allocates a traverse, then `FieldInfo.GetValue`; only the field lookup is cached | Anywhere; also writes with `.SetValue`                                                                                              |
| A `FieldInfo` resolved once in a static constructor, closed over by a `Func<TInstance, TField>` | One `GetValue`, no lookup                                                        | Anywhere; the common shape when a mod needs several fields off one system                                                           |
| `AccessTools.FieldRefAccess<TInstance, TField>("name")`                                         | A field load — the emitted method body is `Ldarg_0; Ldflda; Ret`                 | Class instances only, and `TField` must be exactly the field's type for a value-type field; returns a `ref`, so it reads and writes |
| The `___fieldName` injected parameter                                                           | Nothing extra                                                                    | Inside a patch body only                                                                                                            |

**`AccessTools.Field` walks base types**, so a lookup naming a derived class still resolves a field the base declares — which is why an accessor built against the wrong class works, and why a miss means the field is nowhere in the hierarchy rather than merely on the wrong type.

`performance-and-memory` owns what these cost in a hot path.

## What gets patched, and why those surfaces

The patch targets read fall into four groups, and the groups are about **what kind of seam is missing** rather than about subject matter.
They describe what was found rather than what exists, so a target fitting none of them tells you nothing either way — check it rather than concluding the game left a seam there.

- **A tool's per-frame raycast and snap configuration.** The reason this group exists is structural: the raycast system calls `InitializeRaycast()` on the _active tool only_, so a mod widening what a vanilla tool can hit has nowhere else to stand.
- **A value the game publishes to its own UI.** Most of these producers are private and reached only through a delegate the system captured in its own `OnCreate`, so there is no seam by construction: the binding is registered, the system is a concrete type, and the producer is not virtual. Some are neither private nor non-virtual, so check yours before concluding a patch was the only route.
- **A value the game asks for and then acts on.** Consumed immediately and rewritten through `ref __result` — usually a boolean forced the other way, sometimes a returned object.
- **A simulation value, or the managed method that schedules a job.** Time, climate, upkeep, wind, prefab refresh.

The surfaces recorded in each group, and what to make of your own target's absence from them: [what the corpus was found patching](what-gets-patched.md).

`custom-tools` owns the raycast masks, the `InitializeRaycast` contract and the `GetRaycastResult` pair, and is where a tool mod should start; this reference owns the patch discipline that applies to any of them.
`prefabs-and-assets` owns the prefab-refresh question the game's own documentation answers with a patch only as its third remedy.

## Prefixes, postfixes and injected parameters

**A prefix returns `void` or `bool`, and nothing else.**
Any other return type fails at patch time, not at call time.
Returning `false` means "do not run the original".

**A prefix returning `false` also skips later prefixes — but only those that could have skipped it themselves.**
A prefix is wrapped in the skip check only when it could affect the original, which is true when it returns `bool`, or takes any parameter that is `out`, `ref`, or a reference type; `__instance`, `__originalMethod` and `__state` are exempt from that test.
So a void prefix taking only value-type arguments always runs, and a void prefix taking `ref Something` stops running once someone ahead of it returned `false`.

**Postfixes are never guarded.**
A postfix runs even when a prefix suppressed the original, including a prefix from a mod that has never heard of yours.

**Ordering is priority descending, then registration order.**
Priority attributes and explicit before/after attributes exist in Harmony.
Nothing in this ecosystem uses them, and mod load order is dictionary iteration order, so **cross-mod patch order on a shared target is unspecified** — the discipline below is what mods rely on instead.

### The injected parameters

Declare a parameter with one of these names in your patch method and the patcher supplies it.

| Name               | What it is                                                                                                              |
| ------------------ | ----------------------------------------------------------------------------------------------------------------------- |
| `__instance`       | The instance the original was called on                                                                                 |
| `__originalMethod` | The `MethodBase` being patched                                                                                          |
| `__args`           | All arguments, as `object[]`                                                                                            |
| `__result`         | The return value; take it `ref` to rewrite it                                                                           |
| `__state`          | A value you pass from your prefix to your postfix; the prefix must take it `ref` or `out`, or its write lands on a copy |
| `__exception`      | The exception, in a finalizer                                                                                           |
| `__runOriginal`    | Whether the original is still going to run                                                                              |
| `__0`, `__1`, …    | A positional argument, by index                                                                                         |
| `___fieldName`     | A private field on the original's declaring type                                                                        |

Three of them carry traps.

- **`__instance` on a static original is `null`, silently.** Nothing at patch time warns; a body that dereferences it throws at call time.
- **`__result` is type-checked at patch time**, both ways — asking for one on a `void` method fails, and so does declaring the wrong type.
  Taking it `ref` in a **postfix** rewrites the return value; taking it `ref` in a **prefix that returns `false`** is how you supply a return value without running the original.
- **`___field` resolves against the original's declaring type and does walk base types**, and throws at patch time when it misses.
  It is the cheapest way to reach a private field from inside a patch, because there is no accessor to build.
  Two traps ride with it, neither reported at patch time: **writing through it needs a `ref` parameter**, since a by-value declaration reads the field and discards the assignment; and on a **static** original the instance load is emitted anyway, so it reaches for argument zero as though that were the instance — the `__instance` trap above, except that it corrupts rather than nulls.

**`__state` is keyed by your patch class**, not by the patched method, so a prefix and a postfix share state only when they live in the same class.

### Two prefix shapes, and only one of them composes

**Skip and supply:** write `ref __result`, return `false`.
Total control, and it takes the method away from every prefix behind you.

**Rewrite and continue:** take the parameter `ref`, mutate it, return `true`.
Two independent prefixes written this way on the same method both run, in either order, and neither can suppress the other — which is why this is the shape to reach for when the change can be expressed as an argument edit.

Transpilers, finalizers and reverse patches exist in Harmony and nothing in this ecosystem uses them; the practice is prefixes and postfixes.

## Disambiguating an overload

`[HarmonyPatch]` takes a parallel `Type[]` and `ArgumentType[]`, and the pair is turned into a signature.

**`ArgumentType.Ref` and `ArgumentType.Out` are the same case** — both produce `type.MakeByRefType()` — so the two spellings are interchangeable and either matches an `out` parameter.
`ArgumentType.Pointer` produces a pointer type and `Normal` leaves the type alone.
This also covers `in`, which is a by-ref parameter in metadata; `typeof(T).MakeByRefType()` in the `Type[]` does the same job by hand, and the patch body receives such a parameter as `ref`.

```csharp
[HarmonyPatch(
    typeof(ToolBaseSystem),
    "GetRaycastResult",
    new Type[] { typeof(Entity), typeof(RaycastHit) },
    new ArgumentType[] { ArgumentType.Out, ArgumentType.Out })]
```

Stacking one attribute per facet — type, then name, then the two arrays — reaches exactly the same patch information as folding them into one call.

**Disambiguation is required more often than it looks**, because the game's tool and time systems routinely give one name more than one signature, split on a single extra parameter — an added `double renderingFrame`, or a wider list.
Do not scope the check by accessibility: the pair is sometimes a public override against a private or protected overload carrying the logic, and sometimes two public overloads.
Where a name carries more than one signature at all, the `Type[]` is load-bearing: without it the lookup asks for the name alone, which is ambiguous across overloads and throws at patch time rather than picking one.
That failure is loud, which makes it the good case — the quiet one is a `Type[]` that matches a real overload other than the one you meant.

**Name the target with a string literal unless a public member of that exact name exists.**
Most of the methods worth patching are `protected` or `private`, so `nameof(SomeSystem.SomeMethod)` will not compile from your mod's assembly, while a string literal binds because the patcher looks the name up with non-public binding flags.
A literal is not free: misspell it and the lookup returns nothing and Harmony throws at patch time, which is exactly the failure `nameof` exists to prevent.
So prefer `nameof` wherever a public member of that name exists, and check the spelling wherever you cannot.

**Name the type that declares the method, not a subclass that inherits it.**
The attribute's own lookup is declared-only, so a method inherited and not overridden is not found on the derived type — patching `GetActualSnap` through `[HarmonyPatch]` means naming the tool base class, even when the tool you care about is a subclass of it.
This is the opposite of the field lookups above, which do walk base types, and the asymmetry is easy to generalise the wrong way.
Resolving the target yourself escapes it, which is one more reason to reach for `TargetMethod()` or an imperative patch when the declaring type is awkward to name.

**A property is patched through its accessor, never through the property name**, since `nameof(SomeType.Thing)` names the property and there is no method by that name.
Two spellings reach the accessor.
The metadata name as a literal — for a property `Thing` that is `"get_Thing"` or `"set_Thing"`, a lowercase accessor prefix followed by the property's own casing.
The lookup is case-sensitive, so that spelling has to be exact.
Or the attribute's own `MethodType.Getter` or `MethodType.Setter` alongside the property name, which is rename-safe where the property is public.

**A prefix mirroring an `out` parameter must assign it on every branch**, and which spelling you chose decides whether the compiler will remind you.
Mirror it as `out` and C# forces the assignment; mirror it as `ref`, which the patcher accepts identically, and nothing is forced — so an early-out branch that assigns nothing compiles, and on a `return false` path the caller reads whatever the slot already held.
Assign on every branch either way: `entity = Entity.Null; hit = default; return true;` or its equivalent.

## A Burst-compiled job is not what you patch

**A `[BurstCompile]` method still takes a patch, but the patch wraps a trampoline.**
The managed body of such a method is one line dispatching to a generated invoker, which fetches a native function pointer and calls it, falling through to the real managed body only when Burst is disabled.
So a prefix or postfix on it runs, and **you can read and rewrite the arguments and the result but you cannot change the logic**, because the logic is in the native image the fallback path never reaches.

For a Burst-compiled **job**, the substitution point is not in C# at all: a job's entry is registered through an `extern` reflection-data call, and the point where Burst swaps in the compiled body is native.
(UNVERIFIED: that a patch on a `[BurstCompile]` job's `Execute` has no effect while Burst is enabled — patching one vanilla job's `Execute` with a logging postfix and observing whether it fires, with Burst on and then off, would settle it.)

**So the rule is: replace the job, not its body.**
Patch the managed method that _schedules_ the job — the last managed instruction before the schedule — and schedule your own job instead.
The worked shape, from the two mods that do it:

- Resolve the private target by explicit signature, through `TargetMethod()` when an attribute cannot express it.
- Return `true` immediately for every case you are not replacing, so the vanilla path is untouched for everything else.
- **Rebuild every component type handle and lookup by hand off `__instance`**, because those are per-system state the vanilla method would have refreshed.
- Reach protected base members — `SystemBase.Dependency` above all — through a `MethodInfo` cached in a static field, since a static patch method in another assembly cannot touch them.
- **Assign the scheduled handle to `ref __result` and return `false`, rather than completing the job**, so the caller's temporary allocations still outlive the work.

`ecs-in-this-game` owns jobs, handles and the Burst story itself.

## Lifecycle: apply in `OnLoad`, and know what unpatching is for

**Apply from `IMod.OnLoad`.**
The mod object is constructed without running any constructor, so field initialisers on the mod class never run and a Harmony instance cannot be built there.
Every referenced assembly is loaded before the mod's own type is touched, so Harmony is in the process by the time `OnLoad` runs — and a mod whose reference to it cannot be resolved never reaches `OnLoad` at all, failing with a missed-dependency state naming what was unresolved.

**Prefer `PatchAll(typeof(MyMod).Assembly)` over `PatchAll()`.**
The parameterless form reads the _calling frame's_ assembly off a stack trace, so it patches whatever assembly the call happens to sit in rather than the one you meant — a helper class works only because it is in the same assembly, and a shared bootstrap in another one patches nothing of yours.

**Imperative patching is the escape hatch for a generic patch body.**
Resolve the method yourself and apply it with `harmony.Patch(methodInfo, postfix: new HarmonyMethod(...))`, throwing when the resolution misses.
Reach for it when one patch body has to serve several types, since an attribute cannot name a generic parameter — though a declarative patch can also reach a closed generic by returning it from `TargetMethod()`, which is the lighter option when the set of types is fixed and small.

**Removal filters by patch id, and the filter has a hole at its default.**
`UnpatchAll(harmonyID)` walks every patched method and removes only the patches whose owner matches, so **passing a wrong id removes nothing and reports nothing** — a published mod ships this exact bug, having passed the literal name of its id field instead of the field.
**Passing no id is the opposite and far worse.**
The parameter defaults to null and the owner test returns true for every patch when it is, so `harmony.UnpatchAll()` strips every installed mod's patches from the process, not just yours.
That is the shortest spelling and the one the API invites; always pass your own id.

**`OnDispose` runs in exactly two situations**, and neither is the one authors expect.
It runs at process shutdown, where the AppDomain is going away regardless.
And it runs for one mod when that mod's own load throws — which is the case where unpatching genuinely buys something, since a mod that patched and then failed halfway through `OnLoad` would otherwise leave live patches behind a mod that is not there.
It does **not** run when a code mod is disabled mid-session: that path requires a restart and leaves the mod loaded, and re-initialisation skips any mod not in the initial state, so a mod is never unloaded and re-patched inside one run.

**The game's own patch inventory always finds nothing.**
It exists and it is thorough, but it runs inside engine initialisation _before_ the mod manager is constructed, so nothing has been patched by the time it looks.
It does print: a modding-runtime line goes to the log unconditionally, before it goes looking, so finding that line tells you the inventory ran rather than that nothing is patched.
`diagnostics` owns the habit that replaces it: log `harmony.GetPatchedMethods()` yourself, immediately after applying, which is the only way to see what is patched.

## Composing with another mod's patch

**Exactly one copy of Harmony is loaded per process, and it is not the one you pinned.**
The asset loader deduplicates executable assets by simple assembly name across every installed mod and loads a single winner, ordered by already-loaded first, then local, then version, then asset id.
That last key is a total tiebreak, so the winner is decided by the installed set rather than by load order — deterministic, and no less out of your hands for it.
So the copy every mod patches through may be one nobody compiled against, and it is not simply the highest version.
Nothing here is strong-named, so version binding is not enforced and no error is raised.
(UNVERIFIED: whether a mod compiled against one version of Harmony executes correctly against a different loaded copy — reading the loaded assembly version back from two mods shipping different copies, in a running game, would settle it.)
`mod-compatibility` owns what this means for a mod's dependency posture, and `cs2-mod-project` owns the pin every mod agrees to.

**Widen a shared flags field with `|=`, and treat a plain `=` as a bug.**
The case that makes this concrete is a postfix widening a vanilla tool's raycast masks: the vanilla method has already cleared that field for the frame, so every widening postfix is competing to put its own bits back.
`flags |= ...` composes; `flags = ...` discards whatever every other mod set that frame.
The choice is per branch rather than per mod — one published patch method does both, in different branches — so it is each assignment that has to be justified, not the file.

**Record whether _you_ were the one that set the flag, and filter only in that case.**

```csharp
[ThreadStatic] private static bool s_weSetTheFlag;

// In the postfix that widens:
s_weSetTheFlag = false;
if ((raycastSystem.raycastFlags & RaycastFlags.Markers) == 0)
{
    raycastSystem.raycastFlags |= RaycastFlags.Markers;
    s_weSetTheFlag = true;
}

// In the patch that narrows the results:
if (!result || !s_weSetTheFlag)
{
    return;
}
```

**What that buys is precise.**
Widening a filter and then narrowing the results is two halves of one transaction, and the narrowing half is only correct for the hits the widening half caused.
If the flag was already on — because the player enabled it, or another mod turned it on for its own purposes — an unconditional result filter vetoes hits that had nothing to do with your mod, and the symptom lands in the _other_ party, where nobody can diagnose it.
Recording the flag makes the patch all-or-nothing with respect to everyone else: it either owns the widening and owns the filtering, or it owns neither.
It also survives a vanilla change — if a future build sets that flag itself, the patch degrades to a no-op instead of breaking the tool.

The same guard is what makes the unguarded-postfix rule harmless: when another mod's prefix suppresses the original, your postfix still runs, and an ownership flag is the thing that stops it acting on a frame that was never yours.

The `[ThreadStatic]` costs nothing at 1.6.0f1 because both ends of that pair run on the main thread, and it is not free to copy: it makes same-thread execution a _requirement_, so the same shape around a pairing whose halves are not on one thread reads back the per-thread default and silently never filters.
What the pairing does require is that the set and the read happen in the same frame on the same call chain.

**One mod in twenty-two does this**, and it is the discipline to copy rather than the norm to expect.
`custom-tools` states the same rule for the raycast case specifically, where it bites most often.
