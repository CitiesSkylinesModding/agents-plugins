# A building watchlist panel

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want a small panel in the game UI that keeps a watchlist of buildings. When I have something selected in the city, a button on the panel adds it to the list; if what I have selected isn't a building, the panel should tell me so instead of adding it. Each entry shows the building's name and whether it has power right now, and has a button to drop it from the list.

## Checklist

### The entry payload crosses the wire through a writer it declares

- Falls in: the entry type handed to a `ValueBinding` or `GetterValueBinding` is a plain class, record or struct with public fields or properties and no `IJsonWritable` implementation, and no `IWriter` is passed or registered for it.
- Avoids: the entry type implements `IJsonWritable` with a `Write(IJsonWriter)` of `TypeBegin`, `PropertyName`/`Write` pairs and `TypeEnd`, or the binding is given an explicit `IWriter` for it, or a `RawValueBinding` writes the entries by hand.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Colossal.UI.Binding/Colossal.UI.Binding/ValueWriters.cs:78

### A list mutated in place is pushed explicitly

- Falls in: the list behind a `ValueBinding<List<T>>` is mutated with `Add`, `Remove` or `RemoveAll` and then handed back to `Update` as the same instance, or returned by a getter as the same instance.
- Avoids: after the mutation the system calls `TriggerUpdate()` on the binding, or passes a freshly built list or array to `Update`, or uses a `RawValueBinding` whose `Update()` has no comparison.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Colossal.UI.Binding/Colossal.UI.Binding/ValueBinding.cs:39

### The power status is re-read while the panel is open

- Falls in: the binding carrying the status is a `GetterValueBinding` passed to `AddBinding`, or the status is read once when the entry is added and nothing in the system reads it again.
- Avoids: the status binding is passed to `AddUpdateBinding`, or the system's `OnUpdate` re-reads the status and calls `Update()` or `TriggerUpdate()` on the binding when it changed.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Game/Game.UI/UISystemBase.cs:42

### The system is gated to the game mode its panel exists in

- Falls in: the `UISystemBase` subclass declares no `gameMode` override, so it keeps the default of every mode.
- Avoids: the system overrides `gameMode` to return `GameMode.Game`.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Game/Game.UI/UISystemBase.cs:63

## Roots

- decompile
- install
