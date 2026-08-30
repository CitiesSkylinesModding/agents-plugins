# Units

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Nothing in the decompile says what a C# value crossing to the frontend is denominated in: `Game.UI.Unit` (`src/Game/Game.UI/Unit.cs`) is a static class of opaque `const string`s such as `kInteger = "integer"` and `kPower = "power"`, a `LocalizedNumber<T>` (`src/Game/Game.UI.Localization/LocalizedNumber.cs`) carries one, and the frontend dispatches on it.
The formatter table in the shipped UI bundle — reached through `game-ui/common/localization/localized-number.tsx`, dispatching on the `Unit` enum `unit.ts` exports — is the only first-party statement of the divisor and threshold that turn a raw value into kilowatts or tonnes; rendering one yourself belongs to [`units-and-formatting`](../../technique/units-and-formatting/units-and-formatting.md).

## The unit table

Source: the shipped UI bundle's `localized-number.tsx` formatter table, the `unit.ts` enum and `units-us-customary.ts`; the metric branch applies under `InterfaceSettings.UnitSystem.Metric` (`src/Game/Game.Settings/InterfaceSettings.cs`), the other under `Freedom` — except the temperature row, which switches on `TemperatureUnit` alone — and `VALUE_*` names are the `Common` localization keys the result renders into.

| Unit string | Metric | Freedom | So the C# value is in |
| --- | --- | --- | --- |
| `integer` | the code-local `DefaultFormat`, 0 fraction digits, thousands-separated | — | whatever it is |
| `integerRounded` | `<1e3` plain; `<1e6` `/1e3` `VALUE_THOUSAND`, 1 digit; else `/1e6` `VALUE_MILLION`, 1 digit | — | |
| `integerPerMonth` | `VALUE_PER_MONTH` | — | per in-game day |
| `integerPerHour` | `VALUE_PER_HOUR` | — | per in-game hour |
| `floatSingleFraction` / `floatTwoFractions` / `floatThreeFractions` | 1 / 2 / 3 fraction digits | — | |
| `percentage` / `percentageSingleFraction` / `percentagePrecise` | `VALUE_PERCENT`, 0 / 1 / 2 digits | — | percent, already ×100 |
| `angle` | `VALUE_ANGLE`, 1 digit | — | degrees |
| `length` | `<1e3` `VALUE_METER`, 1 digit; else `/1e3` `VALUE_KILOMETER`, 1 digit | `<1609` `yards(v)` `VALUE_YARD`; else `miles(v / 1e3)` `VALUE_MILE`, 1 digit | metres |
| `height` | `VALUE_METER`, 0 digits | `feet(v)` `VALUE_FOOT`, 0 digits | metres |
| `netElevation` | `VALUE_METER`, 2 digits | `3 * v` `VALUE_FOOT`, 2 digits | metres |
| `area` | `<1e5` `VALUE_SQUARE_METER`, 0 digits; else `/1e6` `VALUE_SQUARE_KILOMETER`, 1 digit | `<1e5` `squareFeet(v)` `VALUE_SQUARE_FOOT`; else `acres(v)` `VALUE_ACRE`, 1 digit | square metres |
| `volume` | `VALUE_CUBIC_METER`, 1 digit | `gallons(v)` `VALUE_GALLON`, 0 digits | cubic metres |
| `volumePerMonth` | `VALUE_CUBIC_METER_PER_MONTH`, 1 digit | `gallons(v)` `VALUE_GALLON_PER_MONTH`, 1 digit | cubic metres per in-game day |
| `weight` | `<100` `VALUE_KILOGRAM`, 1 digit; `<1e6` `/1e3` `VALUE_TON`, 2 digits; else `/1e6` `VALUE_KILOTON`, 2 digits | `<100` `pounds(v)` `VALUE_POUND`, 1 digit; `<9071847.4` `shortTons(v)` `VALUE_SHORT_TON`, 2 digits; else `shortTons(v) / 1e3` `VALUE_SHORT_KILOTON`, 2 digits | kilograms |
| `weightPerCell` | `/1e3` `VALUE_TON_PER_CELL`, 2 digits | `shortTons(v)` `VALUE_SHORT_TON_PER_CELL`, 2 digits | kilograms per cell |
| `weightPerMonth` | `<100` `VALUE_KG_PER_MONTH`, 1 digit; else `/1e3` `VALUE_TON_PER_MONTH`, 2 digits | `<100` `pounds(v)` `VALUE_POUND_PER_MONTH`, 1 digit; else `shortTons(v)` `VALUE_SHORT_TON_PER_MONTH`, 2 digits | kilograms per in-game day |
| `power` | `<1e4` `/10` `VALUE_KILOWATT`, 1 digit; else `/1e4` `VALUE_MEGAWATT`, 2 digits | — | hundreds of watts |
| `energy` | `/1e4` `VALUE_MEGAWATT_HOURS`, 1 digit | — | hundreds of watt-hours |
| `dataRate` | `VALUE_GIGABIT_PER_SECOND`, 1 digit | — | gigabits per second |
| `dataBytes` | binary ladder over byte, kilobyte, megabyte, gigabyte, terabyte | — | bytes |
| `dataMegabytes` | `1024 * v * 1024`, then the ladder from megabyte | — | megabytes |
| `money` / `moneyPerCell` / `moneyPerMonth` / `moneyPerHour` | `VALUE_MONEY` 0 digits / `VALUE_MONEY_PER_CELL` 1 digit / `VALUE_MONEY_PER_MONTH` / `VALUE_MONEY_PER_HOUR` | — | currency; per in-game day; per in-game hour |
| `moneyPerDistance` / `moneyPerDistancePerMonth` | `VALUE_MONEY_PER_KILOMETER` / `…_PER_MONTH` | `/1.6` `VALUE_MONEY_PER_MILE` / `…_PER_MONTH` | per kilometre; per in-game day |
| `xp` | `VALUE_XP`, 0 digits | — | |
| `temperature` / `temperaturePrecise` | `VALUE_CELSIUS`, 0 / 2 digits | `fahrenheit(v)` `VALUE_FAHRENHEIT` or `kelvin(v)` `VALUE_KELVIN` by `TemperatureUnit`, 0 / 2 digits | degrees Celsius |
| `screenFrequency` | 3 digits, no thousands separator | — | hertz |
| `durationSeconds` | `VALUE_SHORT_SECOND`, 0 digits | — | real seconds |
| `custom` | the caller's own format and fraction-digit arguments | — | |
| `bodiesPerMonth` | no entry in the value table; a fraction entry only | — | belongs to [`units-and-formatting`](../../technique/units-and-formatting/units-and-formatting.md) |

```
fahrenheit(v) = 9 * v / 5 + 32        kelvin(v)     = v + 273.16
gallons(v)    = 264.172 * v           pounds(v)     = v / 0.45359237
shortTons(v)  = v / 907.18474         yards(v)      = v / 0.9144
miles(v)      = v / 1.609344          squareFeet(v) = v / 0.092903
acres(v)      = v / 4046.873          feet(v)       = 3.28084 * v
fromFeet(v)   = v / 3.28084
```

The `273.16` is the triple point rather than absolute zero; a mod matching the vanilla display reproduces it rather than correcting it.
A unit not in the table renders as `` `${value} <unit>` ``.

**`netElevation`'s Freedom branch multiplies by `3`, not by `feet()`'s `3.28084`.**
Every other length unit goes through `feet()`.
Source: the shipped UI bundle's `localized-number.tsx` formatter table.

## The C# side's own conventions

Speed is km/h on the authoring class and m/s on the component: `AnimalPrefab`, `AircraftPrefab` and `AirplanePrefab` divide by `3.6f` writing `m_MoveSpeed`, `m_GroundMaxSpeed` and `m_FlyingSpeed`, and [`roads-and-traffic`](../roads-and-traffic/roads-and-traffic.md) and [`transportation-and-vehicles`](../transportation-and-vehicles/transportation-and-vehicles.md) carry the road and vehicle sites (`src/Game/Game.Prefabs/AnimalPrefab.cs`, `src/Game/Game.Prefabs/AircraftPrefab.cs`, `src/Game/Game.Prefabs/AirplanePrefab.cs`).
Angles are degrees on the class and radians on the component, for vehicle turning and for `ClimatePrefab.sunLimitRadians` alike (`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs`).
Durations are days, real seconds or frames, told apart only by the multiplier at the use site ([cadence.md](cadence.md)).

(VOLATILE: every class, enum, constant, field and `Source:` path this file names — their declarations under `src/Game/` in `Game.UI`, `Game.UI.Localization`, `Game.Settings`, `Game.Prefabs` and `Game.Prefabs.Climate`, at the files the sections cite; and the unit enum, the formatter table, the conversion functions and the module paths — the shipped UI bundle's `unit.ts`, `localized-number.tsx` and `units-us-customary.ts` registrations.)
