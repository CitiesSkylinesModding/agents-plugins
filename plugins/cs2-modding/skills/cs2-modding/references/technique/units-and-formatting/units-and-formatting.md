# Units and formatting

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The C# types below are checkable there; the frontend behaviour is not, and its ground truth is the game's own UI bundle.
`cs2-modding-setup` provisions it.

Rendering a quantity in the player's own units: the unit string a value carries, the formatters that unit selects, and the three interface preferences those formatters branch on.

The ground truth here is the game's own frontend bundle rather than the localization manager, and the reader need not be shipping translations at all.
A panel that renders a number, a tooltip that renders a duration and a stat row that renders a fraction all reach everything on this page without registering a dictionary source of their own, because the keys and separators a formatter reaches for are the game's and are already in the active dictionary.

`localization` owns producing the text the game will display — the dictionary source, the keys and the strings behind them; this reference owns the number that goes inside one.
`settings-and-input` owns the page a mod adds to the options screen; the three preferences below are the game's own, on the game's own interface page.
`binding-layer`, in the UI skill, owns the wire that carries a localized element from C# to the frontend; this reference owns what the far end does with one when it arrives.

`simulation-time-and-units`, in the mechanics family, owns what a unit **means** and how fast the clock runs.
This reference owns how a quantity is **rendered** to a player, and nothing about what the quantity is worth in the simulation.

## The elements C# has, and what they leave to the frontend

**C# offers exactly four localized element types**: `LocalizedString`, `LocalizedNumber<T>`, `LocalizedFraction<T>` and `LocalizedBounds<T>`, all implementing an empty `ILocElement` that extends `IJsonWritable`.
There is **no C# percentage, date, duration or time element, and no C# formatting function of any kind**.
Each numeric element writes its raw value, an optional unit **string** and one flag; the frontend does all the work.

So constructing one is the value and the unit, and nothing else:

```csharp
new LocalizedNumber<int>(n, Unit.kInteger)
```

## The unit is a string, and the two sides carry different lists

`Game.UI.Unit` is a static class of 33 `const string`s, from `kInteger = "integer"` to `kCustom = "custom"`.
The frontend's own `Unit` enum carries **38**: those 33 plus `PercentagePrecise`, `BodiesPerMonth`, `TemperaturePrecise`, `Height` and `DurationSeconds`.
Those five have no C# constant, so reaching them from C# means writing the literal string.

(VOLATILE: the unit constant list on both sides — the UI unit static class, and the frontend's own unit enum.)

**The frontend's number formatter is a lookup table with a visible fallback.**
An unrecognised unit renders the number followed by the unit name in angle brackets — `1234 <myUnit>` — which is the symptom of a typo'd unit string and the reason a mis-spelled unit never throws.

The table is where the player's preferences bite:

- `Integer` renders plain and thousands-separated; `IntegerRounded` switches to a thousand-suffixed key above 1,000 and a million-suffixed one above 1,000,000.
- `Length` renders metres below 1,000 and kilometres above for a metric player, yards below 1,609 and miles above for a freedom-units player.
- `Area`, `Volume`, `Weight`, `WeightPerCell`, `WeightPerMonth`, `Height`, `NetElevation`, `MoneyPerDistance` and `MoneyPerDistancePerMonth` all branch on the unit system the same way.
- `Temperature` branches on the temperature preference across Celsius, Fahrenheit and Kelvin.
- The two `*Precise` units render as `PercentageSingleFraction` and `Temperature` do — same key, and `TemperaturePrecise` branches on the temperature preference exactly as `Temperature` does — and differ only in precision, at two fraction digits against one for `PercentageSingleFraction` and none for `Temperature`.
- `Power` divides the raw value by 10 and renders kilowatts below 10,000, and divides by 10,000 for megawatts above it — so **the value C# passes is in units of 100 W**.
- `Money` has no unit-system branch at all.

Most branches render through a `Common.VALUE_*` key, and the separators come from the dictionary too — a thousands-separator key and a decimal-separator key, applied per call.
The sign prefix is `-` for a negative value always, and `+` for a positive one or `±` for zero **only when the signed flag is set**; otherwise both are empty.

**Fractions and bounds support far fewer units than numbers, and fail ugly outside them.**
`LocalizedFraction` handles eleven — `Volume`, `VolumePerMonth`, `Weight`, `WeightPerMonth`, `Power`, `Energy`, `BodiesPerMonth`, `XP`, `Integer`, `IntegerPerMonth`, `IntegerRounded` — and renders `${value} / ${total} <${unit}>` for anything else.
`LocalizedBounds` handles three — `Power`, `PercentageSingleFraction`, `Temperature` — renders `${min}–${max} <${unit}>` otherwise, and short-circuits to a plain number when min equals max.
Both default to `Integer` when no unit is given.

**`BodiesPerMonth` is the one unit with a fraction entry and no number entry** — `LocalizedFraction` renders it, `LocalizedNumber` prints the angle-bracket fallback.

## Percentage, date and duration exist only on the frontend

`LocalizedPercentage(value, max)` computes `100 * value / max` and renders it as a percentage-unit number — but **clamps any positive result to a minimum of 1**, so 0.2% displays as 1%, and a value or max at or below zero renders 0.

`LocalizedDate({ year, month })` renders a medium-date-format key with the month resolved through the indexed key `Common.MONTH_SHORT:<month>`.
The month is **zero-based**, and a game year is twelve days, so a day _is_ a month — the game's own producer passes `dayOfYear - 1` as the month.

`LocalizedDuration({ value, daysPerYear, maxMonths })` takes a value **in days** and picks a years key at or above `maxMonths` (defaulting to `daysPerYear`), a months key above one, a month key above 23.5/24 of a day, and otherwise falls through to a time-format key with hours and minutes derived from the fraction.

**There is no exported way to display a time of day, and the reason is an export list rather than a missing feature.**
The game has `LocalizedTime`, `LocalizedDateTime` and `LocalizedTimestamp`, plus time-format, date-format and number-formatting hooks, and the time component already branches on the player's 12/24-hour preference.
The public l10n module exports **eleven names and no more**: `Localized`, `LocalizedBounds`, `LocalizedDate`, `LocalizedDuration`, `LocalizedEntityName`, `LocalizedFraction`, `LocalizedNumber`, `LocalizedPercentage`, `LocalizedString`, `Unit` and `useLocalization`.
So formatting a time by hand from the player's preference is the answer for the public module, and reaching the real component is the same errand as reaching any other unexported one, which is `frontend-and-injection`'s material.

**One enum is exported and three are not.**
`Unit` **is** exported and its members are real string values, so `Unit.Money` works at runtime.
The time-format, temperature-unit and unit-system enums are **not** on that list even though the type declaration declares all three, so from the public module their values are written as literals: 24-hour `0` / 12-hour `1`, Celsius `0` / Fahrenheit `1` / Kelvin `2`, metric `0` / freedom `1`.
All three are registered in the frontend's module registry, so a mod already reaching in there gets the live enums instead — `frontend-and-injection` owns that route.

## The player's unit and format preferences

Three enums live on the interface settings class, all under one group of the interface options page:

- `TimeFormat { TwentyFourHours, TwelveHours }`
- `TemperatureUnit { Celsius, Fahrenheit, Kelvin }`
- `UnitSystem { Metric, Freedom }`

with defaults `TwentyFourHours`, `Celsius` and `Metric`.

**From C#**, read them off `SharedSettings.instance.userInterface`.
**From the frontend**, they arrive as one unit-settings struct on the options binding group, and `useLocalization()` returns `{ translate, unitSettings }` — so a component has them without declaring a binding of its own.

The practical answer is that **a mod formatting through `LocalizedNumber` with the right unit never reads them at all**: the formatter branches on them itself.
Read them directly only for the cases the unit table does not cover — a time of day, most of all.

(VOLATILE: the three preference enums and their member order — the interface settings class.)

## What this reference hands to others

`localization` owns the string a formatted number lands inside, and the seam is the unit string: a `LocalizedNumber` carries a raw value and a unit, and the key it substitutes into is that reference's material.
It also owns the inline format spec, which is the shortest route across the seam — a placeholder written `{AMOUNT:Money}` in a translation formats through the table above with no C# formatting code at all, and `localization`'s argument-placeholder section is where its syntax, including the `signed` suffix, is written down.

`settings-and-input` owns the mod's own options page, which reaches this reference twice: a slider declares a `unit` from the same list, and the custom-format attribute replaces it with `"custom"` and hands the numeric formatting to the frontend.

`simulation-time-and-units` is the mechanics topic this surface feeds most directly, and the seam is the unit table: a unit string decides whether a quantity is converted for a freedom-units player, and what that quantity was worth before it reached a formatter is that reference's material.
The calendar crosses on a binding rather than on an element — the frontend derives a date from the time binding group rather than from anything a localized element carries.

Every other mechanics reference bridges here through the same door: a number it teaches a mod to change is a number a panel eventually renders, and the unit string is what decides how.

`binding-layer`, in the UI skill, carries every localized element across the wire — the four element types are `IJsonWritable` structs whose type names are what the frontend switches on.

`frontend-and-injection`, in the UI skill, owns the two errands this topic generates: the time and date formatters the public l10n module does not export, and the module registry entry that hands a mod the three live preference enums instead of their literal values.
