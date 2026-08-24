# Registry paths other references send a reader here for

Verified against game version 1.6.0f1.

**Read this with the game install open.**
Every path and export name below is checkable only against its `Cities2_Data/Content/Game/UI/index.js`, read as the reformatted copy `cs2-modding-setup` records; nothing here names game C#.
The toolchain's environment variables locate it.

Each entry is a module path registered in the bundle, reached through `getModule(path, name)` — exposed on `window` as `cs2/modding` — or through the `findModule` census `frontend-and-injection.md` describes (VOLATILE: every path and export name in this file — the bundle's `add` registrations in `Cities2_Data/Content/Game/UI/index.js`).

## The data-binding module

`game-ui/common/data-binding/binding.ts` exports everything `cs2/api` does plus `bindMapPersistent`, which is registry-only.

## The typed renderer

`game-ui/common/typed-renderer/typed-renderer.tsx` exports `TypedRenderer`, `TypedListRenderer`, `renderTyped`, `entityKeyProvider` and `UnknownElement`.
`UnknownElement` is the failure box — a red `div` with yellow text reading `Unknown element type <typeName>` — and the two renderer components draw it whenever a payload's `__Type` is not a key of the map they were handed; `renderTyped` returns `undefined` silently instead (VOLATILE: the box text — the bundle's typed renderer).
`game-ui/widgets/components/widget-renderer.tsx` exports `WidgetRenderer`, `WidgetListRenderer` and `WidgetComponentMapContext`, and draws the same box for an unknown widget type; it is what draws a mod's settings page.

## The `Loc` dictionary

`game-ui/common/localization/loc.generated.ts` exports one name, `Loc`, built by `createLocDictionary` in `game-ui/common/localization/loc-dictionary.tsx` from a two-level object of key-shape constructors.
The four constructors encode the four key shapes:

- plain id — `Loc.Common.VALUE_YEARS`;
- hashed, rendering `<id>[<hash>]` — `Loc.Assets.NAME`;
- indexed, rendering `<id>:<index>` — `Loc.Assets.CITY_NAME`;
- hashed-and-indexed, rendering `<id>[<hash>]:<index>`.

Each entry is a memoised React component carrying `displayName`, `renderString` and `propsAreEqual`, made by `createLocComponent` in `game-ui/common/localization/loc-component.tsx`: `Loc.Assets.CITIZEN_NAME_FORMAT` takes `FIRST_NAME` and `LAST_NAME` props, and `renderString(localization, props)` is the escape hatch for a plain string.
Every component also accepts `fallback` and `showIdOnFail`.

## The formatters `cs2/l10n` withholds

`game-ui/common/localization/localized-date.tsx` exports `LocalizedDate`, `useDateFormat`, `formatDate`, `LocalizedTime`, `useTimeFormat`, `LocalizedDateTime`, `formatDateTime` and `LocalizedTimestamp`; `cs2/l10n` re-exports only `LocalizedDate`.
`game-ui/common/localization/localized-duration.tsx` exports `LocalizedDuration`, which `cs2/l10n` does re-export.

## The unit enums

`game-ui/menu/data-binding/options-bindings.ts` exports `TimeFormat`, `TemperatureUnit`, `UnitSystem` and `defaultUnitSettings`, beside `OptionsWidgetType`, `RebindOptions`, `ModifierOptions` and `BindingConflict`.
The same module carries dead accessors over its binding objects, so read the enums here and the binding values through `engine`.
`game-ui/common/localization/unit.ts` exports `Unit`, from `Integer` to `DurationSeconds`; `units-us-customary.ts` and `localized-number.tsx` beside it hold the conversions and the number component.

## The time bindings

`game-ui/game/data-binding/time-bindings.ts` holds the tick arithmetic; its `timeSettings` export is a dead accessor like the rest of its group, so the values read through `engine`.

## The panels

Under `game-ui/game/components/`: `progression/progression-panel/progression-panel.tsx`, `statistics-panel/statistics-panel.tsx`, `notifications-panel/notifications-panel.tsx` (with a menu-side twin at `game-ui/menu/components/notifications-panel/notifications-panel.tsx`), `city-info-panel/city-info-panel.tsx` and its `city-info-panel/city-info-policies/policies-page.tsx` and `city-info-panel/city-info-policies/city-policy.tsx`.
The progression, statistics, game-side notifications and city-info panels are `GamePanelType` entries and `gamePanelComponents` values, so replacing one is either an `extend` on its component or a key swap; the two policy modules render inside the city-info panel's policies tab and the menu-side twin inside the main menu, so those three are reached only by `extend` or `override` on their own module.

## The options screen

Under `game-ui/menu/components/options-screen/`: `options-screen.tsx`, `option-page/option-page.tsx`, `option-page/option-page-header.tsx`, `options-search.tsx`, `input-rebinding-dialog/input-rebinding-dialog.tsx` and `display-confirmation-dialog/display-confirmation-dialog.tsx`.

## The selected-info section map

`selectedInfoSectionComponents` in `game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx` is keyed on `Game.UI.InGame.*Section` strings, the enum beside it listing every vanilla one.
Adding a key is how a mod adds a section, and the key must equal the `__Type` its C# section emits.
