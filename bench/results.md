# Benchmark results

- Date: 2026-10-09
- Arms: opus at medium effort, 4 run(s) per arm per task over 30 task(s), judge: opus
- Skills: 5937dea, 77105d0

## Per arm

| arm | pass rate | scored runs | loaded a cs2 skill | contaminated | no code | cost mean usd | turns mean | duration mean ms | input tokens mean | cache creation tokens mean | cache read tokens mean | output tokens mean | thinking tokens mean | web searches mean | web fetches mean |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| control | 87% (438/502) | 120 | 0/120 | 0 | 0 | 1.2668 | 29.4 | 234313 | 52 | 61324 | 1421018 | 20422 | 9482 | 0.0 | 0.0 |
| treatment | 96% (481/500) | 120 | 120/120 | 0 | 0 | 1.6691 | 33.0 | 233192 | 54 | 86059 | 2315704 | 19684 | 8546 | 0.0 | 0.0 |

## Per task kind

| kind | control | treatment |
| --- | --- | --- |
| feature request | 85% (278/326) | 96% (310/324) |
| bug report | 91% (160/176) | 97% (171/176) |

## Per task

| task | kind | control | treatment |
| --- | --- | --- | --- |
| abandoned-auto-bulldoze | feature request | 65% (13/20) | 75% (15/20) |
| abandoned-buildings-tab | feature request | 68% (13/19) | 80% (16/20) |
| bankruptcy-watch | feature request | 100% (24/24) | 100% (24/24) |
| building-chronicle | bug report | 90% (18/20) | 100% (20/20) |
| building-notes-panel | feature request | 67% (8/12) | 100% (11/11) |
| building-rescue-tool | feature request | 81% (13/16) | 100% (16/16) |
| building-watchlist-panel | feature request | 100% (16/16) | 100% (16/16) |
| bulldoze-hidden-paths | feature request | 100% (11/11) | 100% (9/9) |
| cheap-low-density-homes | feature request | 81% (13/16) | 88% (14/16) |
| city-festival-policy | feature request | 95% (19/20) | 100% (20/20) |
| city-pulse-panel | feature request | 50% (8/16) | 100% (16/16) |
| city-stats-export | bug report | 81% (13/16) | 94% (15/16) |
| daily-city-snapshot | feature request | 100% (8/8) | 100% (8/8) |
| daily-scholarship | bug report | 100% (20/20) | 100% (20/20) |
| environment-report-log | feature request | 100% (16/16) | 100% (16/16) |
| instant-trees | feature request | 56% (9/16) | 81% (13/16) |
| large-school-variants | feature request | 88% (21/24) | 100% (24/24) |
| line-status-log | bug report | 100% (20/20) | 100% (20/20) |
| low-funds-alert | feature request | 100% (20/20) | 100% (20/20) |
| monthly-city-diary | bug report | 100% (16/16) | 100% (16/16) |
| new-building-log | bug report | 50% (10/20) | 80% (16/20) |
| police-gap-report | feature request | 100% (20/20) | 100% (20/20) |
| population-milestones | feature request | 88% (14/16) | 100% (16/16) |
| power-outage-panel | bug report | 100% (16/16) | 100% (16/16) |
| repeat-fire-ledger | bug report | 100% (16/16) | 100% (16/16) |
| small-street-stop-signs | feature request | 100% (12/12) | 100% (12/12) |
| tree-species-mix | feature request | 88% (14/16) | 100% (16/16) |
| unpowered-buildings-button | feature request | 75% (6/8) | 100% (8/8) |
| utility-inspector-tooltip | bug report | 100% (16/16) | 100% (16/16) |
| utility-trade-ledger | bug report | 94% (15/16) | 100% (16/16) |

## Per item

Pass/fail/not-applicable counts over the scored runs.

| task | item | control | treatment |
| --- | --- | --- | --- |
| abandoned-auto-bulldoze | The other mod is reached without a compile-time reference | 4/0/0 | 4/0/0 |
| abandoned-auto-bulldoze | Presence asked in `OnLoad` is asked of the mod manager | 4/0/0 | 4/0/0 |
| abandoned-auto-bulldoze | The system whose query names the foreign component is registered after every mod has loaded | 0/4/0 | 0/4/0 |
| abandoned-auto-bulldoze | The other mod's assembly name is matched whole | 4/0/0 | 4/0/0 |
| abandoned-auto-bulldoze | A cached detection answer is refreshed when the mod set changes | 1/3/0 | 3/1/0 |
| abandoned-buildings-tab | The graphed value is a watch on a system the world holds | 2/2/0 | 4/0/0 |
| abandoned-buildings-tab | The watch's sampling interval is a power of two | 3/0/1 | 4/0/0 |
| abandoned-buildings-tab | The counting system runs only while someone is watching | 4/0/0 | 4/0/0 |
| abandoned-buildings-tab | No widget getter runs a query | 0/4/0 | 0/4/0 |
| abandoned-buildings-tab | The text line is a widget kind the menu renders | 4/0/0 | 4/0/0 |
| bankruptcy-watch | Leaving is predicted from the bankruptcy condition, not read off the `MovingAway` tag | 4/0/0 | 4/0/0 |
| bankruptcy-watch | Extractor companies are left out | 4/0/0 | 4/0/0 |
| bankruptcy-watch | The bankruptcy threshold is read from the live `EconomyParameterData` singleton | 4/0/0 | 4/0/0 |
| bankruptcy-watch | The figure compared against the threshold is total worth, not cash | 4/0/0 | 4/0/0 |
| bankruptcy-watch | The daily loss comes from `CompanyStatisticData.m_Profit`, not from `Profitability` | 4/0/0 | 4/0/0 |
| bankruptcy-watch | The recipe is read from the company's prefab | 4/0/0 | 4/0/0 |
| building-chronicle | The component's layout is versioned by an int of the mod's own | 4/0/0 | 4/0/0 |
| building-chronicle | The component's `Deserialize` has no `try`/`catch` | 4/0/0 | 4/0/0 |
| building-chronicle | The system's save section is `IDefaultSerializable` | 4/0/0 | 4/0/0 |
| building-chronicle | `Deserialize` and `SetDefaults` only read the stream | 4/0/0 | 4/0/0 |
| building-chronicle | A new city is told from a save predating the mod by `context.purpose` | 2/2/0 | 4/0/0 |
| building-notes-panel | The export answer comes back on the call's own promise | 0/4/0 | 4/0/0 |
| building-notes-panel | The note of a building without one crosses through a nullable string writer | 4/0/0 | 3/0/1 |
| building-notes-panel | The export format enum crosses with a reader the code supplies | 4/0/0 | 4/0/0 |
| building-rescue-tool | The inherited apply and cancel actions are switched on by the tool | 4/0/0 | 4/0/0 |
| building-rescue-tool | The tool to return to is learned from the tool-changed event | 1/3/0 | 4/0/0 |
| building-rescue-tool | The overlay is written under the buffer's dependency | 4/0/0 | 4/0/0 |
| building-rescue-tool | The tooltip system runs in the tooltip phase | 4/0/0 | 4/0/0 |
| building-watchlist-panel | The entry payload crosses the wire through a writer it declares | 4/0/0 | 4/0/0 |
| building-watchlist-panel | A list mutated in place is pushed explicitly | 4/0/0 | 4/0/0 |
| building-watchlist-panel | The power status is re-read while the panel is open | 4/0/0 | 4/0/0 |
| building-watchlist-panel | The system is gated to the game mode its panel exists in | 4/0/0 | 4/0/0 |
| bulldoze-hidden-paths | The Harmony instance is created in `OnLoad` | 4/0/0 | 3/0/1 |
| bulldoze-hidden-paths | The widening adds to the frame's raycast flags rather than replacing them | 0/0/4 | 0/0/4 |
| bulldoze-hidden-paths | The hit filter acts only on hits the widening caused | 0/0/4 | 0/0/4 |
| bulldoze-hidden-paths | The hit-filter patch names the type that declares the signature it patches | 3/0/1 | 3/0/1 |
| bulldoze-hidden-paths | The `GetRaycastResult` overload is pinned by signature | 4/0/0 | 3/0/1 |
| cheap-low-density-homes | Low density is computed from the zone's data, not read off a name or a field | 4/0/0 | 4/0/0 |
| cheap-low-density-homes | The upkeep is changed on the building prefab's `ConsumptionData.m_Upkeep`, where the simulation reads it | 4/0/0 | 4/0/0 |
| cheap-low-density-homes | The upkeep change is re-applied on every load, after the save's game mode has run | 2/2/0 | 3/1/0 |
| cheap-low-density-homes | The rent cut is re-applied after every rent pass, not written once | 3/1/0 | 3/1/0 |
| city-festival-policy | The city entity is read off `CitySystem` where it is used | 4/0/0 | 4/0/0 |
| city-festival-policy | The toggle's modifier ranges carry the effect in `min` | 4/0/0 | 4/0/0 |
| city-festival-policy | The city modifier is read through a length-guarded accessor | 4/0/0 | 4/0/0 |
| city-festival-policy | The policy is made unlockable before it is registered | 3/1/0 | 4/0/0 |
| city-festival-policy | The policy's lock state is read as an enabled bit | 4/0/0 | 4/0/0 |
| city-pulse-panel | The registrar guards its own body | 0/4/0 | 4/0/0 |
| city-pulse-panel | The game's icons are addressed on a host that serves them | 4/0/0 | 4/0/0 |
| city-pulse-panel | Sizes are written in rem | 4/0/0 | 4/0/0 |
| city-pulse-panel | The headline's font size follows the text-scale setting | 0/4/0 | 4/0/0 |
| city-stats-export | The mod logs through a logger of its own | 4/0/0 | 4/0/0 |
| city-stats-export | A failed write does not stop the player's game | 4/0/0 | 4/0/0 |
| city-stats-export | Literal notification text is passed as a value, not as a key | 1/3/0 | 3/1/0 |
| city-stats-export | The per-city part of the notification is not its title | 4/0/0 | 4/0/0 |
| daily-city-snapshot | The game version is read from `Game.Version.current` | 4/0/0 | 4/0/0 |
| daily-city-snapshot | The order after the statistics refresh is a registration call, not an attribute | 4/0/0 | 4/0/0 |
| daily-city-snapshot | The student count queries the citizen-side `Student` | 0/0/4 | 0/0/4 |
| daily-scholarship | Age is read from `Citizen.GetAge()`, not from the `Child`/`Teen`/`Adult`/`Elderly` tag components | 4/0/0 | 4/0/0 |
| daily-scholarship | Education is read through `GetEducationLevel()`, not decoded from the state bits | 4/0/0 | 4/0/0 |
| daily-scholarship | `Citizen.m_PseudoRandom` is never written | 4/0/0 | 4/0/0 |
| daily-scholarship | Tourists and commuters are excluded | 4/0/0 | 4/0/0 |
| daily-scholarship | The prize is added to the household's money, not to `Household.m_Resources` | 4/0/0 | 4/0/0 |
| environment-report-log | The map handle is completed before the array is read | 4/0/0 | 4/0/0 |
| environment-report-log | The groundwater pollution share is taken against the water present | 4/0/0 | 4/0/0 |
| environment-report-log | The wind is read from the wind simulation, not from `ClimateSystem` | 4/0/0 | 4/0/0 |
| environment-report-log | The used fish amount is read from the cell's field, not from `GetUsedResources` | 4/0/0 | 4/0/0 |
| instant-trees | The tree query excludes the tool preview | 1/3/0 | 2/2/0 |
| instant-trees | A grown tree is tagged for the renderer | 0/4/0 | 3/1/0 |
| instant-trees | A dead tree is removed by tag | 4/0/0 | 4/0/0 |
| instant-trees | Structural changes go through a barrier the game runs | 4/0/0 | 4/0/0 |
| large-school-variants | The vanilla schools are looked up after mod loading has finished | 4/0/0 | 4/0/0 |
| large-school-variants | A typed prefab lookup is null-tested rather than trusted on its return value | 4/0/0 | 4/0/0 |
| large-school-variants | The clone sheds the original's obsolete identifiers before registration | 4/0/0 | 4/0/0 |
| large-school-variants | The result of registering the clone is checked | 4/0/0 | 4/0/0 |
| large-school-variants | The routine skips a variant it already registered | 4/0/0 | 4/0/0 |
| large-school-variants | The variant's name carries a token of the mod's own | 1/3/0 | 4/0/0 |
| line-status-log | A full vehicle is one with no free space, not one flagged `Full` | 4/0/0 | 4/0/0 |
| line-status-log | Waiting passengers are read off the line's waypoint, not the stop | 4/0/0 | 4/0/0 |
| line-status-log | Load and capacity are summed over the consist | 4/0/0 | 4/0/0 |
| line-status-log | Each payload component is tested with its own flags enum | 4/0/0 | 4/0/0 |
| line-status-log | A route option is a shifted ordinal, not a bit value | 4/0/0 | 4/0/0 |
| low-funds-alert | The defaults are seeded by the settings class itself | 4/0/0 | 4/0/0 |
| low-funds-alert | The toggle action is switched on before it is read | 4/0/0 | 4/0/0 |
| low-funds-alert | The threshold carries a slider or dropdown attribute | 4/0/0 | 4/0/0 |
| low-funds-alert | The reset button is a write-only property | 4/0/0 | 4/0/0 |
| low-funds-alert | The hotkeys tab is named through a section overload that takes a tab | 4/0/0 | 4/0/0 |
| monthly-city-diary | The month is read from the DateTime's day slot | 4/0/0 | 4/0/0 |
| monthly-city-diary | The city's age in days is counted from the founding frame | 4/0/0 | 4/0/0 |
| monthly-city-diary | The founding year is the TimeData singleton's | 4/0/0 | 4/0/0 |
| monthly-city-diary | The season comes from the climate prefab | 4/0/0 | 4/0/0 |
| new-building-log | Mod-level state is static or assigned inside `OnLoad`, never left to a field initialiser or constructor | 0/4/0 | 4/0/0 |
| new-building-log | Ordering is requested through the update system, not through ECS ordering attributes | 4/0/0 | 4/0/0 |
| new-building-log | The system is registered into `Modification2`, the phase the building initialiser runs in | 0/4/0 | 0/4/0 |
| new-building-log | The per-city count is reset on a per-load hook | 4/0/0 | 4/0/0 |
| new-building-log | The system's `OnCreate` calls `base.OnCreate()` | 2/2/0 | 4/0/0 |
| police-gap-report | Police coverage is read off the home's road edge | 4/0/0 | 4/0/0 |
| police-gap-report | A station with no district assigned serves everywhere | 4/0/0 | 4/0/0 |
| police-gap-report | An efficiency factor missing from the buffer is 1 | 4/0/0 | 4/0/0 |
| police-gap-report | A working station fields at least one patrol car | 4/0/0 | 4/0/0 |
| police-gap-report | A budget slider nobody has moved reads as 100 | 4/0/0 | 4/0/0 |
| population-milestones | A translation file for a language the game does not ship is not registered blind | 4/0/0 | 4/0/0 |
| population-milestones | The notification's text reaches the UI as a key with substitutions, not as a C# string | 4/0/0 | 4/0/0 |
| population-milestones | A reload adds a new source instance rather than re-adding the one it removed | 4/0/0 | 4/0/0 |
| population-milestones | The source names the mod in `ToString()` | 2/2/0 | 4/0/0 |
| power-outage-panel | The banner crosses as an event, not as a value | 4/0/0 | 4/0/0 |
| power-outage-panel | The throttle is not a `GetUpdateInterval` override | 4/0/0 | 4/0/0 |
| power-outage-panel | The scan runs only while the panel listens | 4/0/0 | 4/0/0 |
| power-outage-panel | The system's `OnUpdate` keeps the binding pump alive | 4/0/0 | 4/0/0 |
| repeat-fire-ledger | The ledger is allocated once and emptied at each city load | 4/0/0 | 4/0/0 |
| repeat-fire-ledger | The burning buildings are gathered asynchronously | 4/0/0 | 4/0/0 |
| repeat-fire-ledger | The job's handle is published rather than completed every update | 4/0/0 | 4/0/0 |
| repeat-fire-ledger | The log line is written when the ledger changes, not on every update | 4/0/0 | 4/0/0 |
| small-street-stop-signs | Stop signs enter through the junction's composition flags, not its lanes | 4/0/0 | 4/0/0 |
| small-street-stop-signs | The junction's edges carry the update marker | 4/0/0 | 4/0/0 |
| small-street-stop-signs | The speed limit is compared in metres per second | 4/0/0 | 4/0/0 |
| tree-species-mix | The rewriter is registered ahead of the consumer rather than with `UpdateAt` | 4/0/0 | 4/0/0 |
| tree-species-mix | The rewritten definition is written synchronously | 4/0/0 | 4/0/0 |
| tree-species-mix | The definition query excludes `Deleted` | 2/2/0 | 4/0/0 |
| tree-species-mix | The species is drawn from the definition's own seed, forced non-zero | 4/0/0 | 4/0/0 |
| unpowered-buildings-button | The button reaches the right menu through a route that renders it | 4/0/0 | 4/0/0 |
| unpowered-buildings-button | The registrar guards its own body | 2/2/0 | 4/0/0 |
| unpowered-buildings-button | Sizes are written in rem | 0/0/4 | 0/0/4 |
| unpowered-buildings-button | The badge's font size follows the text-scale setting | 0/0/4 | 0/0/4 |
| utility-inspector-tooltip | The electricity figure is handed raw under the power unit | 4/0/0 | 4/0/0 |
| utility-inspector-tooltip | The temperature is handed in Celsius under the temperature unit | 4/0/0 | 4/0/0 |
| utility-inspector-tooltip | The water figure is handed raw under the volume unit | 4/0/0 | 4/0/0 |
| utility-inspector-tooltip | The percentage value is in percent points | 4/0/0 | 4/0/0 |
| utility-trade-ledger | Generated electricity is summed from last production, not capacity | 4/0/0 | 4/0/0 |
| utility-trade-ledger | Sewage export is read off the source-side trade edge | 4/0/0 | 4/0/0 |
| utility-trade-ledger | Polluted export water earns nothing | 4/0/0 | 4/0/0 |
| utility-trade-ledger | Sewage export is billed as a cost | 3/1/0 | 4/0/0 |

## References read by treatment runs

| reference | runs |
| --- | --- |
| cs2-mod-project/references/linux-toolchain.md | 75 |
| cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md | 56 |
| cs2-mod-project/references/build-pipeline.md | 45 |
| cs2-mod-project/SKILL.md | 42 |
| cs2-modding-ui/references/binding-layer/binding-layer.md | 20 |
| cs2-modding/references/mechanics/utilities-and-flow-networks/consumption-and-dispatch.md | 20 |
| cs2-modding/references/technique/localization/localization.md | 18 |
| cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md | 16 |
| cs2-modding-ui/references/ui-build-and-devloop/ui-build-and-devloop.md | 16 |
| cs2-modding/references/technique/ecs-in-this-game/ecs-in-this-game.md | 13 |
| cs2-modding-setup/SKILL.md | 12 |
| cs2-modding/references/technique/save-serialization/save-serialization.md | 12 |
| cs2-modding/references/mechanics/simulation-time-and-units/cadence.md | 11 |
| cs2-modding/references/mechanics/zoning-buildings-and-land-value/level-up-loop.md | 11 |
| cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md | 11 |
| cs2-modding/references/mechanics/city-state-and-progression/city-state-and-progression.md | 10 |
| cs2-modding/references/technique/diagnostics/diagnostics.md | 10 |
| cs2-modding/references/technique/performance-and-memory/performance-and-memory.md | 10 |
| cs2-modding/references/mechanics/simulation-time-and-units/simulation-time-and-units.md | 9 |
| cs2-modding/references/technique/settings-and-input/settings-and-input.md | 9 |
| cs2-modding-ui/references/frontend-and-injection/promised-registry-paths.md | 8 |
| cs2-modding/references/mechanics/zoning-buildings-and-land-value/zoning-buildings-and-land-value.md | 8 |
| cs2-modding/references/technique/mod-lifecycle-and-ordering/phase-catalogue.md | 8 |
| cs2-modding/references/mechanics/simulation-time-and-units/calendar.md | 7 |
| cs2-modding/references/mechanics/utilities-and-flow-networks/utilities-and-flow-networks.md | 7 |
| cs2-modding/references/technique/custom-tools/custom-tools.md | 7 |
| cs2-modding/references/technique/prefabs-and-assets/prefab-data-initialisation.md | 7 |
| cs2-modding/references/technique/diagnostics/reporting-to-the-player.md | 6 |
| cs2-modding/references/technique/placement-definitions/placement-definitions.md | 6 |
| cs2-modding/references/mechanics/economy-and-companies/economy-and-companies.md | 5 |
| cs2-modding/references/technique/ecs-in-this-game/universal-tags.md | 5 |
| cs2-modding/references/mechanics/citizens-and-households/citizens-and-households.md | 4 |
| cs2-modding/references/mechanics/city-services-and-coverage/budget-workforce-and-upkeep.md | 4 |
| cs2-modding/references/mechanics/city-services-and-coverage/city-services-and-coverage.md | 4 |
| cs2-modding/references/mechanics/city-services-and-coverage/coverage.md | 4 |
| cs2-modding/references/mechanics/city-services-and-coverage/dispatch.md | 4 |
| cs2-modding/references/mechanics/city-state-and-progression/policies.md | 4 |
| cs2-modding/references/mechanics/city-state-and-progression/statistics.md | 4 |
| cs2-modding/references/mechanics/city-state-and-progression/unlocking.md | 4 |
| cs2-modding/references/mechanics/economy-and-companies/production-and-profit.md | 4 |
| cs2-modding/references/mechanics/environment-and-pollution/cell-maps.md | 4 |
| cs2-modding/references/mechanics/environment-and-pollution/climate-and-weather.md | 4 |
| cs2-modding/references/mechanics/environment-and-pollution/environment-and-pollution.md | 4 |
| cs2-modding/references/mechanics/environment-and-pollution/natural-resources.md | 4 |
| cs2-modding/references/mechanics/environment-and-pollution/water-and-groundwater.md | 4 |
| cs2-modding/references/mechanics/roads-and-traffic/junctions.md | 4 |
| cs2-modding/references/mechanics/roads-and-traffic/network-rebuild.md | 4 |
| cs2-modding/references/mechanics/utilities-and-flow-networks/solve-cycle.md | 4 |
| cs2-modding/references/technique/debug-menu/debug-menu.md | 4 |
| cs2-modding/references/technique/ecs-in-this-game/update-frame-buckets.md | 4 |
| cs2-modding/references/technique/mod-compatibility/mod-compatibility.md | 4 |
| cs2-modding/references/technique/units-and-formatting/units-and-formatting.md | 4 |
| cs2-mod-project/references/publishing.md | 3 |
| cs2-modding/references/mechanics/roads-and-traffic/roads-and-traffic.md | 3 |
| cs2-modding/references/mechanics/simulation-time-and-units/day-night-and-seasons.md | 3 |
| cs2-modding/references/mechanics/transportation-and-vehicles/lines-and-fleet.md | 3 |
| cs2-modding/references/mechanics/utilities-and-flow-networks/flow-graph.md | 3 |
| cs2-modding/references/mechanics/utilities-and-flow-networks/production-and-sources.md | 3 |
| cs2-modding/references/technique/patching/patching.md | 3 |
| cs2-modding/references/mechanics/city-state-and-progression/notifications.md | 2 |
| cs2-modding/references/mechanics/city-state-and-progression/progression.md | 2 |
| cs2-modding/references/mechanics/environment-and-pollution/map-dynamics.md | 2 |
| cs2-modding/references/mechanics/simulation-time-and-units/units.md | 2 |
| cs2-modding/references/technique/localization/vanilla-namespaces.md | 2 |
| cs2-modding/references/technique/prefabs-and-assets/regenerating-a-prefab.md | 2 |
| cs2-modding/references/technique/settings-and-input/building-the-options-page.md | 2 |
| cs2-modding/references/mechanics/citizens-and-households/education-pipeline.md | 1 |
| cs2-modding/references/mechanics/citizens-and-households/lifecycle.md | 1 |
| cs2-modding/references/technique/performance-and-memory/cleanup-components.md | 1 |
| cs2-modding/references/technique/performance-and-memory/colossal-collections.md | 1 |
| cs2-modding/references/technique/save-serialization/deserialize-phase-census.md | 1 |
| cs2-modding/references/technique/settings-and-input/attribute-catalog.md | 1 |
