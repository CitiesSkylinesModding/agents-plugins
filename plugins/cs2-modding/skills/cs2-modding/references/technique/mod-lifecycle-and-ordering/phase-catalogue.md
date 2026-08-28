# The phase catalogue

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

What lives in each of the 32 update phases, for choosing between two candidates the chooser in `mod-lifecycle-and-ordering` does not separate.
The names below are a phase's characteristic occupants rather than a full listing, and where a phase's purpose is stated it is inferred from what lives there: the enum carries no documentation and neither does any vanilla comment.
Where each phase sits in the frame, and what that position costs, is the phase tree in the entry file.

(VOLATILE: the per-phase occupant names below — the vanilla system-order class.)
Source: `src/Game/Game.Common/SystemOrder.cs` (every occupant and the band it is registered into), `src/Game/Game/SystemUpdatePhase.cs` (the phases themselves).

## Driven from the frame update

- **`MainLoop`** — the frame's spine, and the phase most of the others are driven from. A mod registering here lands after the phases `MainLoop` itself drives, but **before `Cleanup`, `LateUpdate` and `DebugGizmos`** — so the simulation, which `LateUpdate` drives, has not run for that frame when it fires.
- **`Raycast`** — the tool raycast system alone. First of the middle band in `MainLoop`, so nothing else registered with `UpdateAt` has run this frame; the front band has, which means the `EndFrameBarrier` has already updated and is shut from then until the system that reopens it, later in this same band. Where a mod's own raycast system goes; see `custom-tools`.
- **`PrefabUpdate`** — texture streaming, geometry asset loading, prefab and object initialisation, mesh, UI and zone initialisation. Driven every `MainLoop` frame, unconditionally — the gating is per system, each occupant carrying its own query requirement, so a mod system registered here gets an `OnUpdate` every frame and must do the same. Where prefab-shaping systems go.
  Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.
- **`PreTool`** — one vanilla occupant, driven immediately before `ToolUpdate`.
- **`ToolUpdate`** — the eleven vanilla tools plus upgrade-deletion, bracketed by the tool output barrier. The tool system enables the active tool immediately before driving this phase and disables it when the tool stops being active, which is why a tool system belongs here and not merely by convention: elsewhere it would still be enable-gated by the tool system but would run at the wrong moment.
  Source: `src/Game/Game.Tools/ToolSystem.cs`.
- **`ClearTool`** / **`ApplyTool`** — driven from the tail of `ToolUpdate` and mutually exclusive on the tool system's apply mode. `ApplyTool` holds the nine vanilla apply systems.
- **`PostTool`** — tool feedback, selection update, course splitting, sub-element deletion, map tiles.
- **`Deserialize`** — the largest phase after `GameSimulation`, and the only one whose three bands are used as a designed pipeline. Fires once per load. Its contents belong to `save-serialization`.
- **`PrefabReferences`** — primary and secondary prefab references plus two check passes. Reached from inside both `Deserialize` and `Serialize`.
- **`Modification1`** — the generation systems plus graph deletion: where entities get created from placement definitions. See `placement-definitions`.
- **`Modification2`** — edge, route and building initialisation; damage and destruction.
- **`Modification2B`** — cross-references and area geometry.
- **`Modification3`** — sub-object references, owner lookup, attachment, and network composition selection. The phase to anchor into for composition work.
- **`Modification4`** — modifiers, sub-net references, network geometry and lanes. Both are derived layers: change what they derive from and let the pipeline here regenerate them, forking the owning system only where the behaviour has no data seam; see `roads-and-traffic`.
- **`Modification4B`** — object emergence, lane references, secondary lanes, building state efficiency.
- **`Modification5`** — removal, the update-collection systems, the search trees and graph systems.
- **`ModificationEnd`** — instance counts, lane data, zone checking, validation, prefab application, notification triggers. The last chance to touch an entity before the frame's tool and render work.
- **`PreCulling`** — camera update, pre-culling, overlay infomodes, mesh colour, wind textures. Where per-instance colour work goes.
- **`UIUpdate`** — every vanilla UI system, and where UI work of your own belongs. That `UISystemBase` belongs here is convention rather than a constraint: the base class itself constrains no phase. See `binding-layer`.
  Source: `src/Game/Game.UI/UISystemBase.cs`.
- **`UITooltip`** — every vanilla tooltip system. Registering a tooltip system here is a hard requirement rather than a convention: a tooltip system in any other phase does nothing at all. `custom-tools` owns tooltips, and the mechanism behind the requirement. A mod that puts its tooltip system here and its other UI systems in `UIUpdate` reads like an inconsistency and is correct.
  Source: `src/Game/Game.UI.Tooltip/TooltipUISystem.cs`.
- **`Rendering`** — batch instances, the initialisation family, object colour, batch data, area rendering, visual effects. Runs after `UIUpdate` in the same frame.
- **`Serialize`** — path trimming and two pre-serialize wrappers in front, then prefab serialization begin and end, the serializer, and the writer. Vanilla registers **nothing** in the back band, so a mod's `UpdateAfter` here is the last thing to run before the save completes.
- **`Cleanup`** — audio, animation, batch upload, cleanup, culling and enabled-state completion. Driven after the UI update, at the very end of the frame update. Where a disposal system goes.

## Driven from the late update

- **`LateUpdate`** — five vanilla systems, the drivers of the simulation and rendering-completion phases among them. A mod's `UpdateAt` here lands after all five, the whole simulation included.
- **`PreSimulation`** — driven every frame, with no vanilla occupant. The only place to run exactly once per frame immediately before that frame's simulation steps.
- **`GameSimulation`** — by far the largest phase; the whole city simulation, and where `citizens-and-households`, `economy-and-companies` and `city-services-and-coverage` live. Runs 0–8 times per frame with the update-interval mask applied.
- **`EditorSimulation`** — time, climate, snow, wind, natural resources, fire, street lights: environment only, no city. A mod that must also work in the editor registers the same systems into both this and `GameSimulation`; that dual registration is the pattern `environment-and-pollution` needs.
- **`LoadSimulation`** — navigation and AI systems, run eight iterations per frame while the loading counter is positive — on the order of a thousand iterations for a new game. Everything registered here pays that multiplier.
- **`PostSimulation`** — the water system alone. Runs once per frame after the steps.
- **`CompleteRendering`** — driven after every GPU upload for the frame has completed.
- **`DebugGizmos`** — the debug system family. Last phase of the frame.
