# Assets and resource hosts

Verified against game version 1.6.0f1.

The asset database a mod loads content through, and the resource scheme it serves its own files over.
The prefab layer above both is [prefabs and assets](prefabs-and-assets.md).

## Loading an asset from code

`Colossal.IO.AssetDatabase` is the layer under the prefab system.
Four statics exist — `global`, `game`, `user` and `packages` — plus a factory for a throwaway transient one.
**`global` is a collection of registered databases rather than a database itself**, and it is the one to read through; `user` is the writable one.

The read surface is `IAssetDatabase`: `TryGetAsset` and `GetAsset`, each keyed four ways — by `Uri`, by string uri, by guid, and by a search filter — plus `GetAssets<T>(SearchFilter<T>)`, `AllAssets()`, `DeleteAsset` and `UnloadAllAssets`.
The write surface is `ILocalAssetDatabase`, which adds `AddAsset<TAssetData, TData>(AssetDataPath, TData, Hash128 forceGuid = default)` and simpler overloads, `MoveAssetTo`, `CopyAssetTo`, `Exists<T>` and `MarkForDeletion`.
`AssetDatabase.global` is not an `ILocalAssetDatabase`.

**`PrefabAsset` is the asset kind carrying a prefab**, and `Load()` / `Load<T>()` return the `ScriptableObject`.
It saves as text by default with a binary option.
The game's own prefab-loading step is nothing more than those three calls in a loop:

```csharp
foreach (PrefabAsset asset in assetDatabase.GetAssets(default(SearchFilter<PrefabAsset>)))
{
    if (asset.Load() is PrefabBase prefab)
    {
        m_PrefabSystem.AddPrefab(prefab);
    }
}
```

Two reads come up constantly and are worth knowing verbatim.
**Locating your own mod's directory on disk** goes through the executable asset for your assembly, found with a search filter matching its full name; from there `Path.GetDirectoryName(asset.path)` is your install folder.
**Testing an existing prefab's provenance** is `prefab.asset?.database == AssetDatabase<ParadoxMods>.instance`, which distinguishes subscribed content from base-game content.

**Registering a database of your own** is the deep end and is occasionally the right answer, for a mod importing content it generates or ships outside the normal asset pipeline.
Declare a descriptor — five members: `name`, `canWriteSettings`, `dlcId`, `assetFactory`, `dataSourceProvider` — expose `AssetDatabase<YourDescriptor>.instance`, register it with `AssetDatabase.global.RegisterDatabase(...)`, populate it, and unregister on dispose.
Content goes in through `AddAsset<PrefabAsset, ScriptableObject>(path, prefab, Hash128.CreateGuid(name))` followed by `Save()`, and the resulting prefab is handed to `AddPrefab` **from the main thread**.
The same call shape stores geometry, locale and image assets.

(VOLATILE: the `IAssetDatabase` and `ILocalAssetDatabase` member lists and the four `AssetDatabase` statics — both interfaces, and the database type itself.)

## Serving your own files over the game's resource scheme

**The UI reads files through `coui://<host>/<path>`, where a host is a name mapped to one or more directories on disk.**
Registration is one call:

```csharp
UISystem.AddHostLocation(string hostName, string path, bool shouldWatch = true, int priority = 0);
```

It appends to the host's path list, keeps the list sorted by priority, ignores a duplicate path, and raises a host-added event carrying the watch flag.
An overload takes several paths at once, and `RemoveHostLocation` exists — put it in `OnDispose`, since a host location is state registered outside your own world.

**Resolution walks the host's paths in priority order and takes the first that reads.**
An unknown or empty host fails with an invalid-host-locations error.
So **two mods registering the same host name do not conflict — they stack**, and priority decides who is asked first.

The two shapes worth copying:

- **A read-only directory beside your assembly**, watching off, for icons you ship.
  Derive the path from your own executable asset as above.
- **A watched temporary directory**, for files you generate at runtime.
  Watching is what makes a thumbnail written after startup appear at all; without it the resource handler serves what it already knows.

The game registers three hosts of its own, and they are the shapes to recognise: `gameui` for the base UI, `ui-mods` for the directory of every mod's UI module asset, and one host per UI host asset found in the database.

**Where a `coui://` URL is consumed on the prefab side.**
`UIObject.m_Icon` is a plain string, and the image system returns it when non-empty, falling back to the UI group's icon.
The thumbnail chain is the one to know: icon if set, else a placeholder when thumbnails are disabled, else the prefab's own `thumbnailUrl` with a size query appended — and that url is `"thumbnail://ThumbnailCamera/"` plus the prefab id rendered as a url segment.
**So a prefab with no icon gets a live render keyed on its prefab id**, through one of three extra schemes the game's resource handler layers on top of `coui`, alongside screen capture and user avatar.

The Cohtml side of the frontend is `frontend-and-injection`.

(VOLATILE: the scheme names `coui`, `assetdb`, `thumbnail`, `screencapture` and `useravatar`, the `gameui` and `ui-mods` host names, and `AddHostLocation`'s signature — the UI system, and the default resource handler.)
