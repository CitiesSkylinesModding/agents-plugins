# A hotkey tool that rescues a building from abandonment

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want a tool for rescuing buildings that are about to be abandoned. I'd toggle it with a hotkey; while it's on, whichever building I'm hovering gets a circle drawn around it on the ground and a tooltip at the cursor showing how close it is to being abandoned, and left-clicking the building resets that so the abandonment countdown starts over. Cancelling out of the tool (right-click or Escape, like leaving any other tool) should put me back on whatever tool I was using before I switched to it — if I was bulldozing, back to the bulldozer.

## Checklist

### The inherited apply and cancel actions are switched on by the tool

- Falls in: the tool polls `applyAction` or `cancelAction` (`WasPressedThisFrame()`, `IsPressed()`, `WasReleasedThisFrame()`, `IsInProgress()`) and nowhere assigns that action's `shouldBeEnabled` to `true`; or it reads the click off `UnityEngine.Input`, `Mouse.current`, or an action it looked up itself with `InputManager.instance.FindAction` on the game's tool map.
- Avoids: the click and the cancel are read through the inherited `applyAction` and `cancelAction`, and the tool sets each one's `shouldBeEnabled = true` from `OnStartRunning` or from its own `OnUpdate`.
- Reference: cs2-modding/references/technique/custom-tools/custom-tools.md
- Source: src/Game/Game.Input/UIInputAction.cs:64

### The tool to return to is learned from the tool-changed event

- Falls in: the field holding the tool to return to is written at the tool's own activation site, such as `m_PreviousTool = m_ToolSystem.activeTool` immediately before `m_ToolSystem.activeTool = this`; or the tool-changed delegate is assigned with `=`.
- Avoids: `OnCreate` subscribes `m_ToolSystem.EventToolChanged += ...` and the handler records every tool other than this one; cancelling assigns `activeTool` that recorded tool, with the default tool as the fallback.
- Reference: cs2-modding/references/technique/custom-tools/custom-tools.md
- Source: src/Game/Game.Tools/ToolSystem.cs:96

### The overlay is written under the buffer's dependency

- Falls in: `GetBuffer` is called with its handle discarded (`out _`, or a handle never used), or once in `OnCreate` with the `Buffer` cached in a field, and the `Draw*` calls run on the main thread with no `AddBufferWriter`.
- Avoids: each frame takes `GetBuffer(out JobHandle deps)`, draws from a job scheduled after `deps` combined with `inputDeps`, hands the job's handle to `AddBufferWriter`, and returns it from `OnUpdate(JobHandle)`.
- Reference: cs2-modding/references/technique/custom-tools/custom-tools.md
- Source: src/Game/Game.Rendering/OverlayRenderSystem.cs:621

### The tooltip system runs in the tooltip phase

- Falls in: the system that adds the mouse tooltip is registered with `UpdateAt` in any phase other than `SystemUpdatePhase.UITooltip`, such as `UIUpdate`, `ToolUpdate` or `Rendering`; or the tool adds the widget to `TooltipUISystem.mouseGroup` itself from its own update.
- Avoids: a `TooltipSystemBase` descendant registered with `UpdateAt<...>(SystemUpdatePhase.UITooltip)` calls `AddMouseTooltip` while the tool is the active one.
- Reference: cs2-modding/references/technique/custom-tools/custom-tools.md
- Source: src/Game/Game.UI.Tooltip/TooltipUISystem.cs:52

## Roots

- decompile
