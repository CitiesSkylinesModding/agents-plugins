# The level-up loop: land value, rent, upkeep, condition

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The chain is five systems in `SystemUpdatePhase.GameSimulation` — `PropertyProcessingSystem`, `LandValueSystem`, `BuildingUpkeepSystem`, `RentAdjustSystem`, `PropertyRenterSystem` — registered in that order in `src/Game/Game.Common/SystemOrder.cs`.
A system's passes per day is `262144 / GetUpdateInterval(phase)`, and the passes consuming `UpdateFrame` divide one building's own rate by its sixteen buckets (`ecs-in-this-game`).

## Land value

Sources: `src/Game/Game.Simulation/LandValueSystem.cs`, `src/Game/Game.Net/LandValue.cs`, `src/Game/Game.Simulation/LandValueCell.cs`, `src/Game/Game.Prefabs/LandValueParameterData.cs`.

**Land value is two values with one name, and rent reads the edge one.**
`LandValueSystem` writes both a 128 × 128 `LandValueCell` map — the infoview layer, sampled by `LandValueSystem.GetCellIndex(float3)` — and a per-road-edge `Game.Net.LandValue { m_LandValue, m_Weight }`, which is what every rent computation reads through `Building.m_RoadEdge`.
Source: `src/Game/Game.Simulation/LandValueSystem.cs`, `src/Game/Game.Simulation/RentAdjustSystem.cs`.

```
LandValueSystem (kTextureSize = 128, kUpdatesPerDay = 32, interval 262144 / 32),
every coefficient a LandValueParameterData field:

EdgeUpdateJob, per road edge, from the edge's own buffers at bare literal indices:
  health    = lerp of ServiceCoverage[0]        * m_HealthCoverageBonusMultiplier
  education = lerp of ServiceCoverage[5]        * m_EducationCoverageBonusMultiplier
  police    = lerp of ServiceCoverage[2]        * m_PoliceCoverageBonusMultiplier
  commerce  = lerp of ResourceAvailability[1]   * m_CommercialServiceBonusMultiplier
  bus       = lerp of ResourceAvailability[31]  * m_BusBonusMultiplier
  tram/sub  = lerp of ResourceAvailability[32]  * m_TramSubwayBonusMultiplier
  target = max(0, sum); the edge lerps 60% toward it, and only when the difference is >= 0.1
  // nothing in this file names those six indices; their writers belong to city-services-and-coverage

LandValueMapUpdateJob, per map cell:
  water deeper than 1 m -> m_LandValueBaseline, flat
  otherwise: average the LandValue of the road edges within 1.5 cells whose value is positive
             (a zero-value edge is left out of numerator and denominator)
             + attractiveness and telecom bonuses, each capped at m_CommonFactorMaxBonus
             + a terrain-attractiveness bonus, only when there is no water pollution and no ground pollution
             - the three pollution penalties (m_GroundPollutionPenaltyMultiplier and siblings)
  result = max(m_LandValueBaseline, m_LandValueBaseline + the sum above)
             // the baseline is an addend as well as the floor
  the cell lerps 40%, behind the same >= 0.1 difference guard as the edge
```

## Rent and the market

Sources: `src/Game/Game.Simulation/RentAdjustSystem.cs`, `src/Game/Game.Simulation/PropertyRenterSystem.cs`, `src/Game/Game.Simulation/PropertyProcessingSystem.cs`, `src/Game/Game.Buildings/PropertyUtils.cs`.

```
RentAdjustSystem.AdjustRentJob (kUpdatesPerDay = 16, UpdateFrame bucket), per building with a Renter
buffer, excluding StorageProperty:
  recompute the asked rent (the entry file's formula), land value from Building.m_RoadEdge
  write it into PropertyOnMarket.m_AskingRent when listed, and into each renter's PropertyRenter.m_Rent
      // rent is neither negotiated nor grandfathered; skipped for a renter with no Resources
      // buffer, and for a company renter missing its process or workplace data
  adds PropertyOnMarket outright when dropping a renter whose PropertyRenter is gone left spare
      capacity (not abandoned or destroyed) -- the no-Signature-guard listing path the trap below cites
  affordability per renter:
    household -> EconomyUtils.GetHouseholdIncome(...) + max(0, money)
    company   -> GetCompanyMaxProfitPerDay(...) when nonnegative, else GetCompanyTotalWorth(...)
      // a fallback, not a max: profit 5 beats worth 10000 (economy-and-companies)
  a company over budget gets PropertySeeker enabled and starts hunting; a household does not
      // what a seeking household then does is citizens-and-households' side of the line
  HighRentWarning raised when over 0.7 of renters are over budget (a bare literal in the job),
      the building is under capacity, is no extractor lot (ExtractorProperty), and
      CanDisplayHighRentWarnIcon finds no competing company or workforce notification and takes
      the LAST household's liveness verdict (its loop overwrites the result per household);
      the flag and icon are cleared again whenever the test fails, so the warning is live state
  also runs its own over-capacity eviction and the pollution notification pass

PropertyRenterSystem.PayRentJob (kUpdatesPerDay = 16, UpdateFrame bucket), per building:
  each renter pays RoundToIntRandom(PropertyRenter.m_Rent / 16)
      // stochastic rounding: the daily total is right in expectation and no single tick is
  a StorageCompany renter is drained of ALL its money instead
  drops renters whose PropertyRenter is gone; evicts from the buffer's end when the building is
      abandoned, destroyed or over capacity
  adds PropertyToBeOnMarket when under capacity, not abandoned or destroyed, not Signature,
      and not already PropertyOnMarket
  raises a RentersUpdated event entity when anything changed

PropertyProcessingSystem.PutPropertyOnMarketJob (interval 16):
  abandoned -> drop PropertyToBeOnMarket and stop
  otherwise recompute the asking rent and swap PropertyToBeOnMarket for PropertyOnMarket --
      unless PropertyOnMarket is already there, in which case it is REMOVED instead (an unlisting)
  a Signature building gets its company MANUFACTURED here, from the matching company prefab's
      archetype -- signature buildings do not wait for a company to find them
```

## Condition, level-up, abandonment

Sources: `src/Game/Game.Simulation/BuildingUpkeepSystem.cs`, `src/Game/Game.Buildings/BuildingUtils.cs`, `src/Game/Game.Prefabs/BuildingConfigurationData.cs`.

```
BuildingUpkeepSystem.BuildingUpkeepJob (kUpdatesPerDay = 16, UpdateFrame bucket), per building
with BuildingCondition, its query excluding Abandoned, Destroyed, Deleted, Temp and
ResourceNeeding -- Abandoned, Destroyed and ResourceNeeding freeze the condition,
Deleted and Temp are lifecycle exclusions:
  levelingCost = BuildingUtils.GetLevelingCost(...)      // the entry file's formula
  abandonCost  = BuildingUtils.GetAbandonCost(...)
  tick upkeep  = ConsumptionData.m_Upkeep / 16, split into a materials share (/ kMaterialUpkeep, = 4)
                 and a money share, the remainder
  renter worth = sum over the Renter buffer: a household's money, or a company's total worth
                 including its owned vehicles
  worth < money share -> condition -= m_BuildingConditionDecrement * pow(2, level) * max(1, renters)
  else, any renters   -> condition += m_BuildingConditionIncrement[areaType] * pow(2, level) * max(1, renters)
                         and each renter is charged an equal share of the money part
  m_DebugFastLeveling -> condition written straight to levelingCost, replacing either arm's write
  condition >= levelingCost:
    BuildingFlags.Historical -> condition pinned at levelingCost, nothing further
    else request level-up materials: ResourceNeeding and GuestVehicle buffers added, the material
        list resolved in order from LevelUpResourceData on the building prefab, then
        ZoneLevelUpResourceData on the zone prefab filtered to the current level, then the
        ZoneLevelUpResourceData buffer on the BuildingConfigurationData singleton entity,
        level-filtered the same way; the arm is chosen by buffer presence, so a chosen buffer
        with no entry for the current level requests nothing, and an empty request is already
        satisfied -- the building levels at once
  condition <= -abandonCost, prefab without SignatureBuildingData (the job re-checks Abandoned and
      Destroyed, redundantly -- the query above already excludes both):
    // this test reads the chunk value written back after both tests, so it sees the condition
    // one tick behind the decrement just applied
    Historical -> condition pinned at -abandonCost
    else queued for level-down, and the condition credited back by levelingCost

ResourceNeedingUpkeepJob (skips a building with no GuestVehicle buffer):
  every ResourceNeeding entry Delivered -> buffer removed, condition zeroed, level-up enqueued

LevelupJob:
  skip when the zone prefab's PrefabData is disabled
  SelectSpawnableBuilding: in the same BuildingSpawnGroupData zone chunk, a prefab at level + 1 with
      identical m_LotSize, height under the cell ceiling re-read from the zone cells under the
      footprint, identical left/right access flags, m_ResidentialProperties at least the current
      one, and identical m_AllowedManufactured, m_AllowedInput, m_AllowedSold, m_AllowedStored;
      matches reservoir-sampled at equal weight
  winner -> UnderConstruction { m_NewPrefab, m_Progress = 255 }   // the construction pipeline
      performs the swap
  fires a TriggerAction per area type present and a ZoneBuiltLevelUpdate { zone, from, to, squares }
      // the level-transition delta for the unlock tallies (districts-and-themes.md carries the feed)

LeveldownJob (abandonment):
  adds Abandoned { m_AbandonmentTime = frame } and Updated
  removes ElectricityConsumer, WaterConsumer, GarbageProducer, MailProducer
  doubles CrimeProducer.m_Crime, zeroing m_DispatchIndex in the same write
  removes PropertyRenter from every renter and empties the Renter buffer
  queues the road edge for update so the utility connections rebuild; clears every Problem and
      FatalProblem icon, swaps in the abandonment notification, and enqueues level-down
      TriggerActions for commercial and for industrial-or-office properties -- none for residential
```

**A level grants nothing intrinsic: level-up replaces the prefab.**
Everything a building "gains" is either authored on the level+1 prefab it swaps to or a formula term reading `SpawnableBuildingData.m_Level`, and because the swap needs a matching candidate, a zone height limit painted after construction can stop a building levelling ever again.
Source: `src/Game/Game.Simulation/BuildingUpkeepSystem.cs`.

**An abandoned building is destroyed on a timer, not kept.**
`DestroyAbandonedSystem` raises a destroy event once `Abandoned.m_AbandonmentTime` plus `BuildingConfigurationData.m_AbandonedDestroyDelay` elapses, and `DestroySystem` writes `Destroyed` from it — the same event path every other destruction source takes.
Source: `src/Game/Game.Simulation/DestroyAbandonedSystem.cs`, `src/Game/Game.Objects/DestroySystem.cs`.

**Upkeep is baked per prefab at initialisation and never recomputed.**
`PropertyRenterSystem.GetUpkeep` runs once, inside `BuildingInitializeSystem` — skipping a prefab that authors its own `ServiceConsumption` and any zone without `ZoneServiceConsumptionData` — into each remaining building prefab's `ConsumptionData.m_Upkeep`, so retuning `EconomyParameterData` or a zone's `ZoneServiceConsumptionData` after prefab initialisation changes nothing already written.
Source: `src/Game/Game.Prefabs/BuildingInitializeSystem.cs`, `src/Game/Game.Simulation/PropertyRenterSystem.cs`.

**A signature building's rent is computed at level 5 and its upkeep at level 2.**
`SignatureBuilding` writes `SpawnableBuildingData.m_Level = 5` (`kStatLevel = 5`, a `const`), but the upkeep bake substitutes level 2 for any prefab carrying `SignatureBuildingData`; the same prefabs never abandon, only `PayRentJob`'s listing gate excludes `Signature` (`AdjustRentJob`'s own listing path has no such guard).
Source: `src/Game/Game.Prefabs/SignatureBuilding.cs`, `src/Game/Game.Prefabs/BuildingInitializeSystem.cs`, `src/Game/Game.Simulation/BuildingUpkeepSystem.cs`, `src/Game/Game.Simulation/PropertyRenterSystem.cs`, `src/Game/Game.Simulation/RentAdjustSystem.cs`.

(VOLATILE: every system, job, component, field, flag and constant this file names, the two land-value shapes and the buffer indices most of all — their declarations in `Game.Simulation`, `Game.Buildings`, `Game.Prefabs`, `Game.Net`, `Game.Common`, `Game.Economy`, `Game.Companies`, `Game.Objects`, `Game.Triggers`, `Game.Vehicles` and `Game.Agents` under `src/Game/`, at the files the source lines cite.)
