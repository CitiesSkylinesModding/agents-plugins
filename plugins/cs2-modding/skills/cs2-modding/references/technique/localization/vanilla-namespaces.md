# The vanilla key namespaces

Verified against game version 1.6.0f1.

**Read this with the game's string tables open.**
The counts below are taken from those tables and the decompile carries none of them; `localization` owns the C# behind a key lookup.
They ship inside the install, which the toolchain's environment variables locate.

The lookup behind `localization`'s reuse section: every group the game's own shipped strings occupy, and how many keys each one holds.

Each row is a **group**: the segment before the first dot.
**Ids** counts the distinct `Group.ID` pairs in the group, ignoring hash and index.
**Entries** counts the actual rows, so a group whose ids are mostly hashed or indexed carries far more entries than ids — and the gap between the two columns is what tells you whether to look a key up or construct one.

**75 groups, 2,153 ids, 22,120 entries**, counted in English, which is the fallback locale and therefore the set that defines what exists.

| Group                         | Ids | Entries | Covers                                                                                                                                           |
| ----------------------------- | --: | ------: | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Achievements`                |   2 |      82 | achievement `TITLE`/`DESCRIPTION`, both hashed by achievement id                                                                                 |
| `AirPollutionInfoPanel`       |   1 |       1 | the air-pollution info view's average readout                                                                                                    |
| `AnimationCurve`              |   2 |       2 | axis labels on a curve editor                                                                                                                    |
| `Assets`                      |  30 |   12028 | prefab display names and descriptions, citizen and vehicle name formats, address formats, themes, upgrades, and the indexed generated-name pools |
| `BikesInfoPanel`              |   2 |       2 | parked bikes and bike-parking availability                                                                                                       |
| `Budget`                      |   7 |      35 | budget-panel tooltips, including the tax breakdowns                                                                                              |
| `Chirper`                     | 116 |     341 | every Chirper message, most of them indexed variants                                                                                             |
| `CinematicCamera`             |  24 |      24 | the cinematic camera editor                                                                                                                      |
| `CityInfoPanel`               |  14 |      70 | demand factors and their descriptions                                                                                                            |
| `Climate`                     |   1 |       4 | `SEASON`, hashed by season                                                                                                                       |
| `Common`                      | 153 |     438 | shared actions, dialog scaffolding, separators, month names, and **every `VALUE_*`, `FRACTION_*` and `BOUNDS_*` unit string**                    |
| `CompanyInfoPanel`            |   3 |       3 | commercial, industrial and office profitability                                                                                                  |
| `Content`                     |   2 |      15 | content-pack name and prerequisite                                                                                                               |
| `DefaultTool`                 |   1 |      15 | `INFOMODE_TOOLTIP`                                                                                                                               |
| `DisasterInfoPanel`           |   3 |       3 | shelter capacity and evacuation                                                                                                                  |
| `EconomyPanel`                | 114 |     373 | the whole economy panel: budget lines, taxation, loans, production                                                                               |
| `Editor`                      | 263 |     678 | the map and asset editors, end to end                                                                                                            |
| `EditorTutorials`             |   3 |     143 | editor tutorial scaffolding                                                                                                                      |
| `EducationInfoPanel`          |   7 |      20 | education availability and distribution                                                                                                          |
| `ElectricityInfoPanel`        |   8 |       8 | electricity availability, trade, battery charge                                                                                                  |
| `EventJournal`                |   4 |      20 | event journal entries and effects                                                                                                                |
| `FireAndRescueInfoPanel`      |   1 |       1 | average fire hazard                                                                                                                              |
| `GameListScreen`              |  39 |      70 | the save-game list and its city summary fields                                                                                                   |
| `GarbageInfoPanel`            |   4 |       4 | garbage rate, landfill availability, processing                                                                                                  |
| `Glossary`                    |   8 |     521 | the in-game glossary: 48 categories, 229 sections with a title and a body each, 11 tabs                                                          |
| `GroundPollutionInfoPanel`    |   1 |       1 | average ground pollution                                                                                                                         |
| `HealthcareInfoPanel`         |  10 |      10 | health, cemetery and crematorium availability                                                                                                    |
| `InfoPanels`                  |   6 |       6 | labels shared across info panels — capacity, consumption, output, processing, production, stored                                                 |
| `Infoviews`                   |  67 |     430 | info view names and their legend tooltips                                                                                                        |
| `ISO`                         |   1 |     249 | `COUNTRY`, hashed by ISO code                                                                                                                    |
| `LandValueInfoPanel`          |   1 |       1 | average land value                                                                                                                               |
| `LevelInfoPanel`              |   9 |       9 | building level names per zone kind                                                                                                               |
| `LifePath`                    |  33 |     130 | the citizen life-path panel's event descriptions                                                                                                 |
| `Loading`                     |   2 |      39 | loading screen title and hint messages                                                                                                           |
| `Main`                        |  69 |      69 | the main toolbar and its per-button tooltips                                                                                                     |
| `Maps`                        |   4 |      49 | map titles, descriptions, outside connections                                                                                                    |
| `MapTilePurchase`             |  17 |      44 | the tile-purchase panel's resource summary                                                                                                       |
| `Menu`                        |  98 |     185 | main menu, achievements warnings, asset upload, notifications                                                                                    |
| `NaturalResourcesInfoPanel`   |   8 |       8 | fertility, ore, oil, fish availability                                                                                                           |
| `NoisePollutionInfoPanel`     |   1 |       1 | average noise pollution                                                                                                                          |
| `Notifications`               |   2 |     142 | `TITLE`/`DESCRIPTION` for a pushed notification, hashed by its key                                                                               |
| `Options`                     | 149 |    1588 | the whole options screen, including the mod-page keys the settings helpers generate                                                              |
| `OutsideConnectionsInfoPanel` |   2 |       2 | top imports and exports                                                                                                                          |
| `Overlay`                     |  19 |      19 | platform overlay actions and controller-disconnect prompts                                                                                       |
| `Paradox`                     |  82 |     168 | account linking, the mods UI, playsets                                                                                                           |
| `PhotoMode`                   |  19 |     321 | photo-mode property titles and tooltips                                                                                                          |
| `PoliceInfoPanel`             |  10 |      10 | crime probability, arrests, success rate                                                                                                         |
| `Policy`                      |   2 |      44 | `TITLE`/`DESCRIPTION`, hashed by policy prefab name                                                                                              |
| `PopulationInfoPanel`         |  15 |      15 | age distribution, birth and death rates                                                                                                          |
| `PostInfoPanel`               |   4 |       4 | mail collected, delivered, rate                                                                                                                  |
| `Progression`                 |  74 |     320 | milestones, development trees, unlock panels                                                                                                     |
| `Properties`                  |  68 |     138 | the per-prefab stat rows in the selected-info and tooltip panels                                                                                 |
| `Radio`                       |  14 |      19 | radio station UI and emergency messages                                                                                                          |
| `Resources`                   |   1 |      41 | `TITLE`, hashed by resource name                                                                                                                 |
| `RoadsInfoPanel`              |   4 |       4 | parking availability and income                                                                                                                  |
| `SelectedInfoPanel`           | 323 |     926 | the largest group by ids: every row of the selected-info panel                                                                                   |
| `Services`                    |   2 |      32 | `NAME`/`DESCRIPTION` for a service or asset-menu prefab                                                                                          |
| `Statistics`                  |   2 |     214 | statistics panel title and per-statistic label                                                                                                   |
| `StatisticsPanel`             |   4 |     441 | statistic titles and time-scale labels                                                                                                           |
| `SubServices`                 |   1 |      64 | `NAME` for an asset-category prefab                                                                                                              |
| `Toolbar`                     |  44 |      44 | the asset menu, theme and asset-pack panels, brush controls                                                                                      |
| `ToolOptions`                 |  22 |      96 | the tool-options panel's titles and tooltips                                                                                                     |
| `Tools`                       |  33 |      70 | tool tooltips — area size, resource yields, flow and consumption labels                                                                          |
| `TourismInfoPanel`            |   4 |       4 | attractiveness, hotel price, tourism rate                                                                                                        |
| `Transport`                   |  27 |      69 | transport overlay legends and line UI                                                                                                            |
| `TransportInfoPanel`          |  10 |      22 | passengers, cargo, line counts                                                                                                                   |
| `Tutorials`                   |  36 |    1096 | tutorial and advisor scaffolding                                                                                                                 |
| `UpgradesMenu`                |   1 |       1 | `TITLE`                                                                                                                                          |
| `VirtualKeyboard`             |   1 |      15 | `TITLE`, hashed by what is being named                                                                                                           |
| `WaterInfoPanel`              |   8 |       8 | water and sewage treatment and trade                                                                                                             |
| `WaterPollutionInfoPanel`     |   1 |       1 | average water pollution                                                                                                                          |
| `WealthInfoPanel`             |  13 |      17 | average wealth, income, rent, fees, upkeep and resource cost, with a wealth-tier key                                                             |
| `WhatsNew`                    |  10 |      10 | the what's-new panel's per-release copy                                                                                                          |
| `WorkplacesInfoPanel`         |   4 |       4 | workplaces, workers, availability                                                                                                                |
| `ZoningFactors`               |   3 |      19 | zoning factor panel title and positive/negative labels                                                                                           |

(VOLATILE: the whole table — its group set, both count columns and the coverage of each group all move with the game's shipped locale data.)

The 21 groups `localization` names as the C#-visible ones are `Editor`, `Common`, `Options`, `Properties`, `Paradox`, `Assets`, `Tools`, `Menu`, `PhotoMode`, `DefaultTool`, `SelectedInfoPanel`, `Services`, `Maps`, `Infoviews`, `Policy`, `GameListScreen`, `SubServices`, `StatisticsPanel`, `Radio`, `Notifications` and `Loading`.
Every other row above is built entirely in the frontend.
