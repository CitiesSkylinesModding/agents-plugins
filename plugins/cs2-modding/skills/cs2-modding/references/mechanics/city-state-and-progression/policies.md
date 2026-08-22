# Policies

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`Policy { m_Policy, m_Flags, m_Adjustment }` is a buffer element (`src/Game/Game.Policies/Policy.cs`) stored on whatever entity the policy applies to — the city entity, a `District`, a `Building`, a `Route` — and `PolicyFlags` has the one member `Active`.
The scope is declared nowhere: `ModifiedSystem.GetPolicyRange` derives it by testing the policy prefab's option or modifier component against the target's, in the order district, building, route, city, returning `ModifiedSystem.PolicyRange` (`src/Game/Game.Policies/ModifiedSystem.cs`) — the only place the four scopes are named as a set.
The authoring surface is the `ComponentBase` classes under `[ComponentMenu("Policies/", typeof(PolicyPrefab))]` in `src/Game/Game.Prefabs/`, one per `*ModifierData` buffer or `*OptionData` mask `ModifiedSystem.RefreshEffects` names — `CityModifiers` → `CityModifierData` is the shape — each turning an authored array into its component; the modifier element is `{ m_Type, m_Mode, m_Range }` in every scope.
`PolicyPrefab` is abstract with two concrete subclasses, `PolicyTogglePrefab` and `PolicySliderPrefab`, the latter adding `PolicySliderData` (`src/Game/Game.Prefabs/PolicyPrefab.cs`, `src/Game/Game.Prefabs/PolicySliderPrefab.cs`).
`PolicyVisibility`, `PolicyCategory` and `PolicySliderUnit` are the declared sets under `src/Game/Game.Prefabs/`.

## Applying a policy

Source: `src/Game/Game.Policies/ModifiedSystem.cs` (`Modification4`), `src/Game/Game.Policies/Modify.cs`, `src/Game/Game.UI.InGame/PoliciesUISystem.cs`.

A mod applies a policy by creating an `Event + Modify { m_Entity, m_Policy, m_Flags, m_Adjustment }` entity, which is what `PoliciesUISystem.ModifyPolicy` does behind the `policies.setPolicy` and `policies.setCityPolicy` triggers; editing the `Policy` buffer skips the refresh below.

```
per Modify, when the target has a Policy buffer (the target gets Updated either way):
    find the entry with m_Policy == modify.m_Policy
    found, and modify.m_Flags lacks Active:
        the policy is the ticket-price policy → fire TriggerType.FreePublicTransport
        no PolicySliderData on the policy       → remove the entry; RefreshEffects; emit PolicyEventInfo(deactivated)
        PolicySliderData.m_Default == entry.m_Adjustment → the same
        otherwise fall through:                   // an off-default slider stays as an inactive entry
    found (any remaining case): entry.m_Flags = modify.m_Flags; entry.m_Adjustment = modify.m_Adjustment; RefreshEffects
    not found, and Active set: append Policy(policy, flags, adjustment); RefreshEffects;
        fire TriggerType.PolicyActivated; emit PolicyEventInfo(activated)     // the only path that fires it
RefreshEffects(target, policy, buffer): for each scope whose pair matches, rebuild that scope's
    option mask or modifier buffer from the whole Policy buffer; then add Updated
side paths: a Modify on an Extension with a policy carrying BuildingOption.Inactive toggles ExtensionFlags.Disabled;
    a Modify on a ServiceUpgrade marks its owner Updated
```

## Modifier composition

Source: `src/Game/Game.Simulation/CityModifierUpdateSystem.cs` (`RefreshCityModifiers`, `AddModifier`; `GameSimulation`, `GetUpdateInterval` = 256), `src/Game/Game.City/CityUtils.cs`, `src/Game/Game.Prefabs/CityEffects.cs`.

```
RefreshCityModifiers (from ModifiedSystem, and every 256 frames from CityModifierUpdateSystem):
    modifiers.Clear()
    per Active policy whose prefab has a CityModifierData buffer, per element:
        slider:  t = (adjustment - slider.m_Range.min) / (max - min); t = 0 when min == max; t = saturate(t)
                 delta = lerp(element.m_Range.min, element.m_Range.max, t)
        toggle:  delta = element.m_Range.min
        AddModifier(element, delta)
    per CityEffectProvider entity (not Deleted, Destroyed or Temp), its prefab's CityModifierData
        plus each InstalledUpgrade's not flagged BuildingOption.Inactive:
        no Signature and an Efficiency buffer → delta = lerp(m_Range.min, m_Range.max, efficiency)
        otherwise                             → delta = m_Range.max
        AddModifier(element, delta)
AddModifier: grow the buffer to index m_Type, then by m_Mode
    Relative:        delta.y = delta.y * (1 + d) + d
    Absolute:        delta.x += d
    InverseRelative: d = 1 / max(0.001, 1 + d) - 1; then as Relative
CityUtils.ApplyModifier(ref value, modifiers, type), when modifiers.Length > type:
    value += delta.x; value += value * delta.y               // absolute first, then relative; else no-op
CityUtils.GetModifier: the raw float2, default when the buffer is shorter
```

A toggle policy contributes `m_Range.min`, not `max`.
`CityEffects` constructs a one-sided range `Bounds1(0, m_Delta)`, so a building's effect at zero efficiency is zero; it adds `CityEffectProvider` to the instance archetype, or through `GetUpgradeComponents` when the prefab is a `ServiceUpgrade`.
`RefreshCityOptions` is the same rebuild for `City.m_OptionMask`: zero it, then OR in `CityOptionData.m_OptionMask` per active policy.
`CityModifierType` declares explicit values with a hole — no member has value 2 — so that index of a live buffer is always `default` (`src/Game/Game.City/CityModifierType.cs`); `DistrictModifierType` is contiguous (`src/Game/Game.Areas/DistrictModifierType.cs`).

## Default policies

Source: `src/Game/Game.Policies/DefaultPoliciesSystem.cs` (`Modification3`).

```
AddDefaultPoliciesJob, over [Created, Policy, PrefabRef] without Temp, per DefaultPolicyData on the prefab:
    append Policy(policy, Active, PolicySliderData.m_Default when the policy is a slider, else 0)
PostDeserialize, on NewGame or a load below Version.taxiFee:
    the same for the city entity, reading DefaultPolicyData off the ServiceFeeParameterData prefab
```

(VOLATILE: every system, component, field, property, enum, method, binding name and `Source:` path this file names — their declarations under `src/Game/` in `Game.Policies`, `Game.Prefabs`, `Game.Simulation`, `Game.City`, `Game.Areas`, `Game.Buildings`, `Game.Routes`, `Game.Triggers`, `Game.Common`, `Game.Tools`, `Game.UI.InGame` and the root `Game` namespace, at the files the sections cite, plus `Bounds1` in the `Colossal.Mathematics` assembly.)
