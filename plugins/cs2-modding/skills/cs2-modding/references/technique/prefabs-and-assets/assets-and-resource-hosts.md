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

It inserts the path into the host's list at a binary-searched position on priority alone, ignores a duplicate path, and raises a host-added event carrying the watch flag.
An overload takes several paths at once, and `RemoveHostLocation` exists — put it in `OnDispose`, since a host location is state registered outside your own world.
**Unregister with the two-argument form, naming both the host and the path**: the single-argument overload drops the whole host and every path any other mod registered under it.

**Resolution walks the host's paths in priority order and takes the first that reads.**
An unknown or empty host fails with an invalid-host-locations error.
So **two mods registering the same host name do not conflict — they stack**, and the lowest priority number is asked first.
Paths sharing one priority land wherever the binary search puts them, so between two mods that shipped the same file name under one host, which copy resolves is not something to rely on.
`mod-compatibility` owns what that means when the host is one every UI mod shares.

The two shapes worth copying:

- **A read-only directory beside your assembly**, for icons and markup you ship.
  Derive the path from your own executable asset as above.
- **A directory you write into at runtime**, watching off, for files you generate.

**The watch parameter defaults to `true`, so an argumentless call watches.**
Its one consumer is the UI view's live-reload component, which a view constructs only when its settings enable live reload.
Two things turn that on: the UI developer-mode launch option, which is how you get it in the sessions you develop in, and a stored UI-manager settings asset, which the game reads over the launch option's value and which no shipped database carries.
So a player ordinarily has no watcher, and you cannot treat that as a guarantee.
There **a change under the directory reloads the whole view** rather than refreshing the file that changed.
That is what you want for a directory you ship and edit while iterating, and it is why a directory your mod writes into at runtime takes `false`: every file written blanks and rebuilds the UI the write was updating.

Watching is not what serves the file either.
Resolution holds no cache — a request walks the host's paths and reads the file off disk again — so **a file written after startup is served the next time the frontend asks for it, watched or not.**

**One branch runs before that walk, and it is a name collision waiting to happen.**
A request for a raster image — the extension list is `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.psd`, `.tga`, `.astc`, `.pkm`, `.dds`, `.ktx`, and pointedly not `.svg` — is first looked up as a Unity resource under `UI/SharedImages`, keyed on the file name with its extension dropped, and only falls through to the host walk when that lookup comes back null.
So `coui://yourmod/settings.png` serves the game's built-in `settings` texture if one exists under that name, and your file is never read.
**Name a raster file you serve after something no shared image is called**, or ship it as `.svg`, which never takes this branch.
What may not ask again is the frontend, and a fixed file name rewritten in place is where that bites.
A version query on the name reaches the same file, since the resolved path comes from the URL's path alone and the `coui` branch reads no query at all.
(UNVERIFIED: what the engine's image cache does with a URL it already holds when the bytes under it change — no source this plugin reads states the cache's key or its invalidation, and nobody has watched a re-request in a running game.)

The game registers three hosts of its own, and they are the shapes to recognise: `gameui` for the base UI, `ui-mods` for the directory of every mod's UI module asset, and one host per UI host asset found in the database.

**Where a `coui://` URL is consumed on the prefab side.**
`UIObject.m_Icon` is a plain string, and the image system returns it when non-empty, falling back to the UI group's icon.
The thumbnail chain is the one to know: icon if set, else a placeholder when thumbnails are disabled, else the prefab's own `thumbnailUrl` with a size query appended — and that url is `"thumbnail://ThumbnailCamera/"` plus the prefab id rendered as a url segment.
**So a prefab with no icon gets a live render keyed on its prefab id**, through one of three extra schemes the game's resource handler layers on top of `coui`, alongside screen capture and user avatar.

The Cohtml side of the frontend is `frontend-and-injection`.

(VOLATILE: the scheme names `coui`, `assetdb`, `thumbnail`, `screencapture` and `useravatar`, the `gameui` and `ui-mods` host names, `AddHostLocation`'s signature, what a watched change reloads, and the raster extension list and `UI/SharedImages` path behind the collision above — the UI system, the UI live-reload class, and the default resource handler.)
