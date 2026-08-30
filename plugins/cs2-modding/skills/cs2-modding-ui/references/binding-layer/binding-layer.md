# The binding layer

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Nearly every line here names a game type or a method on one, and the wire strings are checkable only against the shipped frontend bundle, so without the tree the kinds and their lifecycle still hold but no signature, log text or `__Type` spelling below can be confirmed.
`cs2-modding-setup` provisions it.

Carrying a value, a call or an event between a mod's C# and the game's frontend: the binding kinds, where they register and die, how a type crosses the wire, and what arrives at the far end.
The C# side up to the call that crosses is this reference's; the far end is shown only as the receiver of what C# wrote.
The frontend as source — the module registry, React, injection — is [`frontend-and-injection`](../frontend-and-injection/frontend-and-injection.md)'s, and building the UI project is [`ui-build-and-devloop`](../ui-build-and-devloop/ui-build-and-devloop.md)'s.

The binding kinds all live in one small assembly, `Colossal.UI.Binding`, under `src/Colossal.UI.Binding/Colossal.UI.Binding/` in the decompile; the systems that register them and the gates around them (`UISystemBase`, `GameMode`, `UIUpdateState`) are in `Game`.
A mod project reaches it only through an explicit `<Reference Include="Colossal.UI.Binding">` in its own csproj — the toolchain's props put `$(ManagedPath)` on the assembly search path, so no hint path is needed, but they declare no game assembly (`cs2-mod-project` owns the csproj and its reference rules).

## Three kinds, and a container

- **Push** — C# owns a value, the frontend observes it: `ValueBinding<T>`, `GetterValueBinding<T>`, `RawValueBinding`, `StackBinding<T>`, the three event bindings, and the two map bindings.
- **Trigger** — the frontend calls, C# runs, nothing comes back: `TriggerBinding` with zero to four typed arguments, and `RawTriggerBinding` handing the caller the `IJsonReader`.
- **Call** — the frontend calls, C# returns a value into a promise: `CallBinding<TResult>` and its arities up to five arguments.

`CompositeBinding` is not a kind: it holds bindings and forwards `Attach`, `Detach` and `Update` to them, which is also what the game's own registry is made of.

Every binding is a path, computed once in `BindingBase` as `group + "." + name`, and `ToString()` returns it.
Nothing validates a group, nothing checks uniqueness, and dots inside `name` are legal: two bindings sharing a path both register their handlers and both answer every subscribe.
**Use the mod's own id as the group.**
That convention, and nothing in the layer, is what keeps two mods off one path; the game's own groups are short nouns (`l10n`, `time`, `options`, `tool`) with no prefix to collide with (VOLATILE: the vanilla group names — the `k…Group` constants and the info sections' `group` overrides under `src/Game/`).

The constructor signatures the kinds take, from the files of the same name in that namespace:

| Kind | Constructor |
| --- | --- |
| `ValueBinding<T>` | `(group, name, T initialValue, IWriter<T> writer = null, EqualityComparer<T> comparer = null)` |
| `GetterValueBinding<T>` | `(group, name, Func<T> getter, IWriter<T> writer = null, EqualityComparer<T> comparer = null)` |
| `RawValueBinding` | `(group, name, Action<IJsonWriter> writerDelegate)` |
| `TriggerBinding` / `<T1..T4>` | `(group, name, Action<…> callback, IReader<T1> reader1 = null, …)` |
| `RawTriggerBinding` | `(group, name, Action<IJsonReader> callback)` |
| `CallBinding<…, TResult>` | `(group, name, Func<…, TResult> callback, IReader<T1> reader1 = null, …)` |
| `RawMapBinding<K>` | `(group, name, Action<IJsonWriter, K> onRequestUpdate, IReader<K> keyReader = null, IWriter<K> keyWriter = null)` |
| `GetterMapBinding<K, V>` | `(group, name, Func<K, V> getter, IReader<K> keyReader = null, IWriter<K> keyWriter = null, IWriter<V> valueWriter = null, EqualityComparer<V> comparer = null)` |
| `EventBinding` / `EventBinding<T>` | `(group, name)` / `(group, name, IWriter<T> writer = null)` |
| `RawEventBinding` | `(group, name)` |
| `StackBinding<T>` | `(group, name, IWriter<T> elementWriter = null)` |

(VOLATILE: the signatures — the constructors in `src/Colossal.UI.Binding/Colossal.UI.Binding/`.)

## Registration and teardown

`UISystemBase : GameSystemBase` (`src/Game/Game.UI/UISystemBase.cs`) is the route every UI system takes, and it is short enough to read whole.
`AddBinding(IBinding)` appends to the system's own list and to `GameManager.instance.userInterface.bindings`; `AddUpdateBinding(IUpdateBinding)` does that and also appends to the system's update list, which `OnUpdate` walks calling `Update()`.
`OnDestroy` removes every binding from the registry, which detaches it.

The registry is a `CompositeBinding` typed `IBindingRegistry` (`src/Game/Game.SceneFlow/UserInterface.cs`), and its `AddBinding` attaches the new binding immediately when the view is already attached.
A `UISystemBase` is therefore a convenience, not a requirement: anything holding that registry can register directly, as the game's own top-level groups do.
A mod publishing a binding outside a system needs the registry reference and something that calls `RemoveBinding` on teardown.

A minimal system registering the three kinds, created and put on the `UIUpdate` pump from `OnLoad` by `updateSystem.UpdateAt<MyUISystem>(SystemUpdatePhase.UIUpdate)`:

```csharp
public partial class MyUISystem : UISystemBase
{
    private ValueBinding<int> m_Count;

    protected override void OnCreate()
    {
        base.OnCreate();

        AddBinding(m_Count = new ValueBinding<int>("MyMod", "count", 0));
        AddBinding(new TriggerBinding<int>("MyMod", "setCount", value => m_Count.Update(value)));
        AddBinding(new CallBinding<int, bool>("MyMod", "isEven", value => value % 2 == 0));
    }
}
```

**A binding added through `AddUpdateBinding` is pumped by its own system's `OnUpdate` and by nothing else.**
That pump is the whole body of `UISystemBase.OnUpdate`, so a subclass overriding `OnUpdate` calls `base.OnUpdate()` or every binding it added that way silently stops updating, and a system obtained with `GetOrCreateSystemManaged` rather than `UpdateAt` never has `OnUpdate` called at all.
`UISystemBase` only ever calls the registry's `AddBinding`, so the registry's own update pump — the one `GameManager.UpdateUI()` drives — reaches only the groups `UserInterface`'s constructor builds itself: `LocalizationBindings`, `AppBindings`, `OverlayBindings`, `AudioBindings`, `UserBindings`, `InputBindings`, `InputActionBindings`, `InputHintBindings` and `ParadoxBindings` (VOLATILE: that set — the constructor in `src/Game/Game.SceneFlow/UserInterface.cs`).
Source: `src/Game/Game.UI/UISystemBase.cs`, `src/Game/Game.SceneFlow/UserInterface.cs`.

**A UI system registered at `UIUpdate` runs `OnUpdate` every frame, and a `GetUpdateInterval` override there is dead.**
`UIUpdateSystem` is registered at `MainLoop` and its whole body calls the interval-less `Update(SystemUpdatePhase.UIUpdate)` overload, which reads no interval and no offset; the interval-aware overload serves only the simulation phases.
Dead for throttling only: registration still reads the override for every phase and throws on a value that is not a power of two.
A throw out of a UI system's `OnUpdate` is caught per system and logged at Critical as a system update error; the frame continues and the remaining systems still run (VOLATILE: the log text — `src/Game/Game/UpdateSystem.cs`).
Source: `src/Game/Game.UI/UIUpdateSystem.cs`, `src/Game/Game/UpdateSystem.cs`, `src/Game/Game.Common/SystemOrder.cs`.

Frame order is world update first — `MainLoop`, and inside it `UIUpdate` — then `UpdateUI()`, which advances the view and hands the queued binder events to the page.
A value pushed during `UIUpdate` reaches JS later in the same frame.

**Teardown removes and detaches, and disposes nothing.**
`UISystemBase.OnDestroy` removes each binding from the registry; the registry's `DisposeBindings` runs only from `UserInterface.Dispose`, at process end, and the world is destroyed before that, so a mod's binding has already left the list it walks.
`IDisposable` on a binding registered through a system therefore never fires — the game's localization group gets disposed only because the `UserInterface` constructor's own groups are never removed — so unhook an event subscription from the system's `OnDestroy`.
Source: `src/Game/Game.UI/UISystemBase.cs`, `src/Colossal.UI.Binding/Colossal.UI.Binding/CompositeBinding.cs`, `src/Game/Game.SceneFlow/GameManager.cs`.

**A UI system is never destroyed by a game-mode change, and neither is the view.**
`userInterface` is constructed once, right after the world and long before systems and mods are created, and released only when the game terminates; the world is destroyed only at quit.
Bindings registered from an `OnLoad`-created system stay registered and attached for the whole process across loading a save, returning to the menu and entering the editor.
The one thing that does detach is navigation: `UserInterface.OnNavigateTo` detaches the whole composite and `OnReadyForBindings` re-attaches it, which resets every observer count to zero.
Source: `src/Game/Game.SceneFlow/GameManager.cs`, `src/Game/Game.SceneFlow/UserInterface.cs`.

## The game-mode gate

`UISystemBase.gameMode` defaults to `GameMode.All`, and `OnGamePreload` sets the system's `Enabled` to whether the incoming mode intersects it.
`GameMode` is a `[Flags]` enum in `src/Game/Game/GameMode.cs` with `None`, members for the game, the editor, the main menu and an "other" state, plus `GameOrEditor` and `All` combinations (VOLATILE: the member set and values — that file).
Override it on a system whose panel exists in one mode only; the game's own editor panels, info sections, toolbar and selected-info systems all do.

**What the gate stops is `OnUpdate`, and nothing else.**
A disabled system's bindings are still in the registry and still attached, so a subscribe still reaches them and a value binding still pushes its current value on subscribe.
A `GetterValueBinding` on a disabled system runs its getter on the first-ever subscribe — `m_ValueDirty` starts true — and answers every later subscribe from that cached value, since only an unobserved pump re-marks it dirty and a disabled system pumps nothing; it never updates again until the mode changes back.
`RequireForUpdate<T>` behaves the same way: a missing singleton silences the pump without unregistering anything.
A throw out of a system's own `OnGamePreload` override lands in the same disabled state — `GameSystemBase` catches it, logs at Error and sets `Enabled` false — until the next preload runs the override again.
Source: `src/Game/Game.UI/UISystemBase.cs`, `src/Game/Game/GameSystemBase.cs`, `src/Colossal.UI.Binding/Colossal.UI.Binding/GetterValueBinding.cs`.

Without the gate a system's `OnUpdate` and getters run in the main menu and the editor too, against a world with no city in it, and whatever they assume about a loaded city fails there — into the per-system catch above, every frame.

## Choosing a push binding

`ValueBinding<T>.Update(newValue)` compares against the held value with the comparer and pushes only on a difference; `TriggerUpdate()` pushes unconditionally when observed.
Nothing polls it: an owner that forgets to call `Update` has a binding that is stale forever.

`GetterValueBinding<T>.Update()` calls the getter, compares against the previous value and pushes on a difference.
**The getter runs on every pump whether or not anything changed**, so its cost is paid every frame the binding is observed.
Unobserved, it marks itself dirty so the next subscribe re-reads rather than trusting the cache.
Its `TriggerUpdate()` re-sends the cached value without calling the getter unless that flag is set, so `Update()` is the only call that forces a fresh read.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/GetterValueBinding.cs`.

`RawValueBinding.Update()` has no comparison at all and writes the whole payload every time it is called.
The game's own default is push-on-change — `AddBinding` plus an explicit `Update()` from the owning system — with `AddUpdateBinding` the minority, and a raw binding almost never on the update pump.
**A `RawValueBinding` on the update pump serialises its entire payload into the binder every observed frame.**
The narrower write is its second event, `path + ".patch"`: `PatchBegin()` opens an event carrying a path array then a value, `PatchEnd()` closes it, and `RawValueBindingExtensions` writes the path argument; the frontend clones down that path and replaces the leaf, an empty path replacing the whole value.
`PatchBegin` asserts `attached`, and the assertion is live in the shipped assembly.
The game patches two ways: the production-company panel patches one field of one array element with an `[index, fieldName]` path on a plain raw binding, and the widget tree's `WidgetBindings` holds one raw binding for the whole children array and emits one patch per changed node, with the widget action triggers registered beside it — typed `TriggerBinding<IWidget, …>`s such as `invoke` and `setExpanded` taking the one shared `IReader<IWidget>` path resolver as their reader, and `setValue` the lone `RawTriggerBinding`.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/RawValueBinding.cs`, `RawValueBindingExtensions.cs`, `src/Game/Game.UI.InGame/ProductionCompanyUISystem.cs`, `src/Game/Game.UI.Widgets/WidgetBindings.cs`, `InvokableBindings.cs`, `ExpandableBindings.cs`, `SettableBindings.cs`, `Cities2_Data/Content/Game/UI/index.js`.

Three consequences of the comparer decide which to reach for:

- **`ValueBinding<T>` with a mutable reference type never pushes on mutation.**
  `EqualityComparer<T>.Default` on a class without `IEquatable<T>` is reference equality, so `Update(sameInstanceMutated)` compares equal and returns; `StackBinding<T>` mutates its list and then calls `TriggerUpdate()` directly for exactly this reason.
  Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/StackBinding.cs`.
- **A struct payload wants `IEquatable<T>`.**
  Without it the default comparer boxes and compares field by field reflectively; the game's time-settings struct declares it with a hand-written `Equals`, and most vanilla payload structs do not bother.
  Source: `src/Game/Game.UI.InGame/TimeUISystem.cs`.
- **An always-false comparer is the escape hatch for an opaque value.**
  An `EqualityComparer<T>` subclass — the abstract class, not the interface — whose `Equals` returns false, passed as the named `comparer:` argument since `writer` precedes it, forces a push on every `Update`, which is the supported hook for a payload recreated whole on every edit; the game itself passes no custom comparer anywhere, so there is no vanilla example to open.
  Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/ValueBinding.cs`.

**The engine's guard against every-frame pushes is dead code.**
`GetterValueBinding` declares a consecutive-update cap, a logged-warnings set and a check method, and nothing calls the check or reads the set; a binding that pushes on every consecutive frame does so in silence.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/GetterValueBinding.cs`.

The game's throttle for expensive UI work is `UIUpdateState` (`src/Game/Game.UI/UIUpdateState.cs`), an ordinary class a mod can construct: `Create(world, updateInterval)` holds a simulation-frame counter and `Advance()` returns true once the interval elapsed or `ForceUpdate()` was called.
That counter is `SimulationSystem.frameIndex`, which stops while the game is paused and runs up to four times faster at the top speed, so the game ORs a change signal in front of `Advance()` or calls `ForceUpdate()` on the event; a panel tracking non-simulation state throttled by `Advance()` alone goes stale for the whole pause.
The other lever is `active` and `observerCount`, both public on every event-based binding: skip the query entirely when nothing is listening, as the game's chirper and company panels do.

## What the far end does

`EventBindingBase`, the base of every push binding except the map ones and `StackBinding<T>` (a group wrapping a `ValueBinding<List<T>>`), registers two view events on `Attach` and computes a third name (VOLATILE: the verb strings — `src/Colossal.UI.Binding/Colossal.UI.Binding/EventBindingBase.cs`):

```
path + ".subscribe"    → OnSubscribe, observerCount++
path + ".unsubscribe"  → OnUnsubscribe, observerCount-- floored at zero
path + ".update"       → the event a push writes
```

`active` is `observerCount > 0`; `Attach` and `Detach` both reset the count.
`ValueBinding`, `GetterValueBinding` and `RawValueBinding` override `OnSubscribe` to push the current value at once, inside a try/catch that logs an error naming the path on the `UI` logger and swallows, and the map bindings do the same on their own subscribe verb.
**That catch exists only on the subscribe path**; a throw from an ordinary `Update` is not caught there.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/ValueBinding.cs`, `MapBindingBase.cs`.

A trigger, by contrast, is no push binding at all: it registers on the bare path with no verb suffix — `view.RegisterForEvent(path, …)` — and the frontend's `trigger(group, name, ...args)` is `engine.trigger("group.name", ...args)`, fire-and-forget, with no observer count to read.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/TriggerBinding.cs`, `Cities2_Data/Content/Game/UI/index.js`.

A push is `BeginEvent(updateEventName, 1)`, one written value, `EndEvent()`, and nothing is written when no observer holds the path, so an unobserved value binding costs one boolean test; `EventBinding` and `RawEventBinding` carry no such check.

The frontend's `bindValue(group, name, fallback)` composes the same strings plus a `.patch` one, registers handlers on update and patch, then triggers subscribe.
The subscribe round trip is synchronous: the trigger returns with the update handler already called once and the payload in hand, which is what lets the frontend's wrapper read its value on the next line.
Every subscribe fans the push out to every JS listener on that path, not only the new one, because `OnSubscribe` emits one `.update` event and the JS event bus broadcasts it; the binding wrapper's identity compare absorbs a repeated primitive and passes a repeated object payload through, deserialised fresh on every push, and the `useValue` hook's structural compare absorbs it from there.

**This layer logs nothing for a path nobody registered.**
At the raw engine layer the subscribe trigger returns normally, throws nothing, and no update ever fires.
At the `cs2/api` layer `bindValue` throws on first read of its value when no fallback was passed, with a message saying the update was not called after subscribe and asking whether the binding was added on the C# side; the map flavour names the key instead (VOLATILE: both texts — the frontend bundle's data-binding module).
With a fallback passed there is no throw either: the panel renders the fallback forever.
So a binding the C# side never registered — a typo in the group — surfaces only in the frontend, as that throw or that frozen fallback; an `OnCreate` that threw is logged by the mod loader instead, since `UpdateAt` creates the system synchronously and the throw fails the whole mod's `OnLoad` with a stack trace in `Modding.log` and no dialog.
The registry walk and the live read-back under `## Two things that look like bindings and are not` settle which end failed.
Source: `Cities2_Data/Content/Game/UI/index.js`.

**A payload whose `__Type` no component is registered for does not throw.**
The typed renderer substitutes a yellow-on-red box reading `Unknown element type <typeName>`, which is the visible signature of a `TypeBegin` string that does not match what the frontend expects (VOLATILE: the box text — the frontend bundle's typed renderer).
Source: `Cities2_Data/Content/Game/UI/index.js`.

## Map bindings: one key at a time

`MapBindingBase<K>` uses a different verb set from every other push binding (VOLATILE: the verb strings — `src/Colossal.UI.Binding/Colossal.UI.Binding/MapBindingBase.cs`):

```
path + ".subscribeMapEntry"     the frontend sends the key
path + ".unsubscribeMapEntry"   the frontend sends the key and a keepAlive bool
path + ".updateMapEntry"        C# sends the key, then the value
```

`OnSubscribe` bumps that key's observer count and pushes it; `OnUnsubscribe` drops the key only when the count reaches zero and the keep-alive bool is false.
`Update()` with no argument walks every observed key; `Update(K key)` pushes one.
`RawMapBinding<K>` re-serialises on every ask; `GetterMapBinding<K, V>` caches per key and compares with the comparer.
`Update(K key)` on a key nobody holds does nothing and logs nothing — the public paths check the observer count first, so the unsubscribed-key throw behind them is unreachable.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/GetterMapBinding.cs`, `MapBindingBase.cs`.

**Every map binding registers a second, ordinary binding under the same path.**
Its constructor builds a `RawValueBinding` on the same group and name that serialises the whole observed set as an array of key-value pairs, so the plain `.subscribe` verb on a map binding answers with the currently observed entries — an empty array when nothing is held, which a probe reads as an empty binding rather than as the keyed one it is.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/MapBindingBase.cs`.

The frontend's `bindMap` stringifies a key with a `JSON.stringify` over the key's own properties sorted by name and with `__Type` dropped, which is why an `Entity` works as a map key at all.

## Events, and subscription as a signal

`EventBinding` with no payload triggers the update event directly, with no observer check; `EventBinding<T>` writes its payload inside a `try`/`finally` so `EndEvent` always runs, and does nothing when unobserved.
`RawEventBinding` exposes `EventBegin()`/`EventEnd()` for a caller writing its own payload.
The frontend's `bindEvent` subscribes on the first listener and unsubscribes on the last.

**`observerCount` is public, and a binding can exist only to be counted.**
The game's simulation-pause barrier is an `EventBinding<bool>` that is never triggered: while any frontend component holds a subscription to it, the simulation speed is forced to zero every frame, and the camera and tool input barriers work the same way.
Source: `src/Game/Game.UI.InGame/TimeUISystem.cs`, `src/Game/Game.UI/InputBindings.cs`.

A mod-side subclass of `ValueBinding<T>` overriding `OnSubscribe`, `OnUnsubscribe` and `Detach` can own a resource — enable an input action, run a query — exactly while the frontend holds a subscription, which is the template for a panel that costs nothing while closed.

## Readers, writers, and how a custom type crosses

`ValueWriters` and `ValueReaders` are static registries with public `Register` overloads and a `Create<T>()` that throws when it cannot resolve.
`Create` resolves in a fixed order: registry hit, then `IJsonWritable`/`IJsonReadable` wrapped in `ValueWriter<>`/`ValueReader<>`, then array, then list, then dictionary, then an `ArgumentException` naming the type — the writer side matching any `IList<>`/`IDictionary<,>` implementer and the reader side only a concrete `List<>`/`Dictionary<,>`, so a custom collection writes but will not read.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/ValueWriters.cs`, `ValueReaders.cs`.

Registered out of the box, by both static constructors (VOLATILE: this set — those two constructors and the `MathematicsWriters`/`MathematicsReaders` and `UnityWriters`/`UnityReaders` classes they call): `bool`, `int`, `uint`, `float`, `double`, `string`; `int2`, `int3`, `int4`, `float2`, `float3`, `float4`, `quaternion`, `Vector2`, `Vector3`, `Vector4`, `Vector2Int`, `Vector3Int`, `Bounds1`, `Bounds2`, `Bounds3`, `Bezier4x3`; `Entity`, `Color`, `Color32`, `Keyframe`, `AnimationCurve`; and a reader only for `Keyframe[]`.

Four asymmetries in those registrations, each a trap:

**`long` and `ulong` have readers and no writers.**
`new ValueBinding<long>(group, name, 0L)` throws at construction while `new TriggerBinding<long>(…)` works.
The writer to pass is `LongWriter` (`ULongWriter` for `ulong`, identical encoding), which encodes two 32-bit halves as an array, index 0 the low bits and index 1 the high, because JavaScript numbers lose integers above 2^53.
The registered reader reads a plain number, while `LongReader` requires that array and throws on anything else, so the registered reader and the array writer disagree about representation.
Pair `LongWriter` with an explicit `LongReader` (`ULongReader` for `ulong`), or use neither.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/LongWriter.cs`, `LongReader.cs`, `ULongWriter.cs`, `ULongReader.cs`.

**Enums resolve to nothing on either side.**
`EnumWriter<T>` writes the int, `EnumNameWriter<T>` writes the member name, `EnumReader<T>` reads an int and casts; every one is passed explicitly, and the game does it both ways depending on what the frontend wants.
`EnumWriter<T>` and `EnumReader<T>` unbox through `int`, so an enum with any other underlying type — `: byte`, `: long` — throws `InvalidCastException` at runtime; `EnumNameWriter<T>` and an inline `DelegateWriter` casting directly work for any.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/EnumWriter.cs`, `EnumNameWriter.cs`, `EnumReader.cs`, `src/Game/Game.UI.InGame/TimeUISystem.cs`.

**`string` is nullable on read and non-nullable on write.**
The registered `StringWriter` writes the null and then throws, and since `ValueBinding.TriggerUpdate` has no try/catch around the writer, the throw escapes with `EndEvent()` never called; the value, array, list, collection and dictionary writers do the same write-then-throw on null — `ValueWriter<T>` being what `Create<T>()` resolves for your own `IJsonWritable` class.
The fix is `ValueWriters.Nullable(new StringWriter())`, which is what the game's own nullable string bindings pass.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/StringWriter.cs`, `ValueWriter.cs`, `src/Game/Game.UI/AppBindings.cs`.

**`Nullable` means two different things across the value/reference divide.**
`ValueWriters.Nullable<T>` and `NullableWriter<T>` are both constrained to classes, so a struct does not compile there and takes `ValueWritersStruct.Nullable<T>` instead, giving `NullableStructWriter<T> : IWriter<T?>`.
`ValueReaders.Nullable<T>` is unconstrained and its `NullableReader<T>` throws at construction for a value type, so there is no nullable reader for a struct at all.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/ValueWritersStruct.cs`, `NullableWriter.cs`, `NullableStructWriter.cs`, `NullableReader.cs`.

### Crossing with your own type

`IJsonWritable` is one method, `void Write(IJsonWriter writer)`; `IJsonReadable` is `void Read(IJsonReader reader)`.
Implementing the first makes `Create<T>()` resolve to `ValueWriter<T>`; implementing the second plus a public parameterless constructor resolves `ValueReader<T>`, which is constrained `where T : IJsonReadable, new()`.

`IJsonWriter` declares a `debugName` and these writing members (`src/Colossal.UI.Binding/Colossal.UI.Binding/IJsonWriter.cs`): `TypeBegin(string)`/`TypeEnd()`, `MapBegin(uint)`/`MapEnd()`, `ArrayBegin(uint)`/`ArrayEnd()`, `PropertyName(string)`, `WriteNull()`, and `Write` for `bool`, `int`, `uint`, `long`, `ulong`, `float`, `double` and `string`.
`JsonWriterExtensions` adds the `int`-taking begins, empty array and map, `string[]` and `int[]`, `Write<T>` over an `IJsonWritable` and over a nullable struct one, `WriteNullable<T>` for a class one, `IList<T>` and `IList<string>`, and three `IReadOnlyDictionary` overloads (VOLATILE: both member sets — those two files).

The canonical shape is one `TypeBegin`, a `PropertyName` and value pair per field, one `TypeEnd`:

```csharp
public struct MyPayload : IJsonWritable, IEquatable<MyPayload>
{
    public int count;
    public int limit;

    public void Write(IJsonWriter writer)
    {
        writer.TypeBegin(typeof(MyPayload).FullName);
        writer.PropertyName("count");
        writer.Write(count);
        writer.PropertyName("limit");
        writer.Write(limit);
        writer.TypeEnd();
    }

    public bool Equals(MyPayload other) => count == other.count && limit == other.limit;
}
```

On arrival, `TypeBegin(s)` becomes a `__Type` string property on the JS object, `MapBegin` a plain object with no `__Type`, and `ArrayBegin` an array.
`GetType().FullName` on a nested type yields the CLR `Outer+Inner` spelling and the `+` reaches the frontend verbatim; the `__Type` string is data, never reflection (VOLATILE: the `Outer+Inner` spelling the bundle switches on — the `LineVisualizerSection` `__Type` compares in the frontend bundle).

**The `__Type` string is a free-form contract and is often not a C# type name.**
Most of the game's literals are `<group>.<TypeName>` short forms, a few gathered in `src/Game/Game.UI/TypeNames.cs` and the rest inline in their owning file, and some name a namespace the game does not have: the prefab panel's number and string properties write `Game.UI.Common.*` tags while the types live in `Game.UI.InGame` (VOLATILE: every `__Type` string — the `TypeBegin` literals under `src/Game/`, matched against the scaffold's `bindings.d.ts`).
What a tag has to be is whatever the consuming React component switches on, and for the game's own components that is written down in the scaffold's `bindings.d.ts` as enum values and `const`s.
That file declares the wire tags the game's own React consumes through the public modules; it is not a census of what the C# side can emit, and a payload type absent from it is not absent from the game.
A mod's own component reads whatever `__Type` the mod's own writer emits, so for a private payload the string is the mod's to choose.
Source: `src/Game/Game.UI/TypeNames.cs`, `src/Game/Game.UI.InGame/IntProperty.cs`.

**A generic writable uses a literal for its `TypeBegin`.**
The wire's worked polymorphic example is the four localized elements: `LocalizedString` passes `GetType().FullName`, while the generic `LocalizedNumber<T>`, `LocalizedFraction<T>` and `LocalizedBounds<T>` hard-code `Game.UI.Localization.LocalizedNumber`, `…LocalizedFraction` and `…LocalizedBounds` (VOLATILE: those four tags — `src/Game/Game.UI.Localization/`).
They must, because the frontend dispatches on exact string equality — a `switch` over the four tag constants falling through to a literal `<INVALID TYPE>`, and the typed renderer's key lookup falling through to the unknown-element box above — so a generic `FullName`, whose backtick arity suffix and type-argument list make it a different string, matches nothing (VOLATILE: both fallbacks — the frontend bundle's localization dispatch and typed renderer).
Source: `src/Game/Game.UI.Localization/LocalizedString.cs`, `LocalizedNumber.cs`, `Cities2_Data/Content/Game/UI/index.js`.

`Entity` is the one payload every mod sends and it is pre-registered: a `Unity.Entities.Entity` tag with `index` and `version`, read back symmetrically; [`ecs-in-this-game`](../../../cs2-modding/references/technique/ecs-in-this-game/ecs-in-this-game.md) owns the type.

**A custom reader is any `IReader<T>` passed inline to the trigger that needs it.**
For an enum that is the shipped `new EnumReader<MyEnum>()`; for any other conversion a `new DelegateReader<T>((IJsonReader r, out T v) => …)` lambda; a class implementing `IReader<T>` is the same thing spelled longer, which is what the game's own service-budget reader is.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/EnumReader.cs`, `DelegateReader.cs`, `src/Game/Game.UI.Editor/EditorScreenUISystem.cs`, `src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs`.

## The request/response escape hatch

`CallBinding<…, TResult>` registers with `view.BindCall(path, …)` rather than an event, and its callback logs an error naming the path and then **rethrows** — the only kind that re-raises after logging, where every trigger binding logs and swallows (VOLATILE: the log texts — `src/Colossal.UI.Binding/Colossal.UI.Binding/CallBinding.cs`, `TriggerBinding.cs`).
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/RawCallBindingBase.cs`, `CallBinding.cs`.

At the far end `call(group, name, ...args)` is `engine.call` on the path: it allocates a request id, sends, and returns a promise.
A name with no C# handler rejects on the next animation frame with a message saying no handler is registered with that name (VOLATILE: the text — the frontend bundle's engine shim).

The game's own call bindings all answer the same shape of question: **a value the frontend cannot compute and does not want to cache** — is this prerequisite met, what index did the keyframe land at, what does this controller read right now.

**When it beats a trigger plus a value binding.**
A trigger plus a push gives an answer too, but the answer arrives with no correlation to the request, so two callers cannot tell whose answer they got, and an answer equal to the last one is deduplicated away by the comparer and never arrives at all.
`engine.call` correlates by request id and always resolves.
Against it: the callback runs on the main thread inside the view's advance like every other binding callback, so an expensive answer costs a frame, and the answer is a one-shot, never observed afterwards.
Source: `src/Colossal.UI/Colossal.UI/UIView.cs`, `src/Game/Game.SceneFlow/GameManager.cs`.
Reach for a push binding for state a panel displays, and for a call for a question a panel asks once.

## The helper you write yourself

The de-facto helper is three small classes, written from the shipped API alone.

1. A `UISystemBase` subclass with typed create overloads that fill in the mod id as the group, so a call site reads `m_Mode = CreateBinding("mode", MyMode.Single)` — once `ValueWriters.Register<MyMode>(new EnumWriter<MyMode>())` and `ValueReaders.Register<MyMode>(new EnumReader<MyMode>())` have run, since an enum resolves neither on its own.
2. A value holder wrapping a `ValueBinding<T>` behind a `Value` property whose setter calls `Update`, with an implicit conversion to `T` so it reads like a field.
3. The writer and reader route: implement `IJsonWritable`/`IJsonReadable` on each payload type and let `Create<T>()` resolve it, or call `ValueWriters.Register<T>(writer)` and `ValueReaders.Register<T>(reader)` once during `OnLoad`, before the `UpdateAt` that creates the system, so no call site ever passes one.

```csharp
public sealed class BindingValue<T>
{
    public ValueBinding<T> Binding { get; }

    public BindingValue(ValueBinding<T> binding) => Binding = binding;

    public T Value
    {
        get => Binding.value;
        set => Binding.Update(value);
    }

    public static implicit operator T(BindingValue<T> holder) => holder.Value;
}

public abstract partial class MyUISystemBase : UISystemBase
{
    protected BindingValue<T> CreateBinding<T>(string name, T initial, IWriter<T> writer = null)
    {
        var binding = new ValueBinding<T>(MyMod.Id, name, initial, writer);

        AddBinding(binding);

        return new BindingValue<T>(binding);
    }

    protected GetterValueBinding<T> CreateGetter<T>(string name, Func<T> getter, IWriter<T> writer = null)
    {
        var binding = new GetterValueBinding<T>(MyMod.Id, name, getter, writer);

        AddUpdateBinding(binding);

        return binding;
    }

    protected void CreateTrigger<T>(string name, Action<T> action, IReader<T> reader = null)
    {
        AddBinding(new TriggerBinding<T>(MyMod.Id, name, action, reader));
    }
}
```

`Register` is the supported route to a fallback for a type `Create` cannot resolve; reflecting into the registries' private dictionaries buys nothing `Register` does not give.
The registries are process-global and keyed on the `Type` alone, last write wins and nothing unregisters, so register only types your own mod defines and pass the writer at the call site for a game or shared type.

Three defects ride in the copies circulating, and the code above avoids each.

**A getter binding registered with `AddBinding` runs its getter on the first subscribe and never again.**
Nothing calls its `Update()`, so nothing re-marks it dirty: every later subscribe — a panel reopened, a second consumer — replays the value cached at the first; `AddUpdateBinding` is the only registration that pumps it.
Source: `src/Game/Game.UI/UISystemBase.cs`, `src/Colossal.UI.Binding/Colossal.UI.Binding/GetterValueBinding.cs`.

**A reflective reader that tests `type.IsAssignableFrom(typeof(IJsonReadable))` never calls a type's own `Read`.**
That asks whether the interface is assignable to the concrete type, which is false for every real payload; the test is `typeof(IJsonReadable).IsAssignableFrom(type)`, and the route above needs no test at all because `ValueReaders.Create<T>()` already resolves `IJsonReadable` second.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/ValueReaders.cs`.

**`type.GetElementType()` returns null for a `List<T>`.**
It is defined for arrays and pointers only; the game's own `ValueReaders.Create` builds the list reader from `type.GenericTypeArguments`, and a reflective list branch written on `GetElementType` cannot work.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/ValueReaders.cs`.

## Two things that look like bindings and are not

`ExecuteScript(string)` on the Cohtml `View` a `UIView` exposes hands a string of JavaScript to the page and forgets it: no return value, no reader, no writer, no path, no observer count, and nothing on the C# side learns whether it parsed.
It is the one C#-to-UI channel with no contract at either end; a mod that needs to change the page injects a module instead, which is [`frontend-and-injection`](../frontend-and-injection/frontend-and-injection.md)'s route.

`IDebugBinding` is the developer menu's inspector, not a registry: the menu publishes one chosen binding at a time.
The live set is walkable from C#, since `IBindingRegistry` extends `IBindingGroup` and its `bindings` enumerates every registered binding, each printing its path through `ToString()` — a nested `CompositeBinding` is a group of its own and walks the same way.
**No such walk exists from the frontend**: there the answer is the decompile plus the shipped bundle, and the sibling `coherent-gameface` plugin can read one known path back from the running game, which is the cheapest way to tell "the C# never registered" from "the React never subscribed" — calibrate it on the `l10n` group first, `l10n.locales` a value binding and `l10n.indexCounts` a map — never on an event binding, which pushes nothing on subscribe (VOLATILE: those paths — `src/Game/Game.UI.Localization/LocalizationBindings.cs`).
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/IBindingGroup.cs`, `CompositeBinding.cs`.
[`debug-menu`](../../../cs2-modding/references/technique/debug-menu/debug-menu.md) owns the menu itself and the `debug` group's binding table.

## What this reference hands to others

[`frontend-and-injection`](../frontend-and-injection/frontend-and-injection.md) owns the other end of every wire here: the data-binding module whose `bindValue`, `bindMap`, `bindEvent`, `bindTrigger` and `call` compose the strings above, the typed renderer that turns a `__Type` into a component and draws the unknown-element box when it cannot, and the injection that gets a mod's React onto the page at all.

`cs2-mod-project` owns the csproj half of the one build fact here — the manual reference to `Colossal.UI.Binding.dll` — and [`ui-build-and-devloop`](../ui-build-and-devloop/ui-build-and-devloop.md) the UI project that consumes the scaffold's `bindings.d.ts`.

[`localization`](../../../cs2-modding/references/technique/localization/localization.md) owns what goes into a `LocalizedString`; this reference owns the wire and the `l10n` group as its live probe.

This reference owns the widget-over-bindings transport — the raw binding, its patch event and the widget action triggers; [`settings-and-input`](../../../cs2-modding/references/technique/settings-and-input/settings-and-input.md) owns authoring a settings page onto it and [`debug-menu`](../../../cs2-modding/references/technique/debug-menu/debug-menu.md) the developer menu, and a mod adding a settings page never touches the transport directly.

[`units-and-formatting`](../../../cs2-modding/references/technique/units-and-formatting/units-and-formatting.md) owns what a number means once it crosses: the unit is written as a plain string beside the value, and the player's unit settings travel as a three-field getter binding.
It also owns the four localized-element types and what the frontend does with one on arrival, so what a `LocalizedNumber` renders as is decided there.

[`mod-lifecycle-and-ordering`](../../../cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) owns when a UI system may register — the registry exists before any mod loads, so `AddBinding` from `OnLoad` always finds it — and the `UIUpdate` phase where an interval override is dead.

[`performance-and-memory`](../../../cs2-modding/references/technique/performance-and-memory/performance-and-memory.md) owns the cost side: a getter on every observed frame, a raw payload re-serialised on every pump, the dead consecutive-push guard, and the `UIUpdateState` and observer-count levers that answer them.

[`custom-tools`](../../../cs2-modding/references/technique/custom-tools/custom-tools.md) is where most mod bindings land: a tool's options row is a `UISystemBase` publishing the tool's mode and reading the player's choice back through triggers; gate it to the game mode as the toolbar is.

[`diagnostics`](../../../cs2-modding/references/technique/diagnostics/diagnostics.md) owns where the failures go: every binding error in this layer is written to the `UI` logger obtained once on `BindingBase` — except the widget action bindings, which log a bad path through Unity's `Debug.LogError` — while a throw out of a UI system's `OnUpdate` is the update system's Critical line.

[`ecs-in-this-game`](../../../cs2-modding/references/technique/ecs-in-this-game/ecs-in-this-game.md) owns `Entity`, the pre-registered payload every mod sends, and the map key the frontend's key stringifier is built to handle.

Among the mechanics topics, [`simulation-time-and-units`](../../../cs2-modding/references/mechanics/simulation-time-and-units/simulation-time-and-units.md) has the smallest complete worked example in the game — the time system's settings struct and its pause barrier — and [`city-services-and-coverage`](../../../cs2-modding/references/mechanics/city-services-and-coverage/city-services-and-coverage.md) has the most complete, the service-budget system registering a raw value, a keyed map, and triggers with a hand-written reader side by side.
