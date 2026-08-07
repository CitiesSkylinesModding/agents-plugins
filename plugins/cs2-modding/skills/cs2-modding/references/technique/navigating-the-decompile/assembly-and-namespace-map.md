# The assembly and namespace map

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The map below is of that tree's own directories, so without one there is nothing here to navigate.
`cs2-modding-setup` provisions it.

Which namespace directory owns a subject, and how big each one is in the only sense that changes what you do: whether you can read it whole or have to search inside it.
Reach for it when you are deciding what to open and what to exclude.

(VOLATILE: the read-when list and the noise families — the checkout's own `src/` tree, at directory level; the namespace set and the assembly-root file list — `src/Game`.)

## Inside `Colossal.Core`

Three directories matter to a mod, and all three are small enough to read: `Colossal.Entities`, `Colossal.Serialization.Entities` and `Colossal.Json`.
The rest is a grab-bag — a vendored shell-exec library, a command-line option parser, the coroutine host — and none of it is game behaviour.

## Read when your mod goes there

`Unity.Entities`, `Unity.Collections`, `Unity.Mathematics`, `Unity.Burst`, `Unity.InputSystem`, `PDX.SDK`, `PDX.ModsUI`, `Colossal.OdinSerializer`, `Colossal.AssetPipeline`, `Colossal.PSI.Common`, `Colossal.ATL`, `Cohtml.Runtime`, `cohtml.Net`, `Cohtml.RenderingBackend`, `Game.ArtPipeline`.

`Unity.InputSystem` is the trap in that list: `Game.Input` wraps it, and a mod's keybindings go through the wrapper, so read `Game.Input` unless you have a reason not to.
`PDX.SDK` and `PDX.ModsUI` are Paradox Mods, accounts and cloud saves, and nothing else.

## Noise, by family

- **Base class library and Mono** — `mscorlib`, `System*`, `netstandard`, `Mono.*`. The largest family in the tree by a wide margin.
- **`UnityEngine*`** — the `UnityEngine.*Module` set, plus `UnityEngine.UI` and `UnityEngine`.
- **Render pipelines** — `Unity.RenderPipelines*`.
- **Console-platform SDK** — `Unity.Microsoft.GDK*` and `XblPCSandbox`, which a Windows mod never reaches.
- **Vendored third party** — Steamworks, `Newtonsoft.Json`, `ICSharpCode.SharpZipLib`, `Unity.TextMeshPro`, `Cinemachine`, `Unity.Timeline`, `Unity.VectorGraphics`, `DiscordSDK`.
- **Test and tooling** — the `*TestScenarios` set, `DryDock.Runtime`, `Colossal.TestFramework`.

## `src/Game`, by what the directory lets you do

**Too big to read whole — search inside them.**
`Game.Prefabs` by a wide margin, then `Game.Simulation`, `Game.UI.InGame`, `Game.Rendering`, `Game.Net`, `Game.UI.Widgets`, `Game.Buildings`, `Game.UI.Editor` and `Game.Tools`.

**Every other directory here you can list and skim**, from `Game.Vehicles` and `Game.Citizens` down to the single-file ones.
`navigating-the-decompile` owns the non-`Game` namespaces that also live in this assembly, and the codegen files worth excluding from a search.

**A subject that sounds like a sub-area usually has its own directory, dots and all.**
`Game.Simulation.Flow`, `Game.Serialization.DataMigration`, `Game.Prefabs.Effects` and `Game.Rendering.Utilities` are siblings of their parents rather than folders inside them, and each is small.
So run `ls src/Game` before searching one of the nine: the directory that owns your subject may be a hundredth of the size of the one you were about to grep.

**Ten files sit at the assembly root**, in `src/Game/*.cs` with no namespace directory: three codegen artifacts, plus `DayNightCycleData.cs`, `DebugCamera.cs`, `DepthFadePass.cs`, `GameModeSettingData.cs`, `ShowIfAttribute.cs`, `UberZOrdererTest.cs` and `VTTestGameManager.cs`.
A `src/*/*/…` glob never sees them.
