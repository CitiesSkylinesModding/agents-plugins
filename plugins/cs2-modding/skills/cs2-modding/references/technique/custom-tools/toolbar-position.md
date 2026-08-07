# Contending for a prefab from the toolbar

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

Read this when your tool must claim a prefab kind a vanilla tool already claims.
A tool reached from a mod's own UI or a hotkey needs none of it: `custom-tools` states the walk and the `null`/`false` answer that keeps such a tool out of the contest.

The list is built by append, not by registration: the tool base class adds each tool to `ToolSystem.tools` from its own `OnCreate`, so the order is the order the systems are _constructed_ in.
That is not the order they are registered in, because constructing one system pulls in every system it resolves — the default tool is appended before any other, since the tool system creates it from its own `OnCreate`.
**So there is no vanilla order to hold, only a live list to read.**
A tool appended from `OnLoad` lands behind every tool already constructed and never sees a prefab first, and any mod constructing one early shifts everything after it.

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

Negotiating a list position with another mod that wants the same one is `mod-compatibility`.
