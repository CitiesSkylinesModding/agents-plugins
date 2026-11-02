# Moves on a running game

Verified against game version 1.6.2f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

## Holding the camera still

**Block the `Camera` input map.**

```csharp
var b = Game.Input.InputManager.instance.CreateMapBarrier("Camera", "agent-freeze");
b.blocked = true;
```

The undo is `b.Dispose()`.
A later eval reaches the barrier through the map's internal `barriers`, a set shared with the game's own barriers, so dispose only the entry whose `name` reads `agent-freeze`:

```csharp
var m = Game.Input.InputManager.instance.FindActionMap("Camera");
var l = System.Linq.Enumerable.ToList<Game.Input.InputBarrier>(m.barriers);
l[0].name  // Each index in turn; then l[i].Dispose() on the match.
```

All camera input, edge scrolling included, is gated on the map's `enabled`; `CameraController.inputEnabled` looks like the switch and nothing on that class reads it.
A click still acts, since the tool map stays live.
Source: `src/Game/Game/CameraController.cs` (what the flag gates), `src/Game/Game.Input/ProxyActionMap.cs` (the barrier).
(VOLATILE: the `Camera` map name — `Game.Input.InputManager.kCameraMap`; the read sites — `Game.CameraController`.)
