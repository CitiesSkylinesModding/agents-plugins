# A power outage panel that misses repeats, freezes its count and costs frames

## Kind

bug report

## Prompt

I'm making a Cities: Skylines II mod. It adds a panel I can toggle open that lists every building in the city that currently has no electricity, with the number of them in the panel header, and when a building newly loses power while the panel is open, a short banner at the top of the panel names it. The code is written and the panel shows up with the list in it, but three things are off. A building that keeps flickering on and off gets the banner the first time it goes dark and never again. The number in the header stays at whatever it was when I first opened the panel, even as the list under it keeps changing. And in my big city the frame rate drops noticeably as soon as the mod is enabled, whether the panel is open or closed.

## Checklist

### The banner crosses as an event, not as a value

- Falls in: the per-outage notification is a `ValueBinding<T>` or `GetterValueBinding<T>` holding the latest outage, given each new one through `Update` or a getter, and the frontend reacts to that value changing with `useValue` or `useValueOnChange`.
- Avoids: the notification is an `EventBinding<T>` whose `Trigger(value)` fires once per outage and which the frontend subscribes to through `bindEvent(group, name).subscribe(listener)`; or a `ValueBinding<T>` whose payload carries a per-outage sequence number or whose constructor is given an always-false `EqualityComparer<T>` as `comparer:`.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Colossal.UI.Binding/Colossal.UI.Binding/ValueBinding.cs:39

### The throttle is not a `GetUpdateInterval` override

- Falls in: the `UISystemBase` subclass overrides `GetUpdateInterval` (with or without `GetUpdateOffset`) and relies on it to space out its building scan.
- Avoids: the system declares no `GetUpdateInterval` override; any spacing of the scan is done by a `UIUpdateState` (`UIUpdateState.Create` then `Advance()` in `OnUpdate`), by a frame or time counter the system keeps itself, or not at all.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Game/Game/UpdateSystem.cs:186

### The scan runs only while the panel listens

- Falls in: the system's `OnUpdate` runs the building scan unconditionally, with nothing testing whether any binding has a subscriber.
- Avoids: `OnUpdate` returns before the scan when a binding of the panel's reports `active == false` or `observerCount == 0`, or the scan is started and stopped from a binding subclass's `OnSubscribe` and `OnUnsubscribe` overrides.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Colossal.UI.Binding/Colossal.UI.Binding/EventBindingBase.cs:16

### The system's `OnUpdate` keeps the binding pump alive

- Falls in: the system registers a binding through `AddUpdateBinding` and overrides `OnUpdate` without calling `base.OnUpdate()`.
- Avoids: the `OnUpdate` override calls `base.OnUpdate()`, or nothing is registered through `AddUpdateBinding` and every binding is pushed from the system's own code with `Update(...)` or `TriggerUpdate()`.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Game/Game.UI/UISystemBase.cs:42

## Roots

- decompile
- install
