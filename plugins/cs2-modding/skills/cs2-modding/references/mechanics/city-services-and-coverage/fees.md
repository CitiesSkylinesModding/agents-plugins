# Service fees

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A fee runs in two directions: it is charged to households, and at a few hand-written sites it changes behaviour.
The fees live in `Game.City.ServiceFee { PlayerResource m_Resource; float m_Fee; }`, a buffer on the city entity, read through the static `ServiceFeeSystem.GetFee(resource, fees)` — which returns 0 for a missing member — and written with `SetFee`.
`Game.Prefabs.ServiceFeeParameterData` declares one `FeeParameters { m_Default, m_Max, m_Adjustable }` field per fee-bearing `PlayerResource`.
Source: `src/Game/Game.City/ServiceFee.cs`, `src/Game/Game.Simulation/ServiceFeeSystem.cs`, `src/Game/Game.Prefabs/ServiceFeeParameterData.cs`.

**`FeeParameters.m_Adjustable` gates the panel, not the write: `ServiceFeeSystem.SetFee` applies no clamp and no adjustability test.**
The panel renders a slider (and its reset button) only for fees whose `m_Adjustable` is set (`s.fees.filter((e) => e.adjustable)` in the bound data), so the bool decides what a player can move, while a mod may write any fee the city buffer carries, past `m_Max` included — `SetFee` edits an existing `ServiceFee` member and silently writes nothing for a resource the buffer lacks, and `GetDefaultFees()` seeds the nine members it ever carries — so check the `ServiceFeeParameterData` singleton's `FeeParameters` fields before building against a fee slider.
Source: `src/Game/Game.Simulation/ServiceFeeSystem.cs`, `src/Game/Game.Prefabs/ServiceFeeParameterData.cs`, the game's UI bundle (the fee rows' `adjustable` filter in the `service-detail.tsx` module of the reformatted copy).

## Direction one: the charge

`ServiceFeeSystem.PayFeeJob` walks each `Game.City.ServiceFeeCollector` building's `Patient` and `Student` buffers and bills the occupant's household `(int)round(GetFee(resource, fees) / 128)` per update, queueing a `FeeEvent`; against the system's 2048-frame interval the divisor turns a per-day price into a per-charge amount.
**The debit is the rounded integer while the fee income the city books is the nominal fee times charges, unrounded** — a fee small enough to round to a zero debit charges nothing and still shows up as income.
The education resource is picked from the student's level (`GetEducationResource`); utility and garbage fees are billed elsewhere, by `UtilityFeeSystem` ([`economy-and-companies`](../economy-and-companies/economy-and-companies.md) owns the fee machinery's money half).
Source: `src/Game/Game.Simulation/ServiceFeeSystem.cs`.

## Direction two: the behaviour

The generic dispatchers — `GetConsumptionMultiplier`, `GetEfficiencyMultiplier`, `GetHappinessEffect` — exist only to feed the budget panel (every caller is in `ServiceBudgetUISystem`): the two multipliers return 1 for every resource except `Electricity` and `Water`, while `GetHappinessEffect`'s default arm returns a constant additive 1; simulation reads a fee only where a system reads it by hand, and inside this topic there are two such services:

**Education: a higher fee makes students drop out.**
`GraduationSystem` reads the fee into `GetDropoutProbability(level, lastCommuteTime, fee, …)`, raises the per-check probability to `1 - saturate(1 - p)^32` for the real roll (only for students at level 3 and up, after three failed graduations the student leaves regardless), and `SchoolAISystem` calls the same pair to write the panel's projection.
Source: `src/Game/Game.Simulation/GraduationSystem.cs`, `src/Game/Game.Simulation/SchoolAISystem.cs`.

**Healthcare: for an untracked health event and an earning household, a fee at the defaults' scale suppresses refusals entirely; a zero fee, or a zero-income household, produces them.**
Suppression is the inequality `fee / 2 × income > 10 / health` — the fee term is *subtracted*, scales with household income, and has no graduated elasticity behind it — and the unclamped write flips it: at a negative fee every earning household refuses care.
Source: `src/Game/Game.Simulation/SicknessCheckSystem.cs`, `src/Game/Game.Simulation/HealthProblemSystem.cs`.

`SicknessCheckSystem.CreateHealthEvent` computes the chance a new patient refuses care as

```
p(NoHealthcare) = 10 / citizen.m_Health  -  fee / 2 * householdIncome
```

— at a fee of exactly 0, or for a household with no income, the expression reduces to `10 / health`; a health-event prefab with `HealthEventData.m_RequireTracking` (the authoring class's default) returns before this roll entirely and never reads the fee; `NoHealthcare` is what makes `HealthProblemSystem` skip the treatment path.
Source: `src/Game/Game.Simulation/SicknessCheckSystem.cs`, `src/Game/Game.Prefabs/HealthEvent.cs`.

**Fire response and police have no fee in either direction.**
`PlayerResource.FireResponse` and `PlayerResource.Police` appear in the fee-parameter classes and a trigger arm each in `ServiceFeeSystem`, and in no billing or behavioural site (re-check: grep `src/Game/` for the qualified member names).
Source: `src/Game/Game.City/PlayerResource.cs`, `src/Game/Game.Simulation/ServiceFeeSystem.cs`.

## Three sets of defaults, one operative

- `ServiceFee.GetDefaultFee` is a C# switch (`BasicEducation 100, SecondaryEducation 200, HigherEducation 300, Healthcare 100, Garbage 0.1, Electricity 0.2, Water 0.1`, default 0) — reached from `Deserialize` under `Purpose.NewGame`, from the garbage migration, and from a back-fill that adds a missing water entry.
- The **prefab's** `FeeParameters.m_Default` fields are what a new city actually gets: `CitySystem` fills the buffer from `GetDefaultFees()` on creation, a save missing the buffer entirely is refilled the same way, and the panel's reset button restores `m_Default`. Read live, several differ from the C# switch — the prefab owns the value, and the re-check is reading the `ServiceFeeParameterData` singleton's `FeeParameters.m_Default` fields beside `ServiceFee.GetDefaultFee`.
- **A fee read out of a running game is the save's number**, not either default: `Deserialize` also rewrites old saves' water fee to the literal `0.3` below `Version.waterFeeReset` and re-defaults garbage below `Version.garbageFeeReset`, and a player may have moved anything movable.

Source: `src/Game/Game.City/ServiceFee.cs`, `src/Game/Game.Simulation/CitySystem.cs`, `src/Game/Game.Serialization/RequiredComponentSystem.cs`.

The fee slider is a 200-step lerp: the panel sends `lerp(min, max, t / 200)` and displays `200 * (fee - min) / (max - min)` with `max` from `FeeParameters.m_Max`; the C# side is the `"serviceBudget"` binding group's `setServiceFee` trigger on `ServiceBudgetUISystem`.
Source: `src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs`, the game's UI bundle (module `service-fee-slider-item.tsx`).

(VOLATILE: every component, field, enum member, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.City`, `Game.Simulation`, `Game.Prefabs`, `Game.Buildings`, `Game.Serialization` and `Game.UI.InGame`, at the files each passage cites, the C# default-fee switch and the migration constants included; plus the two UI-bundle module names, against the reformatted bundle copy; plus the live-read claim that shipped defaults differ from the C# switch, against the `ServiceFeeParameterData` singleton's `FeeParameters.m_Default` fields.)
