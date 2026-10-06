# A daily scholarship that never pays out

## Kind

bug report

## Prompt

I'm making a Cities: Skylines II mod that hands out scholarships. Every in-game day, each teenager or adult living in my city who hasn't finished college yet should have a small chance of winning one, and the prize money should go to their household. The code is in this workspace. It builds and loads fine, but it never does anything: the log line I write when someone wins has never shown up, even after running a city of several thousand people for many in-game days.

## Checklist

### Age is read from `Citizen.GetAge()`, not from the `Child`/`Teen`/`Adult`/`Elderly` tag components

- Falls in: the query or the per-citizen test uses the `Teen`, `Adult`, `Child` or `Elderly` component types from `Game.Citizens`, through `EntityQueryDesc.Any`/`All`, `ComponentType.ReadOnly<Adult>()`, `HasComponent<Teen>` or the like.
- Avoids: the age test calls `citizen.GetAge()` and compares the returned `CitizenAge`, and no query names a tag component.
- Reference: cs2-modding/references/mechanics/citizens-and-households/citizens-and-households.md
- Source: src/Game/Game.Serialization/RequiredComponentSystem.cs:1468

### Education is read through `GetEducationLevel()`, not decoded from the state bits

- Falls in: the education level is computed from `Citizen.m_State` by shifting or masking, such as `((int)citizen.m_State >> 4) & 0x7`, or by testing `CitizenFlags.EducationBit1`/`EducationBit2`/`EducationBit3` and assembling a number.
- Avoids: the education test calls `citizen.GetEducationLevel()` and compares the returned int.
- Reference: cs2-modding/references/mechanics/citizens-and-households/citizens-and-households.md
- Source: src/Game/Game.Citizens/Citizen.cs:53

### `Citizen.m_PseudoRandom` is never written

- Falls in: the code assigns `citizen.m_PseudoRandom` (and writes the `Citizen` back) to advance, reseed or vary the roll, or writes it for any other reason.
- Avoids: the random source is a system-level one (`RandomSeed.Next().GetRandom(...)`, a `Unity.Mathematics.Random` the system owns) or a `Random` seeded from `m_PseudoRandom` mixed with the day number, as in `Random.CreateFromIndex((uint)(citizen.m_PseudoRandom + day))`, and no statement assigns `m_PseudoRandom`.
- Reference: cs2-modding/references/mechanics/citizens-and-households/citizens-and-households.md
- Source: src/Game/Game.Citizens/Citizen.cs:40

### Tourists and commuters are excluded

- Falls in: every matched citizen is a candidate; nothing tests `CitizenFlags.Tourist`/`CitizenFlags.Commuter` on the citizen, `HouseholdFlags.Tourist`/`HouseholdFlags.Commuter` on the household, or the `TouristHousehold`/`CommuterHousehold` components.
- Avoids: a citizen is skipped when its `m_State` carries `CitizenFlags.Tourist` or `CitizenFlags.Commuter`, or its household carries the matching `HouseholdFlags` bit or component.
- Reference: cs2-modding/references/mechanics/citizens-and-households/citizens-and-households.md
- Source: src/Game/Game.Citizens/HouseholdInitializeSystem.cs:108

### The prize is added to the household's money, not to `Household.m_Resources`

- Falls in: the grant is added to `Household.m_Resources` (or any other `Household` field) and the component written back with `SetComponentData`.
- Avoids: the grant goes to the `Resource.Money` entry of the household's `Game.Economy.Resources` buffer, through `EconomyUtils.AddResources(Resource.Money, amount, buffer)` or an equivalent write to that buffer element.
- Reference: cs2-modding/references/mechanics/citizens-and-households/citizens-and-households.md
- Source: src/Game/Game.Simulation/ResourceBuyerSystem.cs:234

## Roots

- decompile
