# Bulldoze the hidden paths in a normal game

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want the bulldozer to be able to remove the invisible paths the game lays down inside lots and around buildings during a normal game, the hidden pedestrian and vehicle lanes you otherwise only get at in the editor. Only the paths: the other invisible helper objects, like the spawn points buildings use, have to stay out of the bulldozer's reach, and everything else the bulldozer does should keep working as it does now.

## Checklist

### The Harmony instance is created in `OnLoad`

- Falls in: an instance field initializer on the mod class builds the Harmony instance, such as `private readonly Harmony m_Harmony = new Harmony("...")`, so `OnLoad` or `OnDispose` dereferences a field that was never assigned.
- Avoids: `new Harmony(...)` is called inside `OnLoad` (or from a static initializer), and the result is what `PatchAll` and `UnpatchAll` are called on.
- Reference: cs2-modding/references/technique/patching/patching.md
- Source: src/Game/Game.Modding/ModManager.cs:122

### The widening adds to the frame's raycast flags rather than replacing them

- Falls in: the patch on the bulldozer's `InitializeRaycast` is a prefix returning `false` that re-states the vanilla setup with its own assignments, or a postfix assigning `raycastFlags = ...` (a fresh set of flags with `Markers` among them).
- Avoids: a postfix that runs after the vanilla method and ORs the extra flag onto the field, `raycastFlags |= RaycastFlags.Markers`, leaving every other bit as the frame set it.
- Reference: cs2-modding/references/technique/patching/patching.md
- Source: src/Game/Game.Tools/BulldozeToolSystem.cs:1506

### The hit filter acts only on hits the widening caused

- Falls in: the patch that rejects marker-object hits runs its test whenever the raycast returned a hit, with nothing recording whether this mod turned the markers flag on this frame.
- Avoids: the widening patch records that it set the flag (a static bool set only when the flag was not already present) and the rejecting patch returns early unless that record is set.
- Reference: cs2-modding/references/technique/patching/patching.md
- Source: src/Game/Game.Tools/BulldozeToolSystem.cs:1482

### The hit-filter patch names the type that declares the signature it patches

- Falls in: the attribute names `BulldozeToolSystem` for the `GetRaycastResult(out Entity, out RaycastHit)` signature (the patch method mirrors `entity` and `hit`), which only `ToolBaseSystem` declares.
- Avoids: the attribute names `ToolBaseSystem` for that signature, or names `BulldozeToolSystem` for a `GetRaycastResult` overload it overrides itself (the `out ControlPoint` ones).
- Reference: cs2-modding/references/technique/patching/patching.md
- Source: src/Game/Game.Tools/ToolBaseSystem.cs:542

### The `GetRaycastResult` overload is pinned by signature

- Falls in: `[HarmonyPatch(typeof(...), "GetRaycastResult")]` with no parameter types, or `AccessTools.Method(type, "GetRaycastResult")` with no type array, on a type where the name carries more than one signature.
- Avoids: the attribute carries a `Type[]` for the overload (with `ArgumentType.Out` or `MakeByRefType()` for the `out` parameters), or `TargetMethod()` resolves it by explicit parameter types, or the type named carries a single `GetRaycastResult` signature.
- Reference: cs2-modding/references/technique/patching/patching.md
- Source: src/Game/Game.Tools/ToolBaseSystem.cs:555

## Roots

- decompile
