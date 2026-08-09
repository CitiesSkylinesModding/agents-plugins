# What a power value actually renders as

## Prompt

A Cities: Skylines II code mod displays an electricity figure in the game UI by constructing `new LocalizedNumber<int>(5000, Unit.kPower)`. On a default game profile, what magnitude and what unit does the player actually see rendered? State what physical quantity one raw count of that value corresponds to, and the raw value at which the displayed unit changes.

## Verified answer

The player sees **500 kW**, not 5000. One raw count is **100 W** (0.1 kW), and the displayed unit switches to megawatts at a raw absolute value of 10,000 — so raw 10,000 renders as `1 MW`.

All of the arithmetic lives in the shipped frontend bundle, none of it in C#.

The formatter's power arm, in the reformatted UI bundle copy (`src-ui/source.js`):

- `:29220` — `        [Ic.Power]: (e, t, n) =>`
- `:29221` — `          Math.abs(t) < 1e4`
- `:29222` — `            ? qc(e, t / 10, Sc.Common.VALUE_KILOWATT, n, 1)`
- `:29223` — `            : qc(e, t / 1e4, Sc.Common.VALUE_MEGAWATT, n, 2),`

There is no unit-system branch here, unlike `Length`, `Area` and `Weight`: metric and US profiles render power identically. The threshold tests `Math.abs`, so the switch is symmetric around zero, which matters for signed battery flow.

Raw 5000 takes the kilowatt branch and becomes 500. In `qc` (`source.js:29288`) the default rounding cutoff is `r = 100`, and 500 exceeds it, so the value prints through `l = c.toFixed(0)` (`:29296`) as `500`. Raw 10,000 takes the megawatt branch and becomes 1, which is under the cutoff and so goes through `toFixed(2)` to `"1.00"`, then the trailing-zero regex at `:29286` trims it to `1`.

The C# half carries no scaling at all. `src/Game/Game.UI/Unit.cs` is a `public static class` of 33 `public const string` members with no methods and no arithmetic; `:39` is `public const string kPower = "power";`. A search of `src/Game` for `kilowatt`, `megawatt` or `watt` returns nothing, and call sites pass the raw integer straight through — for instance `Game.UI.Tooltip/RaycastElectricityTooltipSystem.cs:214`, `m_Production.value = component4.m_Capacity;`.

Two further formatter tables carry their own power arms — bounds at `source.js:29504` and fractions at `:29585` — and both threshold on the max/total argument rather than on the value, so a small value inside a large bound renders in MW.

## Rubric

- 4: Says the player sees 500 rather than 5000 — the raw value is divided by 10 — and that the unit is kilowatts.
- 3: States that one raw count corresponds to 100 W (equivalently 0.1 kW).
- 3: Gives the unit change at a raw value of 10,000, above which the value is divided by 10,000 and rendered as megawatts.

## Roots

- decompile
- ui-bundle
