# A city festival policy that switches itself on

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod that adds a city-wide policy called City Festival. While it's on, the city should be 20% more attractive to tourists and residents should get 10% more entertainment. It should become available at milestone 4 like other policies do, and once it's unlocked, the mod should switch it on by itself the moment the city crosses 10,000 residents (the player can still turn it off afterwards). Also, whenever the policy gets toggled on or off, write the city's current entertainment bonus to the log so I can see the effect land.

## Checklist

### The city entity is read off `CitySystem` where it is used

- Falls in: `CitySystem.City` (or the `Game.City.City` singleton entity) is copied into a field once, in `OnCreate`, and every later read of the city's components and buffers goes through that field.
- Avoids: `m_CitySystem.City` is read in the update or handler that uses it, or re-resolved on every load (`OnGameLoadingComplete` / `OnGamePreload`) before use.
- Reference: cs2-modding/references/mechanics/city-state-and-progression/city-state-and-progression.md
- Source: src/Game/Game.Simulation/CitySystem.cs:61

### The toggle's modifier ranges carry the effect in `min`

- Falls in: a `CityModifierInfo` (or `CityModifierData`) authored for the toggle policy has an `m_Range` whose `min` is zero and whose `max` is the intended effect, such as `new Bounds1(0f, 0.2f)`.
- Avoids: every `m_Range` on the toggle policy has the intended effect in `min`, such as `new Bounds1(0.2f, 0.2f)`.
- Reference: cs2-modding/references/mechanics/city-state-and-progression/policies.md
- Source: src/Game/Game.Simulation/CityModifierUpdateSystem.cs:132

### The city modifier is read through a length-guarded accessor

- Falls in: the city's `CityModifier` buffer is indexed by the enum value directly, such as `modifiers[(int)CityModifierType.Entertainment]`, with no comparison against `modifiers.Length` first.
- Avoids: the value comes from `CityUtils.GetModifier` or `CityUtils.ApplyModifier`, or the code checks `modifiers.Length > (int)type` before indexing.
- Reference: cs2-modding/references/mechanics/city-state-and-progression/policies.md
- Source: src/Game/Game.Simulation/CityModifierUpdateSystem.cs:209

### The policy is made unlockable before it is registered

- Falls in: the policy prefab reaches `PrefabSystem.AddPrefab` with no `Unlockable` (or other `UnlockableBase`) component on it, and the milestone requirement is attached afterwards, through `PrefabSystem.AddUnlockRequirement` or by adding `Unlockable` after `AddPrefab`.
- Avoids: an `Unlockable` component whose `m_RequireAll` (or `m_RequireAny`) names the milestone prefab is added to the policy prefab before `AddPrefab` is called.
- Reference: cs2-modding/references/mechanics/city-state-and-progression/unlocking.md
- Source: src/Game/Game.Prefabs/PrefabSystem.cs:605

### The policy's lock state is read as an enabled bit

- Falls in: whether the policy is unlocked is decided by `HasComponent<Locked>` (or its negation) on the policy prefab entity.
- Avoids: it is decided by `HasEnabledComponent<Locked>`, `IsComponentEnabled<Locked>`, or a `ComponentLookup<Locked>.IsComponentEnabled` read.
- Reference: cs2-modding/references/mechanics/city-state-and-progression/unlocking.md
- Source: src/Game/Game.Prefabs/UnlockSystem.cs:208

## Roots

- decompile
