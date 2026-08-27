# Service coverage

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Coverage is a pathfind, not a circle: for each covering building the game searches outward through the road network, and what comes back is a normalised cost per reachable road edge, turned into a value stored on that edge.
A building with no road edge has no coverage of any service, and an unzonable net — a highway among them — carries no coverage buffer at all, because `RoadPrefab` adds `Game.Net.ServiceCoverage` only where the prefab has a zone block.
Source: `src/Game/Game.Prefabs/RoadPrefab.cs`.

## The rotation

`ServiceCoverageSystem` runs every simulation frame and gates itself: `GetFrameService(frame) = (CoverageService)(frame % 256 * 8 / 256)`, so each of the eight services owns 32 consecutive frames of a 256-frame cycle (`COVERAGE_UPDATE_INTERVAL = 256`).
On the frame a slot changes, the next service's buildings are enqueued — the searches issued in slices of at least a 192th of the queue per frame, completed by the pathfinder as it gets to them, with the service's next apply pass as the deadline — so **the values applied in a slot come from searches enqueued during the previous rotation**; the current service's results are applied by a four-job pipeline: clear that one buffer index on every edge, gather the buildings, compute each element, then merge.
Source: `src/Game/Game.Simulation/ServiceCoverageSystem.cs`.

## The search

The search's only tuning input is the prefab's `CoverageData.m_Range`, which the pathfind turns into two bounds per lane pair:

```
m_MinDistance = m_Range * float4(0,   0.6, 0,   0.6)     // x/z: path cost, y/w: raw distance
m_MaxDistance = m_Range * float4(2,   1.2, 2,   1.2)
cost accumulates as  length * PathSpecification.m_Density * |Δ|    (PathUtils.CalculateCost; m_Density is sqrt(density) on car lanes, 1 on pedestrian)
distance as          length * |Δ|
expansion stops once a node's distance reaches m_MaxDistance.y, and a result is emitted
per direction only while the nearer end's distance is under m_MaxDistance.y
per edge end:  normalised = saturate(max((cost - min) / (max - min), (distance - min) / (max - min)))
ProcessResultsJob then keeps the minimum cost per owning edge, oriented per end to the edge's own direction (an end whose lane does not span the edge reads float.MaxValue), and rebuilds the building's CoverageElement buffer
```

So `m_Range` is a cost budget of `2 × m_Range` and a distance budget of `1.2 × m_Range` with the first `0.6 × m_Range` free, and whichever lane runs out first decides the result: **a car-travelling service reaches further along low-density stretches for the same metres, while the distance lane caps the reach absolutely** — for the five services that travel on foot, cost is distance itself and only the budgets differ.
Source: `src/Game/Game.Pathfind/CoverageJobs.cs`, `src/Game/Game.Pathfind/PathUtils.cs`.

How the search travels is per service (`ServiceCoverageSystem.SetupPathfindMethods`): `PostService`, `Education`, `EmergencyShelter` and `Welfare` reach on foot only; `Park` reaches on foot from its activity spots (a twelve-member `ActivityType` mask) rather than from its road connection; everything else — `Healthcare`, `FireRescue`, `Police` — travels the road by car.

**Budget and efficiency scale coverage magnitude, never reach.**
Efficiency enters exactly once, as a multiplier on the written value below, and the range is read off the prefab with no efficiency term anywhere in the path — what a player sees move is the contour where the falloff crosses a visible threshold, not the reach; telecom is the one genuine exception ([parks-leisure-and-telecom.md](parks-leisure-and-telecom.md)).
Source: `src/Game/Game.Simulation/ServiceCoverageSystem.cs`.

## The value, the capacity, the district

`ProcessCoverageJob` computes per element, then `ApplyCoverageJob` merges globally:

```
ProcessCoverageJob, per building:
  the CoverageElement buffer and the prefab's CoverageData are read off the entity as-is; a Temp building then redirects to its m_Original for ONLY what follows — ModifiedServiceCoverage, the Owner walk, districts, efficiency (the simulation query excludes Temp; CoveragePreviewSystem runs this job on Temps)
  CoverageData taken from the prefab; a park's ModifiedServiceCoverage replaces it
  efficiency = GetEfficiency(top-level Owner, walked up until it stops resolving)
             = 0   when the walk or the Temp redirect moved off the chunk's own entity and that entity is itself Inactive or Destroyed
  districts  = the top-level owner's ServiceDistrict buffer as a set; empty set = everywhere
  per CoverageElement (skipping edges whose buffer is missing; district filtering runs only where the edge carries BorderDistrict):
    an edge inside one district        -> skipped unless that district is in the set
    an edge straddling two districts   -> skipped unless a side matches;
                                          densityFactor = 0.5 with one side, 1 with both
    coverage      = max(0, 1 - cost * cost) * m_Magnitude * efficiency    // cost is float2, one lane per end
    lengthFactor  = edge length * sqrt(max(0.01, Game.Net.Density.m_Density))
  the building's elements are sorted best-first; total = remaining = m_Capacity

ApplyCoverageJob (single-threaded, buildings kept sorted by their current best element):
  take the globally best remaining element
  when it beats what the edge already holds:
    spent   = 1 - remaining / total
    knee    = 1 - (0.99 * spent)^8                       // ~1 until near exhaustion
    write   = clamp(coverage * knee - existing, 0, coverage * knee * densityFactor)
    edge   += write                                      // an edge is only ever raised
    remaining -= mean(saturate(write / coverage)) * lengthFactor * densityFactor
  a building whose elements or capacity run out drops; its remaining elements are never written
```

Three consequences: **overlapping stations do not sum** — the strongest wins per edge, per end; **capacity is spent in units of road length weighted by the square root of density** — a building covers a fixed amount of populated road, not an area or a citizen count; and **a station assigned to a district covers a border road at half strength for a quarter of the capacity spend**, since the halving enters both the write and the debit.
Source: `src/Game/Game.Simulation/ServiceCoverageSystem.cs`, `src/Game/Game.Net/CoverageService.cs`.

## Who reads coverage

A consumer samples the edge buffer at its own position: `NetUtils.GetServiceCoverage(coverages, service, curvePos)` lerps the edge's two ends, returning 0 past the buffer's length.
The direct simulation reads — the property-score path below adds more (re-check: grep `src/Game/` for `GetServiceCoverage(` and for raw indexed reads of the buffer):

| Service | Read by | Effect |
| --- | --- | --- |
| `Healthcare` | `CitizenHappinessSystem.GetHealthcareBonuses`; `LandValueSystem` edge job, raw index `[0]` | health and wellbeing; land value |
| `FireRescue` | `EventHelpers.GetFireHazard` | `hazard *= max(0.01, 1 - coverage * 0.01)`, and `riskFactor = hazard / (1 + coverage * 0.5)` — coverage suppresses the hazard, never the burning fire |
| `Police` | `CrimeAccumulationSystem`; `CitizenPathfindSetup` (shelter-seeking cost, twice); `LandValueSystem`, raw index `[2]` | crime rate scales by `PoliceConfigurationData.m_CrimePoliceCoverageFactor * max(0, 5 / (5 + coverage))`; shelter choice; land value |
| `Park` | `CitizenHappinessSystem.GetEntertainmentBonuses` | wellbeing, shaped `min(1, sqrt(coverage / 1.5))` after `CityModifierType.Entertainment` |
| `PostService` | nothing | — |
| `Education` | `CitizenHappinessSystem.GetEducationBonuses`; `LandValueSystem`, raw index `[5]` | wellbeing scaled by `sqrt(n)` against `m_NeutralEducation`, where `n` is the household's size when the scored citizen is itself a child and 0 otherwise (`citizens-and-households` traps the loop); land value |
| `EmergencyShelter` | nothing | — |
| `Welfare` | `CitizenHappinessSystem.GetWellfareBonuses` / `GetWelfareValue`; `CrimeCheckSystem` | wellbeing weighted toward unhappy citizens; a repeat offender's crime is cancelled with probability `coverage * m_WelfareCrimeRecurrenceFactor` against a population-scaled roll |

**`PostService` and `EmergencyShelter` coverage is computed, stored, serialized, rendered — and read by no simulation system**: post works through vans and mailboxes, the shelter through `EvacuationRequest`, so do not tune those two `CoverageData`s expecting a simulation effect.
**The healthcare, education and entertainment bonuses are gated on progression**: each returns zero while the service prefab entity carries an enabled `Locked` — the welfare pair carries no such gate — so a city that has not unlocked one of the three gets no wellbeing from a modded-in building.
Coverage also feeds where households choose to live: `PropertyUtils.GetPropertyScore` and the apartment-quality helpers run the same bonus functions over the candidate's road edge, called from `HouseholdFindPropertySystem` and `CitizenPathfindSetup` — reads the table above does not repeat.
Only the three land-value reads use raw literal indices, so a reordering of `CoverageService` breaks them silently.
Source: `src/Game/Game.Simulation/CitizenHappinessSystem.cs`, `src/Game/Game.Simulation/LandValueSystem.cs`, `src/Game/Game.Simulation/CrimeAccumulationSystem.cs`, `src/Game/Game.Simulation/CrimeCheckSystem.cs`, `src/Game/Game.Simulation/EventHelpers.cs`, `src/Game/Game.Buildings/PropertyUtils.cs`, `src/Game/Game.Net/NetUtils.cs`.

**The buffer has nine slots for eight services, and the ninth is the coverage preview's scratch slot.**
`Game.Net.InitializeSystem` sizes every new edge's buffer to 9 and `Game.Serialization.ServiceCoverageSystem` tops a loaded one up to 9, while `CoverageService.Count` is 8; the simulation's own jobs clear and write indices 0–7, and `CoveragePreviewSystem` schedules the same clear and process jobs at index 8 to render a placement preview, seeded from the active infoview's service — the buffer serializes whole, so a save can carry preview residue there.
Index a coverage buffer by the enum member, never by `Length`.
Source: `src/Game/Game.Net/InitializeSystem.cs`, `src/Game/Game.Serialization/ServiceCoverageSystem.cs`, `src/Game/Game.Tools/CoveragePreviewSystem.cs`.

(VOLATILE: every component, field, enum member and its order, system, job, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Pathfind`, `Game.Net`, `Game.Prefabs`, `Game.Buildings`, `Game.City`, `Game.Tools` and `Game.Serialization`, at the files each listing and the consumers table cite; the `CoverageService` order doubly so, since it is the buffer index and the land-value reads hardcode three of its values.)
