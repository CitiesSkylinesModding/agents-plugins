# Tourism and lodging

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Attractiveness is a city score summed from provider buildings; the score sets a tourist target and a spawn probability; tourists rent hotel rooms and spend through the ordinary sale paths.

## Attractiveness

Sources: `src/Game/Game.Simulation/TourismSystem.cs`, `src/Game/Game.Simulation/AttractionSystem.cs`.

```
TourismSystem (interval a flat 32768 -- 8 passes a game day):
  sum = Σ over every AttractivenessProvider of m_Attractiveness² / 10000
  attractiveness = 200 / (1 + exp(-0.3 * sum)) - 100      // bounded at 100 before the modifier
  CityModifierType.Attractiveness applied
  Tourism.m_Attractiveness = round(attractiveness)
  Tourism.m_Lodging = (renting tourist households, renters + m_FreeRooms) summed over hotels with a property -- the total is reconstructed from a m_FreeRooms the lodging system wrote on its own last pass, so it can lag a pass behind
AttractionSystem (interval 16), per provider building:
  start from the prefab's AttractionData.m_Attractiveness, add installed upgrades' own
  multiply by the building's efficiency -- UNLESS it is a Signature building
  park: multiply by 0.8 + 0.2 * (Park.m_Maintenance / ParkData.m_MaintenancePool) (a zero maintenance pool counts as ratio 1)
  multiply by 1 + 0.01 * TerrainAttractivenessSystem.EvaluateAttractiveness(position)
```

Squaring each provider before summing means one landmark beats many small parks of the same total, and a signature building's attractiveness ignores its own efficiency while everything else's does not.

## Tourist volume

Sources: `src/Game/Game.Simulation/TourismSystem.cs`, `src/Game/Game.Prefabs/AttractivenessParameterData.cs`.

```
GetTargetTourists(a)  = a <= 100 ? a * 15 : 1500 + round(100 * log10(1 + (a - 100)))
                        // linear to 1500 at 100, logarithmic above
GetSpawnProbability(a, current):
  target = GetTargetTourists(a); padded = target * 110 / 100
  current >= target      -> a / 1000
  current/padded < 0.5   -> 1
  else t = 1 - (current/padded - 0.5) / 0.5 -> saturate(1.5 * t²)
GetWeatherEffect: 1 + ONE temperature term (an if/else-if chain: m_TemperatureAffect.x peaking at the centre of the m_AttractiveTemperature band and fading to 0 at its edges, OR 0 to .y across 10 degrees past either m_ExtremeTemperature end) + ONE precipitation term (snow, else rain, across m_SnowEffectRange / m_RainEffectRange) + m_SnowRainExtremeAffect.z when Stormy, then clamp to [0.5, 1.5]
GetTouristProbability = GetSpawnProbability * GetWeatherEffect
Tourism.m_AverageTourists = round(2 * GetTouristProbability * 100000 / 16)  // display figure, not a count
```

The weather terms' coefficients are `AttractivenessParameterData` fields; every other number in this section is a C# literal.
`GetTouristRandomStay()` returns the literal 262144 — one game day — and has no caller in `src/`, so it states an intent rather than a mechanism.
`citizens-and-households` owns the tourist household itself; `TouristSpawnSystem` rolls this probability per pass.

## Lodging

Sources: `src/Game/Game.Simulation/LodgingProviderSystem.cs`, `src/Game/Game.Simulation/CitizenPathfindSetup.cs`.

A hotel is any `ProcessingCompany` prefab whose output is `Lodging` — that condition alone adds `LodgingProvider` and a `Renter` buffer to the archetype (`src/Game/Game.Prefabs/ProcessingCompany.cs`).

```
LodgingProviderSystem (kUpdatesPerDay = 32, UpdateFrame), per hotel with a property:
  roomCount = (int)(lotSize.x * lotSize.y * buildingLevel * BuildingPropertyData.m_SpaceMultiplier)
    // rooms are the building's, not the company's, and scale linearly with level
  evict any renter that is not a TouristHousehold, then any renter past roomCount (clearing its TouristHousehold.m_Hotel)
  charge = LeisureParametersData.m_TouristLodgingConsumePerDay / kUpdatesPerDay * marketPrice(Lodging)
  each tourist household pays (int)charge -- truncated -- while the hotel is credited round(charge * renters), so the pass is not a conserving transfer
  the hotel's Lodging stock drops by the room-nights consumed, with no floor, so it can go negative; ServiceAvailable drops the same amount floored at 0
  m_Price = (int)(charge * kUpdatesPerDay); m_FreeRooms = roomCount - renters
a LodgingProvider with NO property instead clears its whole Renter buffer without clearing any renter's m_Hotel, leaving those tourists pointing at a hotel they no longer rent
```

A hotel with no `Lodging` stock still charges; its `ServiceAvailable` floors at 0, and the commercial production taper then lets it restock ([production-and-profit.md](production-and-profit.md)).
A lodging seeker (a tourist household with no hotel yet) weighs targets in `CitizenPathfindSetup`: a hotel with `m_FreeRooms == 0`, no property, or an inactive building is skipped; a qualifying hotel starts 10,000 cost units ahead of every non-hotel target (−5000 against +5000), and inside that band the cost weighs `-10 * m_FreeRooms` against `min(m_Price, 500)`, minus a road-edge attractiveness term.

## Where the money lands

Sources: `src/Game/Game.Citizens/HouseholdInitializeSystem.cs`, `src/Game/Game.Simulation/HouseholdMoveAwaySystem.cs`.

**Tourist income is a statistic, not a transfer: money brought in minus money taken out.**
`StatisticType.TouristIncome` is credited the household's starting money at spawn and debited whatever it still holds at leave; everything spent in between reaches companies through `ResourceBuyerSystem`, `LeisureSystem` and `LodgingProviderSystem`, so there is no tourist-specific revenue channel to hook.
Source: `src/Game/Game.Citizens/HouseholdInitializeSystem.cs`, `src/Game/Game.Simulation/HouseholdMoveAwaySystem.cs`.

Tourist starting wealth and the tourist consumption multiplier are `citizens-and-households`' fields.

(VOLATILE: every system, component, field, formula and `Source:` path this file names — their declarations in `Game.Simulation`, `Game.City`, `Game.Companies`, `Game.Citizens`, `Game.Buildings` and `Game.Prefabs` under `src/Game/`, at the files the sections cite.)
