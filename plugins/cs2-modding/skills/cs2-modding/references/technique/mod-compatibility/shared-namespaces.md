# The namespaces nobody arbitrates

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

The global names two mods can both claim, what each does on the collision, and what keeps it quiet.
None of them arbitrates, and only the prefab-id collision leaves so much as a log line.

## Prefab identity

A prefab's id is its type name, its name, and a hash taken from the prefab's **asset** — the publishing mod's platform id where it has one, otherwise the asset guid.
A prefab a code mod builds at runtime through `PrefabBase.Create<T>(name)` has no asset, so its hash is the default and its identity is nothing but the type name and the name.
**Asset-mod prefabs are namespaced by the mod that published them and a code mod's runtime prefabs are not**, and that asymmetry is the whole exposure.

On a collision `PrefabSystem.AddPrefab` keeps the first registrant's index, logs a duplicate-id line and reports success either way.
The second prefab's entity is still created and appended, so it exists and is simply unreachable by id — the symptom is a prefab nothing can look up, with one log line to say why.
Obsolete identifiers widen it: an obsolete id goes through the same first-come check as a primary one, so a mod migrating its own renamed prefab competes for that name with whatever else claims it, and the id resolves to whichever registered first.

Prefix your runtime prefab names with something nobody else will pick, and treat an obsolete id you claim as a name in the same shared space.
`prefabs-and-assets` owns prefab identity and creation.

## UI resource hosts

Every mod's UI module directory is registered under **one shared host name**, `ui-mods`, so `coui://ui-mods/<path>` is a search path shared across every installed UI mod.
The game registers every module directory at one priority, so between two mods that shipped one path the winner is not something to rely on.

What keeps the bundle itself collision-free is a convention rather than a mechanism: a module's coui path is the shared host plus the **file name** of the module, and the official scaffold emits the bundle named after the module's own id, so the id doubles as the file name.
Keep that shape.

**Everything else the scaffold puts in that directory is exposed**, because the whole directory is what gets registered — its image output lands on `coui://ui-mods/images/<name>`, so an `icon.png` is a name you share with every other mod that shipped one.
Your own build config is the fix: emit those assets under a subdirectory named after your mod, which the registered host resolves just the same.
**Unregister with the two-argument form, naming the host and the path**: the single-argument overload drops the whole host and every path any mod registered under it.
`prefabs-and-assets` owns host registration and resolution.

## Notification identifiers

Pushing a notification is an add-or-update keyed by the identifier string, so two mods choosing the same identifier land in one entry and either can pop the other's.
The merge splits the fields two ways: the title, the thumbnail and the click handler are kept from the first push and the second mod's are dropped, while the text, the progress state and the progress value are overwritten every time.
So a collision leaves one notification wearing the first mod's title and click handler over the second mod's message — the newcomer loses its identity and the incumbent loses its content.
The game's own identifiers are a small fixed set plus one per failing mod's asset id.
Prefix yours with your mod's name.
`diagnostics` owns the notification surface and which fields merge which way.

## Settings asset names

Two mods choosing the same settings **name** share a store; sharing only the file name does not, since each name is its own block in the file.
Name yours after your mod.
`settings-and-input` owns the mechanism.
