# Exposing an API to other mods

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

How a mod offers something other mods can call without either side taking a compile-time reference on the other.
`mod-compatibility` owns why that reference is never taken.

The provider is a `public static class` in a namespace a consumer can hard-code — the **bridge** — and **every signature on it uses only types both sides already reference**: engine types, game types, primitives.
A parameter or return naming a type your own mod declares forces the consumer to construct one reflectively, which is possible and unreadable; a signature over an `Entity`, an `EntityQuery` or an assembly is one a consumer can call with values it already has.

Two moves complete that surface without leaking your types.

- **Hand out your own component types as `ComponentType` values** from a static getter, so a consumer can query for them without ever naming them.
- **Invert the direction.** Expose a `TryRegister(SomeGameBaseType)` that another mod's object calls to register _itself_ into a list you keep, guarded against a double add.
  The consumer then pushes rather than being pulled, and the two mods can be provider and consumer of each other at once.

**A duck-typed extension point needs no bridge class at all.**
Walk the mod manager, take each mod's `IMod`-derived type, look for a `public static` method of an agreed name, and accept it only when the signature matches exactly.
The provider then needs to know nothing about you beyond a method name and a signature — no attribute, no interface, no reference.
Call the method you found from the deferred callback rather than from `OnLoad`: the mod-manager walk is correct there, but the mod whose static you are calling may not have loaded yet.

**Make a compile-time reference impossible rather than asking for it.**
`[Obsolete("...", true)]` on every bridge member turns any compile-time use into a hard compiler error, while leaving reflection untouched: the attribute is the compiler's and is never consulted at runtime.

**The consumer half is a facade, and its shape is the part to copy.**

- Resolve the bridge type once, lazily, behind an initialized flag — and clear that flag on the mod-set change event, since the other mod may load after you or be enabled mid-session, and a facade that once resolved to absent otherwise answers absent for the life of the process.
  Re-attempt from there anything you push through the facade once, a `TryRegister` of your own object included.
  `mod-compatibility` owns that event and which answers are safe to refill from it.
- Cache each `MethodInfo`, resolved with an **explicit parameter-type array** rather than by name alone — that is what disambiguates overloads.
- Route every call through one invoke helper that logs and returns a neutral value when the member is missing or the invocation throws.
- Expose a single `IsAvailable` so no caller ever branches on reflection state itself.
- Open every public method with the availability check, so the whole facade degrades to no-ops when the other mod is absent.
