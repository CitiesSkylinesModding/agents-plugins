# The loader's states, and the failure notification behind them

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

What the loader records per mod asset, and what the player is shown when a load fails.
Which failures reach a log at all, and the order to work through, are in [diagnostics.md](diagnostics.md).

## The states

The loader records one state per mod asset, in this declaration order — the order matters, because the in-game failure notification fires only for states at or above `IsNotModWarning`.

| State | What it says about the assembly |
| --- | --- |
| `Unknown` | The loader **never tried**: the asset was not required, or it was dropped before the load began. An assembly that declares no `IMod` but is referenced by a mod that does is still required, so it loads normally and ends at `Loaded` with nothing to run. |
| `Loaded` | `OnLoad` ran on every `IMod` implementation in the assembly and returned. |
| `Disposed` | `OnDispose` has run, at shutdown or because the load threw. **The three rethrowing states end here**, which is what keeps them below the notification gate; the two that return do not. |
| `IsNotModWarning` | Unreachable at this version — the guard that reaches it is the same condition that already returned. |
| `IsNotUniqueWarning` | Another asset with the same assembly **name** won the duplicate resolution, which orders by already-loaded, then local, then version descending, then asset id. Nothing is already loaded at boot, so there a local build beats a subscribed copy of the same name and a stale local copy shadows an updated one; on a mid-session re-initialization the copy already in the process wins whatever its locality or version. |
| `GeneralError` | Anything else out of the load, which in practice means **`OnLoad` threw**; the load error is the extracted stack trace. |
| `MissedDependenciesError` | At least one assembly reference resolved to null; the load error is the newline-joined list of unresolved reference names. |
| `LoadAssemblyError` | Loading the mod's own bytes threw: bad IL, a target framework the runtime rejects, a truncated file. |
| `LoadAssemblyReferenceError` | Loading one of the mod's **referenced** assemblies threw. |

Source: `src/Game/Game.Modding/ModManager.cs` (the enum, its order, and where each state is set), `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs` (what makes an asset required, the duplicate ordering, and the two assembly-load throws).

(VOLATILE: the state member names and their order — the mod manager's own state enum; they are also the interpolated half of the failure dialog's localisation key, so a rename moves a key too.)

## The failure notification, and the dialog behind it

The two states that survive to the notification pass — an unresolved dependency and a shadowed duplicate — get a notification keyed by the asset's GUID, carrying a warning or failed progress state, the mod's display name and its store thumbnail.

Clicking it opens a message dialog: a title keyed on warning-or-error, a message keyed on the state itself with the mod's name substituted in, and — where the loader recorded a load error — a **details pane with a copy button** holding it.
For a non-local mod, two extra actions open the store page or disable the mod in the active playset.

**A mod's `OnLoad` stack trace reaches the player nowhere**, since its state is `Disposed` by the time this pass runs and the `Modding` logger has its errors suppressed from the UI.
`Modding.log` is the only record of it.
Source: `src/Game/Game.Modding/ModManager.cs`.

At the end of the pass the progress notification is replaced by a summary carrying loaded and total counts, or by an all-failed notification if the whole initialisation threw.
