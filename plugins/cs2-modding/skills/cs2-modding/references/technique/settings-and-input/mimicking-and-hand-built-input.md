# Mimicking a vanilla binding, and building input by hand

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

Mimicking copies a vanilla binding's control path onto the mod's own action and keeps copying it, which is how a mod uses a button the game reserves and still follows the player's rebinds.
It has a declarative form and an imperative one, and past both sits the hand-built map, for an input the binding attributes cannot express at all.
A tool's own apply, secondary apply and cancel need none of this; the entry file states why, and `custom-tools` owns that path in full.

## The declarative form

Declaratively, mimicking is `[SettingsUIBindingMimic(string map, string action)]` on a `ProxyBinding` property.
The resolver looks the named action up, **requires it to be built-in**, then requires a composite for the property's device and a binding for its component; any of those four failing returns false and the property silently falls back to its declared default key.
When it succeeds, the binding is created with the vanilla binding's path, original path and modifiers copied in, and registration additionally attaches a watcher on the **source** binding that re-copies path and modifiers into the mod's binding every time the player rebinds the vanilla one.

That watcher is the whole value of the technique: a plain default key goes stale the moment the player rebinds.
The map name comes from the input manager's constants, of which the tool map and the shortcuts map are the two a mod normally wants.

(VOLATILE: the vanilla map names a mimic can name — the input manager's map constants.)

**Pair every mimic with `[SettingsUIHidden]`.**
A mimicked binding must not be offered for rebinding, because the next watcher callback overwrites whatever the player set.

An axis is mimicked with two properties sharing one action name, one carrying the positive component and one the negative, each with its own mimic attribute — the same grouping rule that builds an axis out of two bindings applies unchanged.

## The imperative form

**The imperative form is the same behaviour, reachable at runtime and revocable.**
`ProxyBinding.Watcher` has a public constructor taking a binding and a change callback, so a mod can build the watcher itself, apply the copy once immediately, and dispose it later.
The helper the settings class uses internally is not reachable, but that constructor is.
The one-shot variant — look the vanilla action up, copy its path and modifiers onto your own binding, set the binding back, and stop there — is simpler and does **not** follow later rebinds, which is the difference that matters.

The imperative form is what lets mimicking be a _player setting_: a bool with a `[SettingsUISetter]` that registers or disposes the watchers, and `[SettingsUIDisableByCondition]` on the mod's own binding properties against the same flag, so those rows grey out while the mimic is on.

## Building the map by hand

**When the attributes cannot express the input at all, the map is built by hand.**
The route is to clone the vanilla composite that already has the shape you need, flip its built-in flag to false, replace its modifiers, and register the result through the input manager's action-adding entry point, which is internal and therefore reached by reflection.
Pass the mod's own `id` as the map name and the resulting actions are reachable through `settings.GetAction(...)` exactly like a declared one, and their locale keys sit under the mod's own map alongside the rest.
The case that forces this is a scroll-wheel action, since no mouse binding enum member names the wheel.

## Diagnosing a mimic that did nothing

**A vanilla target is built-in unless its own asset entry says otherwise, so the built-in requirement is rarely what rejects one.**
The flag is declared in code with a default of true, and the only thing that can clear it on a vanilla composite is the input asset the game deserializes — which is data rather than C#, so no grep of the source settles it either way.
The one site in the game that clears it is a mod's own key-binding registration, and that bounds the code path rather than the asset.
Reach for the mimic attribute's other three failure modes first: a map or action name that resolves to nothing, no composite for the property's device, and no binding for its component.
