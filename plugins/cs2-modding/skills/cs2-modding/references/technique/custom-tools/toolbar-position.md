# Contending for a prefab from the toolbar

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

Read this when your tool must claim a prefab kind a vanilla tool already claims.
A tool reached from a mod's own UI or a hotkey needs none of it: [`custom-tools`](custom-tools.md) states the walk and the `null`/`false` answer that keeps such a tool out of the contest.

The list is built by append, not by registration: the tool base class adds each tool to `ToolSystem.tools` from its own `OnCreate`, and it resolves the systems it needs before it appends.
Creating a system runs that system's `OnCreate` synchronously, inside the caller's, so a tool pulled in by another tool's `OnCreate` is appended first — the default tool ahead of every other, since the tool base resolves the tool system and the tool system creates the default tool from its own `OnCreate`.
**So there is no vanilla order to hold, only a live list to read.**
A tool appended from `OnLoad` lands behind every tool already constructed and never sees a prefab first, and any mod constructing one early shifts everything after it.
Source: `src/Game/Game.Tools/ToolBaseSystem.cs` (the append at the end of `OnCreate`, after the systems it resolves) and `src/Game/Game.Tools/ToolSystem.cs` (the default tool created from the tool system's own `OnCreate`).

(VOLATILE: `ToolSystem.tools` being a mutable `List<ToolBaseSystem>` rather than a read-only view — the tool system's list property, and the tool base class's `OnCreate` append.)

**Take the slot of the one tool you must precede, rather than the front of the list.**
Read the position back and reinsert at it, from `OnCreate`, immediately after the base has appended you:

```csharp
protected override void OnCreate()
{
    base.OnCreate();

    ObjectToolSystem objectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();

    m_ToolSystem.tools.Remove(this);
    m_ToolSystem.tools.Insert(m_ToolSystem.tools.IndexOf(objectTool), this);
}
```

A position stated relative to another tool needs no race to win, and that is why `OnCreate` is the right hook: another mod inserting itself at index 0 later does not stop you preceding the object tool.
`GetOrCreateSystemManaged` rather than an existing-only lookup is what makes the index safe: every vanilla tool is constructed before any mod's `OnLoad`, but another mod's tool is constructed when that mod loads, which may be after you — and an existing-only lookup on one that has not been constructed yet hands back a `null`, `IndexOf` a `-1`, `Insert` an out-of-range index, and `OnCreate` an exception that fails the whole mod.
That guard does not extend to a type that may not exist at all: naming one in a generic call is a compile-time reference, so a game version that removed it fails your `OnCreate` before either lookup runs, and a `try`/`catch` around the call never gets to run either.
Reach it by name instead — `Type.GetType` into the non-generic `GetOrCreateSystemManaged(Type)` — and [`mod-compatibility`](../mod-compatibility/mod-compatibility.md) owns the isolation that makes the failure catchable.
Source: `src/Unity.Entities/Unity.Entities/World.cs` (the creating lookup, its non-generic overload, and the existing-only one that returns `null`).

**Index 0 is the answer to one question, and it ships bound to its condition.**
Reach for the front only when no single vanilla tool can be named as the one to precede — the prefab kind is claimed by more than one, or by a tool you cannot identify — and then return `true` from `TrySetPrefab` only while your tool is already active:

```csharp
public override bool TrySetPrefab(PrefabBase prefab)
{
    return m_ToolSystem.activeTool == this && prefab is ObjectGeometryPrefab;
}
```

That gate is what makes index 0 cost the tools behind it nothing.
The walk reaches your tool first, it declines every prefab it was not already handling, and the vanilla tool behind it claims as usual; only once the player has put your tool in charge does it start intercepting.
A tool at index 0 that returns `true` for prefabs it does not own hijacks the toolbar for every other tool in the game.
Source: `src/Game/Game.Tools/ToolSystem.cs` (the walk that stops at the first tool to claim).

Negotiating a list position with another mod that wants the same one is [`mod-compatibility`](../mod-compatibility/mod-compatibility.md).
