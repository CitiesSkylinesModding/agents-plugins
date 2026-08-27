# Building spawning

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`ZoneSpawnSystem` is the whole of how a demand value becomes a building.

## The spawner

Sources: `src/Game/Game.Simulation/ZoneSpawnSystem.cs`, `src/Game/Game.Prefabs/ZonePrefabs.cs`, `src/Game/Game.Prefabs/BuildingSpawnGroupData.cs`, `src/Game/Game.Buildings/PropertyUtils.cs`.

```
ZoneSpawnSystem (GameSimulation, interval 16, offset 13):
  four gates, from the demand systems' outputs (debugFastSpawn forces all four and sets m_MinDemand = 0):
    residential: (low + medium + high building demand) / 3 > 0   // integer division: a lone demand of 1 or 2 leaves the gate shut
    commercial:  commercial building demand > 0
    industrial:  (industrial building demand + office building demand) / 2 > 0
    storage:     storage building demand > 0

EvaluateSpawnAreas (IJobChunk over Block + Owner + CurvePosition + VacantLot, excluding Temp and Deleted):
  keeps one best candidate per area type per chunk; per VacantLot:
    zone prefab = ZonePrefabs[lot.m_Type]           // where the runtime zone index is spent
    skip the lot unless that prefab has ZonePropertiesData
    return unless the owner road has a ResourceAvailability buffer
    mask off LotFlags.CornerLeft / CornerRight the zone's ZoneFlags do not support
    landValue = m_LandValues[owner].m_LandValue     // indexed with NO HasComponent guard
    sample ground, noise and air pollution at the lot position
    SelectBuilding(...)

SelectBuilding:
  candidate chunks are matched by the shared BuildingSpawnGroupData.m_ZoneType == the lot's zone
      // a chunk filter, which is how the spawner avoids a per-entity zone test
  warehouse chunks only for storage, non-warehouse only for normal, never both
  per candidate prefab in a matching chunk:
    skip unless SpawnableBuildingData.m_Level == 1          // only level 1 ever spawns
    skip unless m_LotSize fits the vacant area and ObjectGeometryData.m_Size.y clears the cell height ceiling
    demand = residential: the computed density's slot in the int3
             commercial:  sum over resources in BuildingPropertyData.m_AllowedSold
             industrial:  sum over m_AllowedManufactured, or m_AllowedStored for storage
    skip unless demand >= m_MinDemand
    priority = fraction of the vacant area covered, jittered per strip
               (the building * rand(1, 1.05), the side strip * rand(0.95, 1), the back strip * rand(0.55, 0.6);
                each remainder strip zeroed independently when the building falls exactly one cell short on that strip's axis)
               * (demand + 1)
               * csum(select(0.01, 0.5, lot corner flags == building access flags))
    the land value handed to the score is scaled first:
        residential: the building width is first widened to the lot's when one cell short and the zone lacks SupportNarrow, then landValue * (building width * the VACANT LOT's depth) / (m_ResidentialProperties == 1 ? 2 : CountProperties())
        the rest:    landValue / m_SpaceMultiplier
    priority *= ZoneEvaluationUtils.GetScore(...)           // below; SelectBuilding's extractor parameter is false at its one call site, so the extractor bypass arms in this method are dead at 1.6.0f1; with m_MinDemand == 0 the score is floored at 0 and 1 is added first
  the winner's lot rectangle is shrunk to its footprint, anchored by its access flags, with a coin flip when it has neither

SpawnBuildingJob (IJobParallelFor scheduled over exactly 3 indices):
  index 0 residential, 1 commercial, 2 industrial; each dequeues its whole queue and keeps the single highest priority
  // so at most three buildings are created per update, one per area type, storage riding inside industrial -- that is the growth throttle, and no parameter tunes it
  Spawn: writes CreationDefinition { m_Prefab, CreationFlags.Permanent | Construction } and an ObjectDefinition into the archetype declared in OnCreate, and the definition machinery builds the entity (placement-definitions)
  position and rotation from ZoneUtils.GetPosition / GetRotation, ground height sampled at the lot front, y clamped under the cell height ceiling -- re-read, unguarded, from the Cell buffer of every block the zone search tree returns under the footprint -- minus the building height minus 0.1
```

**A block spawns only through its `Owner`, and a wrong owner is skipped in silence.**
The query requires `Owner` and the job returns before scoring when that owner has no `ResourceAvailability` buffer, so a mod-built block without a zoning road edge as owner is invisible rather than an error — the unguarded land-value lookup behind that guard faults only for a hand-built owner carrying the buffer without `Game.Net.LandValue`, a pair no vanilla prefab produces.
Source: `src/Game/Game.Simulation/ZoneSpawnSystem.cs`, `src/Game/Game.Prefabs/RoadPrefab.cs`.

## Suitability

Sources: `src/Game/Game.Simulation/ZoneEvaluationUtils.cs`, `src/Game/Game.Prefabs/ZonePreferenceData.cs`.

```
ZoneEvaluationUtils.GetScore, every coefficient a ZonePreferenceData field:
  factor(resource) = min(20, 0.2 / NetUtils.GetAvailability(availabilities, resource, curvePos))
      // an inverse, so scarcity reads high; the residential arm does not use it
  residential: 555 - m_ResidentialSignificanceServices / max(0.1, services availability)
               - m_ResidentialSignificanceWorkplaces / max(0.1, workplaces availability)
               + dot(m_ResidentialSignificancePollution, pollution)
               + m_ResidentialSignificanceLandValue * (landValue - m_ResidentialNeutralLandValue)
  commercial:  555 + max(m_CommercialSignificanceConsumers * (2 - lerp(uneducated factor, educated factor, 0.67)), m_CommercialSignificanceWorkplaces * (2 - workplaces factor))
               + m_CommercialSignificanceCompetitors * (-0.4 + services factor)   // positive in service scarcity
               + m_CommercialSignificanceLandValue * (landValue - m_CommercialNeutralLandValue)
               // GetScore blends 0.9 * lodging:false + 0.1 * lodging:true, and GetCommercialScore never reads its lodging parameter, so the blend is 1.0 of the same number
  office:      555 + m_OfficeSignificanceEmployees * (0.25 - 5 * educated-citizens factor)
               + m_OfficeSignificanceServices * (0.25 - 2 * services factor)
  industrial:  555 + m_IndustrialSignificanceInput * GetTransportScore(m_AllowedManufactured, ...)
               + m_IndustrialSignificanceLandValue * (landValue - m_IndustrialNeutralLandValue)
               - 0.5 * landValue        // a second, unparameterised land-value penalty found nowhere else
  storage:     num = min over allowed resources with nonzero demand of BOTH GetStorageScore(resource, marketPrice, ...) and 0.05 / max(0.1, supply availability);
               max(0, 555 - 10 * num)   // no such resource leaves num at +inf, so the score is 0
  AreaType.None: 0
  // office here means the lot's zone: an industrial-area lot whose zone prefab has an empty ProcessEstimate buffer is scored as office
```

**The suitability infoview's factors and the spawner's score are different expressions.**
`GetFactors` builds the office breakdown from `0.2 - factor` where `GetScore`'s office arm uses `0.25 - 5 * factor` and `0.25 - 2 * factor`, so reading the overlay is not reading what decided the spawn.
Source: `src/Game/Game.Simulation/ZoneEvaluationUtils.cs`.

`ZoningEvaluationResult.CompareTo` sorts the breakdown by absolute score, so the biggest problem and the biggest advantage rank together in the infoview.

(VOLATILE: the system, job, method, component and field names this file names, the score expressions included — their declarations in `Game.Simulation`, `Game.Prefabs`, `Game.Zones`, `Game.Net`, `Game.Tools`, `Game.Common` and `Game.Buildings` under `src/Game/`, at the files the two source lines cite.)
