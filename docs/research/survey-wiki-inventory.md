# Cities: Skylines II Wiki — Complete Page Inventory

> **Seed survey.** Produced 2026-07-31 during the interview that became the `cs2-modding` spec, before the discovery pipeline existed.
> Read the wiki only, fetched 2026-07-31, when the game was at 1.6.0f1.
> Kept as it was written, citations intact; its recommendations are that pass's opinion, not decisions.

## 0. What worked (step 1)

| Attempt                                                                              | Result                                                                                                      |
| ------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| `https://cs2.paradoxwikis.com/Special:AllPages`                                      | **Worked.** Returned the full namespace-0 listing, `1.2` → `Trees`, plus a continuation link.               |
| `https://cs2.paradoxwikis.com/index.php?title=Special:AllPages&from=Tutorial+videos` | **Worked.** Returned `Tutorial videos` → end, including 14 Chinese-language pages.                          |
| `https://cs2.paradoxwikis.com/Special:Categories`                                    | **Worked** (paginated; second call with `&offset=DLC+icons&limit=500` completed it). ~156 categories total. |
| Main-page navbox (`Cities_Skylines_II_Wiki`)                                         | **Worked.** Gave the community's own top-level grouping — see §7.                                           |

`WebFetch` rendered past the Fastly challenge on every single call. Nothing was unreachable. Total page count in namespace 0: **~420 pages**, of which roughly 90 are DLC signature-building/radio-station entries and ~14 are Chinese translations.

A structural caveat that shapes everything below: **this wiki redirects aggressively into a small number of hub pages.** Dozens of titles that look like independent topics (`Electricity`, `Healthcare`, `Crime`, `Efficiency`, `Milestones`, `Speed limits`, `Pathfinding`, `Happiness`, `Building level`) are redirects. The real gameplay knowledge lives in about **14 long hub articles**, not in 150 small ones. I verified 22 redirects by fetching them; the rest are marked "inferred."

---

## 1. Modding — process / toolchain

### Official (Paradox-verified)

**`Modding`** — https://cs2.paradoxwikis.com/Modding
The landing hub. Splits into _Official Resources_ (Code Mods / Editor / Maps / Assets) and _Community-Made Resources_ (General Guides / Asset Guides / Developing Code Mods). Headings: `Create what you imagine` · `Official Resources` (Code Mods, Editor, Maps, Assets) · `Community-Made Resources` (General Guides; Asset Guides → 3D modelling, Asset walkthroughs, Emissive guides; Developing Code Mods → Setting Up, Guides, Knowledgebase, Tips and Tricks). Substantial as a router; carries a beta-documentation warning. Last edited June 2026 — the most actively maintained hub. `Modding/Lang` is the translation bar subpage.

**`Modding Toolchain`** — https://cs2.paradoxwikis.com/Modding_Toolchain
The single most load-bearing page for this skill. Headings: `The Basics` (Modding Launch Parameters) · `Modding Options` · `Requirements` (IDE, Unity Editor & License, Unity Mod Project, .Net SDK, Node.js) · `C# Mod Project Template` (Understanding the Template, Project Variables, Important to note, Building the Mod) · `How to publish a Code Mod` · `UI Mod Project Template` · `Local Mods Location` · `External Documentation`.
**Hard numbers a mod author needs:** VS 2022 17.8+, Rider 2021.3.3+, Unity Editor 2022.3.7f1, .NET SDK 6+ (8 recommended), Node.js 18+ (20.11 recommended); local mods path `AppData\LocalLow\Colossal Order\Cities Skylines II\Mods`. **Stale risk:** verified for **1.1.12f1** while the game is at **1.6.0f1** — a five-version gap on the most critical page. `Modding Toolchain/Lang` is the translation bar.

**`UI Modding`** — https://cs2.paradoxwikis.com/UI_Modding
Headings: `CSS, SCSS and CSSmodules` · `Using image assets` · `Ensuring the UI is scalable for all resolutions.` · `cs2/* packages` (`cs2/api`, `cs2/bindings`, `cs2/l10n`, `cs2/modding`, `cs2/ui`, `cs2/utils`, `cs2/input`) · `Mounting the UI mods and the Module Registry` (Find, Override, Extend, Append, Append at Hook) · `Universal Mod Menu`. Substantial. React/TypeScript + SCSS on a cohtml host; the Module Registry Find/Override/Extend/Append vocabulary is the key mental model. **This is a unit-definition page** for the UI side — the seven `cs2/*` packages are the entire public UI API surface.

**`Options UI`** — https://cs2.paradoxwikis.com/Options_UI
Headings: `Introduction` · `Option types` · `Attributes` · `Option Type Attributes` · `Option Modification Attributes` · `Sorting and grouping attributes` · `Keybinding Attributes`. Substantial throughout. Effectively an **attribute catalog**: 9 widget types, 6 type attributes, 13 modification attributes, 7 sorting/grouping attributes, 7 keybinding attributes. High-value reference material — dense, tabular, mechanical.

**`Mod Key Binding`** — https://cs2.paradoxwikis.com/Mod_Key_Binding
Headings: `Add a key binding` · `Key binding settings` · `Input action` · `Advanced Input Action Settings` · `Usages and conflict resolving` · `Input action registration and using` · `Localization` · `Mimic Keybinding` · `Example`. Substantial. Covers `ProxyBinding`, `SettingsUIKeyboardBindingAttribute` / `SettingsUIMouseBinding` / `SettingsUIGamepadBindingAttribute`, action types (Button/Axis/Vector2), `RegisterKeyBindings()`, `GetAction()`, `IsPressed`/`WasPressedThisFrame`/`ReadValue`, and conflict priority between vanilla and mod bindings. Overlaps deliberately with the Options UI keybinding section.

**`Launch Parameters`** — https://cs2.paradoxwikis.com/Launch_Parameters
Headings: `Launch Parameters` · `Available Parameters` · `How to Use Launch Parameters` · `Via Steam` · `Via Microsoft Store / Xbox App` · `Notes`. Substantial, tabular. Parameters: `--disableModding`, `--disableCodeModding`, `--developerMode`, `--uiDeveloperMode` (UI inspector at `localhost:9444`), `--burst-disable-compilation`. Note `--logsEffectiveness=DEBUG` appears on the Logging page but not here — a small inconsistency.

### Community-made

**`Creating UI And Code Mods`** — https://cs2.paradoxwikis.com/Creating_UI_And_Code_Mods
Headings: `Creating The Projects` · `Configuring Your Mods` · `Results`. Substantial (Results is a stub). The load-bearing detail: the UI mod's ID in `mod.json` must match the code mod's target name, plus `.csproj` XML to build both in one step.

**`Naming Folder And Files`** — https://cs2.paradoxwikis.com/Naming_Folder_And_Files
Headings: `Logs (Logs/YourMod.log)` · `Settings (ModsSettings/YourMod/YourMod.coc)` · `Migrating from YourMod.coc to ModsSettings/YourMod/YourMod.coc` · `Volatile Data and User custom data (ModsData/YourMod/*)` · `Temporary and Cache Data` · `Code Example`. Substantial. **This is the on-disk layout convention** — the headings themselves are the specification.

**`Creating a Settings File`** — https://cs2.paradoxwikis.com/Creating_a_Settings_File
Headings: `Definition` · `Creating & loading a Settings file` · `Saving a Settings file`. Substantial. `.coc` format (JSON + group/section names), `FileLocation` attribute, `AssetDatabase.global.LoadSettings()`, auto-save of non-default values only.

**`Logging`** — https://cs2.paradoxwikis.com/Logging
Headings: `Creating a Log File` · `Changing the Log's Effectiveness` (Available Log Levels) · `Guidelines & Tips`. Substantial. `LogManager.GetLogger(fileName)` from `Colossal.Logging`; logs at `%AppData%\..\LocalLow\Colossal Order\Cities Skylines II\Logs`; 11 severity levels DISABLED→VERBOSE→ALL; global override via `--logsEffectiveness=DEBUG`.

**`Debugging`** — https://cs2.paradoxwikis.com/Debugging
Headings: `Enable Unity Support in IDE` · `Automated debugging` · `Modifying the game files to support a debugger` · `Debugging` (Visual Studio, Rider). Substantial. Five-step manual path: find Unity version → find editor install → copy `UnityPlayer.dll` → edit `boot.config` → add `player-connection-debug=1`. **Contains an explicit version conflict:** the CS2-ModdingTools NuGet automated path is flagged broken on patch **v1.5.7f1** with package 1.0.5, with manual setup recommended instead.

**`Localize your mod`** — https://cs2.paradoxwikis.com/Localize_your_mod
21 headings; the deepest community page on the wiki. `Setup translations files` · `Standalone approach` · `Using I18n EveryWhere dependency` · `Translating strings, localizing dates and numbers` · `In UI code (preferred)` · `Translating strings` · `Format single numbers (with or without units)` · `Format fractions` · `Format bounds` · `Format percentage` · `Format date` · `Format time` · `Format duration` · `Time format, temperature and length unit preferences` · `In C# code (when you don't have a choice)` · `Translation keys namespacing` · `How to name your keys` · `Vanilla translation keys & namespaces` · `Bonus` · `Finding translators` · `Dump all keys and values from the locale dictionary`. Substantial. **Carries a reference table of vanilla translation keys and namespaces** (Assets, Budget, Common, Options…) — directly reusable data.

**`Modding Toolchain on Linux`** — https://cs2.paradoxwikis.com/Modding_Toolchain_on_Linux
Headings: `Requirements` · `The process` · `IDE integration` (Mod template, Intellisense & autocompletion, Building & publishing) · `References` (empty). Substantial. protontricks + dotnet48/dotnet6, Unity Hub 3.7.0 under Proton not Wine, protontricks-wrapped `dotnet` for build/publish.

**`Developer mode`** — https://cs2.paradoxwikis.com/Developer_mode
Headings: `Enabling developer mode` · `Using developer mode` · `Known Issues`. Substantial. `-developerMode`, Tab for dev UI, Home for object menu, "bypass validation results", plus three known bugs and workarounds. **Note the flag spelling differs from Launch Parameters** (`-developerMode` here vs `--developerMode` there) — a genuine contradiction to resolve.

**`Mod security`** — https://cs2.paradoxwikis.com/Mod_security
Headings: `Trusted Sources` · `Avoid Out-of-Date Mods` · `Skyve – Recommended Tool` · `References`. Substantial but **player-facing, not developer-facing** — it is about _consuming_ mods safely. Notable hard rule: "BepInEx & BepInEx mods – do not use."

### Player-side modding / troubleshooting (thin)

- **`Mods`** — https://cs2.paradoxwikis.com/Mods — `Installation` · `Mods with wiki pages` · `See also`. **Stub**; installation section explicitly requests expansion. Only one mod has a wiki page (Extra assets importer).
- **`Paradox Mods`** — https://cs2.paradoxwikis.com/Paradox_Mods — the distribution platform. Not fetched.
- **`Skyve`** — https://cs2.paradoxwikis.com/Skyve — third-party mod manager / playset tool. Not fetched.
- **`Basic troubleshooting`** — https://cs2.paradoxwikis.com/Basic_troubleshooting
- **`Verifying game files`** — https://cs2.paradoxwikis.com/Verifying_game_files — and **`Veryifying game files`** (https://cs2.paradoxwikis.com/Veryifying_game_files), a **typo duplicate**.
- **`Versioning`** — https://cs2.paradoxwikis.com/Versioning — how the wiki tracks game versions.
- **`Options menu`** / **`Options menu/Graphics`** — in-game settings, player-facing.
- **`Community-Made Guides`** — https://cs2.paradoxwikis.com/Community-Made_Guides — mirrors the community half of `Modding` exactly (same section tree, same 21 links). **`Community-made guides`** is the lowercase duplicate/redirect.

---

## 2. Modding — game internals (the highest-value cluster for this skill)

**`ECS - Entity Component System`** — https://cs2.paradoxwikis.com/ECS_-_Entity_Component_System
Headings: `Definitions` (Entities; Components → Standard Components, Shared Components, Buffer Components; Systems) · `Coming from CS1 to ECS` · `Archetypes` · `Queries` · `Usage Example`. Substantial with diagrams. Types: `IComponentData`, `ISharedComponentData`, `IBufferElementData`, `GameSystemBase`, `EntityQuery`, `ComponentType.ReadOnly<>/ReadWrite<>/Exclude<>`, `NativeArray<T>`, `Allocator.Temp`. **Hard number:** archetype chunks are 16KB. Worked example: reduce electricity consumption on residential buildings. **Flagged Work-In-Progress, verification needed for version 1.0** — i.e. six major versions stale.

**`Systems`** — https://cs2.paradoxwikis.com/Systems
Headings: `What are systems?` · `System Types` · `SystemBase` · `GameSystemBase` · `ToolBaseSystem` · `TooltipSystemBase` · `UISystemBase` · `Update Phases` · `Choosing an update phase` · `Update phases order` · `Examples` · `Example 1`. **Substantial but visibly incomplete** — contains literal placeholder text `<insert infographic here>` where the update-phase ordering diagram should be, and is marked Work-In-Progress. **This is a real gap:** the update-phase ordering is exactly the thing a complex mod needs and the wiki does not supply it. Hard constraint recorded: `GameSystemBase` update intervals **must be powers of 2**. Phase guidance: Modification phases for data changes, PreSimulation/PostSimulation for timing-sensitive logic, ToolUpdate for tools, UITooltip for tooltips, UIUpdate for UI.

**`Systems and Components catalog`** — https://cs2.paradoxwikis.com/Systems_and_Components_catalog
Headings: `Systems` (with per-job subsections `CitizenAITickJob`, `CitizenReserveHouseholdCarJob`, `CitizenTryCollectMailJob`, `CitizeSleepJob` [sic — typo in the wiki], `FindJobJob`, `StartWorkingJob`, `HandleBuyersJob` → _Entities with ResourceBought components_ / _Entities with ResourceBuyer components_, `BuyJob`) · `Components` · `Other structs`.
**The single richest cross-over page on the wiki** — it is where modding internals and gameplay domain meet. ~12 systems (deep on `CitizenBehaviorSystem`, `ResourceBuyerSystem`, `PathfindQueueSystem`), ~60 components in a Namespace/Name/Description table with per-component property tables (e.g. `Game.Citizens.Citizen` with `PseudoRandom`, `State`, `WellBeing`, `Health`), plus `Colossal.Mathematics` structs like `Bezier4x3`. Depth is uneven: some systems are multi-paragraph, many components are one-line stubs.

**`Common ECS Components`** — https://cs2.paradoxwikis.com/Common_ECS_Components
Headings: `Common Components Overview` · `"Clean Up" Systems` (`PrepareCleanUpSystem`, `CleanUpSystem`). Substantial. ~20 components of the `Game.Common` namespace in a Type/Cleanup/Description table — `Created`, `Deleted`, `Owner` (`m_Owner: Entity`), `PseudoRandomSeed` (`m_Seed: ushort`). Header says "WIP document, content needed."

**`Queries`** — https://cs2.paradoxwikis.com/Queries
Headings: `Getting Started` · `How to create a Query` · `Using GetEntityQuery` · `Using SystemAPI` · `ReadWrite or ReadOnly` · `When to use ReadWrite` · `Why not always use ReadWrite`. Substantial. `GetEntityQuery()` vs `SystemAPI.QueryBuilder()`, `All`/`Any`/`None`, `EntityQueryDesc`, and the performance argument against blanket ReadWrite.

**`PrefabSystem`** — https://cs2.paradoxwikis.com/PrefabSystem
Headings: `TL;DR` · `3 different "things" that often get mixed up` · `Recommended pattern (safe & compatible)` · `Quick Baseline vs Direct write sample` · `Avoid Errors with SystemAPI.Query() (Prefab vs Runtime types)` · `Quick summary` · `References`. Substantial and unusually well-written. **The core distinction it teaches — authoring `PrefabBase` vs prefab-entity `*Data` components vs runtime instance components — is the number-one conceptual trap in CS2 modding.** Includes a quick-reference table contrasting `PrefabBase` / `*Data` / `PrefabRef` / instance components. Explicitly warns about the `Game.Prefabs.DeathcareFacility` vs `Game.Buildings.DeathcareFacility` namespace collision. Covers `PrefabSystem.TryGetPrefab()`, `RefRW<T>`, `EntityCommandBuffer`.

**`Prefab - Quick Start`** — https://cs2.paradoxwikis.com/Prefab_-_Quick_Start (**`PrefabQuickGuide`** redirects here — verified)
Headings: `Maxim` · `Minimal baseline` · `Example authoring components & fields` · `Example ECS *Data components (changeable) you write on prefab-entities` · `Minimal Write to prefab` · `Why workers example is "special"` · `"When do I mutate runtime instance components?"` · `Bonus thing` (stub) · `References` (stub). Substantial. **Overlaps heavily with `PrefabSystem`** — same author, same maxim ("vanilla baseline = the original default values included with the game"), same worked example. These two should be treated as one topic. The workers example explains why prefab edits do _not_ cascade to already-placed buildings and offers three remedies: player-triggered rebuild, custom code, or Harmony patches.

**`Creating a Tool`** — https://cs2.paradoxwikis.com/Creating_a_Tool (**`Tool Systems`** redirects here — verified)
Headings: `Definition` · `The Tool Lifecycle` · `The ToolBaseSystem` · `Lifecycle Methods` · `System Creation` · `Starting and Stopping Running` · `Sharing the Selected Prefab` · `Letting User Select Objects (Raycasting)` · `Responding to Hotkey Presses` · `System Update` · `Disable the Tool`. Substantial despite an "under construction" banner. **The raycasting section is the payload** — full enum tables for `typeMask`, `collisionMask`, `netLayerMask`, utility types, flags. The first two sections are thin and the lifecycle diagram is missing.

**`Commonly units in the game`** — https://cs2.paradoxwikis.com/Commonly_units_in_the_game
Headings: `Units` · `Time :uint`. **Stub — but a load-bearing stub.** It is the only place defining the simulation time unit (aliases `frameIndex` / `UpdateInterval`): **≈182.04 units = 1 minute; 16,384 units = 90 minutes; update-interval values must be a power of 2.** Every other intended unit is unwritten. This is the sharpest documentation gap on the wiki relative to its importance.

**`How To Avoid Memory Leaks`** — https://cs2.paradoxwikis.com/How_To_Avoid_Memory_Leaks
Headings: `Managed vs Unmanaged Code` · `Unity's Unmanaged Collections` · `Example Code`. Substantial. Burst/DOTS unmanaged memory, the three allocators (`Temp`, `TempJob`, `Persistent`), disposal with `JobHandle` to avoid premature deallocation, `OnCreate`/`OnUpdate`/`OnDestroy` lifecycle example.

---

## 3. Modding — assets / maps / editor (out of scope for the plugin, inventoried)

**Editor** (Paradox-verified for **1.5.2f1** — the most current official docs on the wiki):

- `Editor` — https://cs2.paradoxwikis.com/Editor
- `Editor: Interface` — https://cs2.paradoxwikis.com/Editor:_Interface — headings `Asset Importer` · `Asset Browser` · `Photo Mode` · `Bulldoze Mode` · `Simulation Overrides` · `Workspace` · `Infoviews` · `World Camera` · `Advisor & Pause Menu` · `Inspector`. Substantial.
- `Editor: Asset and Prefab Inspector` — https://cs2.paradoxwikis.com/Editor:_Asset_and_Prefab_Inspector — **relevant beyond assets**: it is the in-game view of the prefab component model.
- `Editor: Snapping and Tool Modes` — https://cs2.paradoxwikis.com/Editor:_Snapping_and_Tool_Modes
- Each has a `/Lang` translation-bar subpage.

**Asset pipeline** (all Paradox-verified, most at 1.5.2f1):

- `Asset Creation Guide` — https://cs2.paradoxwikis.com/Asset_Creation_Guide — headings `The Basics` · `General Guidelines` · `Common Terminology in Cities: Skylines II` · `Naming Conventions Matter` · `Size, Scale, Triangle Count` · `Texel Density` · `Textures` · `Asset Categories` · `Detailed Guidelines`. **Carries hard numbers:** FBX 2018, metric 1:1, textures 512²–4096² PNG power-of-2, and the naming schema `<Theme>_<AssetName>_<Level>_<LotSize>_<Module>_<LOD>_<Material>`.
- `Asset Pipeline: Buildings` · `Asset Pipeline: Props` · `Asset Pipeline: Decals` · `Asset Pipeline: Aging Trees` · `Asset Pipeline: Surfaces`
- `Assets: Common Asset Principles` · `Assets: Importing` · `Assets: Importing Decals` · `Assets: Import and Setup Aging Trees` · `Assets: Import and Setup Surfaces` · `Assets: Setting Up Color Variations` · `Assets: Setting Up Emissive` · `Assets: Setting Up Decals` · `Assets: Texture Sharing` · `Assets: Package, Share and Upload`
- Most have `/Lang` subpages.

**Map creation:**

- `Map Creation` — https://cs2.paradoxwikis.com/Map_Creation — headings `Before You Begin` · `Planning Your Map` · `Using Mods` · `Road Map` · `Editor Basics for Map Creators` · `Heightmaps and Terrain` · `Water & Water Sources` · `Climate` · `Outside Connections and Networks` · `Resources` · `Vegetation and Detailing` · `Map Settings and Polish` · `Sharing and Maintaining Maps` · `Community-Made Guides`. Substantial road-map page.
- Ten subpages: `Map Creation: Editor Basics` · `: Terrain` · `: Water` · `: Climate` · `: Outside Connections` · `: Resources` · `: Detailing` · `: Settings and Polish` · `: Creating Custom Map Tile Prefabs` · `: Sharing on Paradox Mods`. **`Map Creation: Creating Custom Map Tile Prefabs` is the one that touches code-mod territory** (prefab authoring).

**Community asset guides:**

- `Substance 3D Painter Setup` · `Train Station Measurements` · `Subway Station Measurements` · `Creating and sharing intersection assets` · `Applying Animation Curves to Multiple Emissive Light Sources`
- `Extra assets importer` — https://cs2.paradoxwikis.com/Extra_assets_importer — a _code mod_ documented as a user tool; headings `How to use` · `How to create`. Existing subpages: `/Basics`, `/Surfaces`, `/Local assets`, `/json files`, `/Advanced`. **Redlinks:** the hub links `/Decals`, `/Netlanes`, `/Recommended mods`, `/Publish` — none exist in AllPages. Status-dated 2025-07-08, author Triton Supreme.
- `Photo Mode` — https://cs2.paradoxwikis.com/Photo_Mode

**Chinese translations (14):** `模组制作` (Modding), `资产制作指南`, `资产流程：建筑`, `资产流程：道具`, `贴花资产流程`, `资产：导入`, `导入贴花`, `设置贴花`, `资产：设置自发光`, `资产：设置颜色变化`, `资产：纹理共享`, `资产：通用资产原则`, `资产：打包、分享与上传`, `编辑器：界面`, `编辑器：资产与预制件检查器`, `编辑器：捕捉与工具模式`. All are asset/editor content — **no gameplay or code-mod page has been translated.**

---

## 4. Gameplay domain

This is where the redirect topology matters most. Fourteen hub articles carry essentially all of it.

### 4.1 The hub articles (substantial)

**`Zoning`** — https://cs2.paradoxwikis.com/Zoning
Headings: `Themes` · `Tool mode` · `Zone Types and Densities` · `Theme-Based Zoning` · `Unthemed Zoning` · `Specialized industry` · `Building level` · `Demand` · `Zone suitability` · `Land value` · `See also` · `References`.
**Hard numbers:** per-zone tile ranges and lot dimensions (e.g. Low Density Housing 4–24 tiles, 2×2 to 4×6), milestone unlock numbers 0–10, pollution tiers per specialized industry. **Relationship material — very strong:** the demand loop (residents need jobs → industrial/commercial demand; manufacturers need retail → commercial demand; companies need workers → residential demand), and the land-value/rent/upkeep loop that drives building level-up 1→5 (residential gains apartments at levels 3 and 5). Several sections marked "copied from source."
_Redirects in (verified): `Building level`, `Specialized industry`. (Inferred): `Specialized Industry`, `District`?, `Demand`, `Residential`, `Commercial`, `Industrial`, `Mixed Residential`, `Industry`._

**`Services`** — https://cs2.paradoxwikis.com/Services
Headings: `Budget` · `Service fees` · `List of services` → `Roads`, `Road Services`, `Electricity`, `Water`, `Sewage`, `Healthcare`, `Deathcare`, `Garbage management`, `Education`, `Fire rescue`, `Police`, `Administration`, `Transportation`, `Parks & recreation`, `Communications` (→ `Post`, `Telecommunications`) · `Service trade` · `References`.
**Numbers:** budget slider 50–150%; at 50% budget service runs at 25% efficiency (non-linear — worth noting); every 1% below 100% raises electricity consumption by +0.2% and fee-driven demand by −0.4%; telecom 1 Gbit/s per citizen. **Explicitly tagged "To be split" and "last verified for version 1.0."** Prose, not tables.
_Redirects in (verified): `Electricity`, `Water & Sewage`, `Education`, `Healthcare`, `Garbage Management`, `Crime`, `Attractiveness`. (Inferred): `Water & sewage`, `Garbage management`, `Garbage`, `Sewage`, `Water`, `Police`, `Fire & Rescue`, `Fire & rescue`, `Deathcare`, `Disaster control`, `Disaster Control`, `Administration`, `Post`, `Postal`, `Mail`, `Telecom`, `Communications`, `Internet`, `Research`, `Parks`, `Parks & Recreation`, `Recreation`, `Road Services`, `Recycle`._
This is **~25 titles collapsing into one article** — the wiki's biggest concentration point.

**`Citizens`** — https://cs2.paradoxwikis.com/Citizens
Headings: `Age` · `Happiness` (Happiness factors; `Well-being` → Leisure; `Health`) · `Education` · `Employment` (Work efficiency; Wages) · `Conditions` · `Criminals` · `Tourists` · `Lifepath` · `References`.
**Densest hard-number page in the gameplay set:** 4 life stages; 5 education levels; school fees ₡50/₡100/₡200/₡200 per month; wages ₡1,500–₡2,700/month by education; Family Allowance ₡400/mo, Pension ₡1,200/mo, Unemployment Benefit ₡800/mo capped at 10 days; Residential Minimum Earnings ₡1,400/mo; Commuter Wage Multiplier 1.1. **Relationship material:** education → job level cap → work efficiency → company output; happiness = well-being + health → work efficiency and crime probability. Verified for **1.0**.
_Redirects in (verified): `Happiness`. (Inferred): `Age`, `Well-being`, `Health`, `Leisure`, `Criminals`, `Prisoners`, `Residents`, `Tourists`, `Lifepath`, `Education` (partly)._

**`Economy`** — https://cs2.paradoxwikis.com/Economy
Headings: `Taxation` · `Service Fees` · `Service Trade` · `Loans` · `Production` (Companies; Efficiency; Materials; Material goods; Immaterial goods) · `References`.
**Formula/table-rich:** tax range −10%…+30%; parking fees ₡0–₡50; electricity default fee 0.2, water 0.3; healthcare ₡100/mo per hospitalized citizen; electricity import ₡5,000/MW, export ₡2,500/MW; loan interest 2.3–20%, reduced 1% by City Hall and 2% by Central Bank; max production efficiency bonus **115% at 10 kt of production**.
**Three input/output tables that are effectively the game's production graph:** _Materials_ (wood, grain, livestock, vegetables, cotton, crude oil, metal ore, coal, rock), _Material goods_ (metals, steel, minerals, concrete, machinery, petrochemicals, chemicals, plastics, pharmaceuticals, electronics, vehicles, beverages, convenience food, food, textiles, timber, paper, furniture), _Immaterial goods_ (software, telecom, financial, media, lodging, meals, entertainment, recreation). **This is the highest-value relationship table on the wiki for a mod author.**
Weakness: the `Companies` / `Efficiency` sections are qualitative — no profitability formula, no staffing thresholds.
_Redirects in (verified): `Companies`, `Resources`, `Efficiency`. (Inferred): `Goods`, `Company`, `Marketplace`?, `Shopping`._

**`Roads`** — https://cs2.paradoxwikis.com/Roads
Headings: `Driving side` · `Development Tree` · `Road construction and tools` · `Tool mode` · `Elevation` · `Parallel mode` · `Snapping` · `Miscellaneous` · `Directions` · `Roundabouts` · `Road types` · `Parking spaces`.
**The largest hard-numbers table on the wiki:** 60+ road variants with speed limit (20–120 km/h), base cost per km (₡1K–₡27.5K), monthly upkeep, elevated cost, tunnel cost, car lane count (1–8), noise factor, air pollution factor, traffic-light capability. Plus a second table of 15+ parking facilities (capacity, cost, upkeep, comfort) and roundabouts in 4 sizes ₡200–₡1,500. Other hard facts: all non-highway roads carry 40 MW low-voltage capacity plus water and sewage; highways produce 3× the noise of standard streets and forbid adjacent zoning.
_Redirects in (verified): `Speed limits`, `Roundabouts`. (Inferred): `Road`, `Bridges and Ports`?, `Parking`, `Parking Spaces`._

**`Traffic`** — https://cs2.paradoxwikis.com/Traffic
Headings: `Route selection` · `Traffic accidents` · `Intercity traffic` · `Finding traffic` · `References`.
**No numbers at all** — purely qualitative, which is a notable gap given how central pathfinding is. But **the relationship content is excellent:** pathfinding weights are Time / Comfort / Money / Behavior, weighted per demographic ("Time is the foremost factor for Adult citizens", "Comfort is the foremost factor for Senior citizens"); service vehicles dispatch by "lowest overall pathfinding cost" accounting for future vehicle positions; road condition and streetlights reduce accident probability. Several sections flagged outdated / needing verification.
_Redirects in (verified): `Pathfinding`. (Inferred): `Accidents`, `Traffic accidents`._
**Pairs directly with `PathfindQueueSystem` in the Systems catalog** — one of the few clean gameplay↔internals bridges.

**`Transportation`** — https://cs2.paradoxwikis.com/Transportation
Headings: `Building networks` · `Passenger transport` · `Bus` · `Taxi` · `Tram` · `Subway` · `Passenger train` · `Ferry` · `Passenger ship` · `Passenger plane` · `Cargo transport` · `Cargo train` · `Cargo plane` · `Cargo ship` · `Oil carrier` · `Transportation Info View` · `See also` · `References`.
Per-mode spec tables with cost, upkeep/month, XP, vehicle counts (e.g. Bus Depot ₡150,000 / ₡23,250 upkeep / 25 vehicles; International Airport ₡4,000,000 / ₡150,000 upkeep / 18 gates). Substantial.
_Redirects in (inferred): `Bus`, `Bus Depot`, `Taxi`, `Taxi Depot`, `Tram`, `Subway`, `Metro`, `Train`, `Passenger train`, `Passenger ship`, `Passenger plane`, `Cargo train`, `Cargo plane`, `Cargo Ship`, `Ship`, `Seaways`._

**`Service buildings`** — https://cs2.paradoxwikis.com/Service_buildings
Headings by service family: `Communications` (Post, Telecom) · `Education & Research` (Education, Research) · `Electricity` · `Fire & Rescue` (Fire, Disaster Control) · `Garbage Management` · `Healthcare & Deathcare` · …
**The definitive building-stat catalog: 50+ buildings, 200+ rows with upgrade variants.** Columns: size in cells, requirements, cost, monthly upkeep, XP, capacity/production/efficiency, pollution, workforce count and education requirement, service range, service magnitude, consumption rates. **This is the page a mod author would scrape for balance work.**

**`Signature buildings`** — https://cs2.paradoxwikis.com/Signature_buildings
Headings: `Residential buildings` · `Mixed residential buildings` · `Commercial buildings` · `Industrial buildings` · `Office buildings` · `References`. ~115 entries across five tables. Columns vary by type: Name, DLC, Size, Theme, Requirements, XP, Effects, Households / Attractiveness / Employees / Goods-Services sold / Noise pollution. Unlock conditions include milestones, happiness thresholds, and zoned-cell counts. Substantial, table-dense.

**`Progression`** — https://cs2.paradoxwikis.com/Progression
Headings: `Milestones` · `Development` · `Map Tiles` · `Map Tiles Upkeep` · `References`.
**Numbers:** 20 milestones (Tiny Village → Megalopolis); rewards ₡25,000–₡500,000; development points +1 → +30 (cumulative total 232); expansion permits 3 → 56 total; dev-tree node costs 1/2/4/8 points by tier; **441 map tiles of 0.4 km² each, 9 owned at start**, tile cost scaling ≈ +₡125 per tile unlocked plus buildable-area and resource factors. Includes a **1.1.5f1 before/after changelog table** for all 20 milestone money rewards. Flagged "Potentially outdated."
_Redirects in (verified): `Milestones`, `Map Tiles`. (Inferred): `Milestone`, `Development`, `Development Points`._

**`Climate`** — https://cs2.paradoxwikis.com/Climate
Headings: `Weather` · `Months and seasons` · `Natural disasters` (Forest fire; Hail storm; Tornado) · `References`.
**Numbers:** minimum electricity consumption between 18 °C/64 °F and 22 °C/71 °F, rising to 200% below −18 °C or above 58 °C; solar output −25% on cloudy days; one month = one day/night cycle, one season = three months; three climate types. Good relationship content (weather → indoor vs outdoor leisure choice; snow → snowplows).
_Redirects in (verified): `Disasters`. (Inferred): `Seasons`, `Weather`, `Rain`, `Forest fire`, `Hail storm`, `Tornado`, `Air`._

**`Pollution`** — https://cs2.paradoxwikis.com/Pollution
Headings: `Ground pollution` · `Groundwater pollution` · `Air pollution` · `Water pollution` · `Noise pollution` · `References`.
**No numbers** — only a vague mention that trees reduce noise on "a fairly weak log scale." But **rich relationship content:** ground pollution → resident health, tree death, destruction of fertile land, and contamination of adjacent groundwater; groundwater pollution spreads until ground pollution is removed; air pollution advects with wind, dilutes, and is scrubbed by rain; water pollution flows downstream into intakes; noise scales with traffic volume. Verified for **1.0**.
_Redirects in (inferred): `Ground pollution`, `Groundwater pollution`, `Air pollution`, `Noise pollution`, `Industrial Ground Pollution`._

**`Natural resources`** — https://cs2.paradoxwikis.com/Natural_resources
Headings: `Exploitation` · `Groundwater` · `Depletion of resources` · `See also` · `References`. Five resource types: Groundwater, Fertile Land, Forest, Ore, Oil. Substantial prose, **no numeric tables**. `Natural Resources` and `Natural resource` are casing/number variants (inferred redirects), as are `Forest`, `Ore`, `Oil`, `Fertile land`, `Fertile Land`, `Groundwater`.

**`Info views`** — https://cs2.paradoxwikis.com/Info_views
33 info views, each a heading: `Roads` · `Traffic` · `Electricity` · `Water & Sewage` · `Healthcare & Deathcare` · `Garbage Management` · `Fire & Rescue` · `Disaster Control` · `Police` · `Administration` · `Education` · `Transportation` · `Post` · `Telecom` · `Leisure` · `Tourism` · `Outside Connections` · `Residential` · `Commercial` · `Industrial` · `Office` · `Building Level` · `Land Value` · `Company Profitability` · `Natural Resources` · `Population` · `Happiness` · `Citizen Wealth` · `Workplace Availability` · `Air Pollution` · `Ground Pollution` · `Noise Pollution` · `Water Pollution` · `View details` · `References`. Substantial, with a comparison table and colour-coding conventions.
**Structurally this is the game's own taxonomy of its simulation dimensions** — arguably the cleanest available decomposition of the domain, and it maps near-1:1 onto the ECS component families.

**`Notifications`** — https://cs2.paradoxwikis.com/Notifications
Headings: `Notification tiers` · `Details` · `Economy` · `Electricity` · `Water` · `Sewage` · `Other Services` · `Construction / Placing` · `Placed buildings` · `Transportation` · `Networks` · `Pollution` · `Miscellaneous` · `References`. **90+ notifications** in tables with icon, title, description, severity tier, and resolution guidance. Substantial and underrated: **it is a complete enumeration of the failure states the simulation can enter**, which is precisely the surface a diagnostic or overlay mod would target.

**`Maps`** — https://cs2.paradoxwikis.com/Maps
Headings: `Official maps` · `References`. One big table, **26 maps** with thumbnail, bundled DLC, theme, climate in °C and °F, latitude, buildable area in km², outside connections (road/rail/boat/air/power), and resource quantities (fertile land, forest, ore, oil, fish) in kt/t. Substantial, hard numbers throughout.

**`Landscaping`** — https://cs2.paradoxwikis.com/Landscaping
Headings: `Terraforming` · `Vegetation` (Base Game Trees) · `Pathways` (Pedestrian bridges) · `Piers and quays` · `Bicycle paths and quays` · `Surfaces` · `Nature` · `Park` · `Potted plants` · `Residential props` · `Commercial props` · `Industrial props` · `Debris` · `Decals` · `References`. Substantial catalog. Numbers: brush size 10–1000 (10 ≈ 7/8 of a zoning cell), brush strength 1–100%, vegetation ₡10 apiece. 11 base-game trees listed.
_Redirects in (inferred): `Terraforming`, `Trees`, `Vegetation`._

### 4.2 Smaller gameplay pages

- **`Theme`** — https://cs2.paradoxwikis.com/Theme — two base themes (North American, European); locked to map theme: road markings, speed-limit signage, traffic lights, some service/delivery vehicles; theme-agnostic: zoned buildings, signature buildings, some transit stops, landscaping. Moderate. `European theme`, `North american theme` are likely separate short pages or redirects.
- **`Districts`** — https://cs2.paradoxwikis.com/Districts — headings `Districts` · `See also` · `References`. **Marked stub, verified for 1.0.** Key rule worth extracting: services not assigned to a district serve the whole city; assigned services are confined to their district. `District` is the singular variant.
- **`Tourism`** — https://cs2.paradoxwikis.com/Tourism — attractiveness (seasonal), hotels/lodging in commercial zones, leisure destination choice by distance × attractiveness, high-attractiveness "attractions" overriding distance. Moderate; includes a 1.1.10f1 bugfix note. Related: `Attractions`, `Tourist attractions`, `Tourists`, `Leisure`, `Entertainment`.
- **`Vehicle`** — https://cs2.paradoxwikis.com/Vehicle (`Vehicles` redirects here — verified) — headings `General` · `Services` · `Transportation` · `References`. **Stub.** States vehicles have passenger/cargo capacity, noise rating and speed, but gives no table. A real gap.
- **`Development Tree`** — https://cs2.paradoxwikis.com/Development_Tree — headings `Development Trees` · `Gallery`. **Stub**: one paragraph plus 11 tree images (Roads, Electricity, Water & Sewage, Healthcare & Deathcare, Garbage Management, Education & Research, Fire & Rescue, Police & Administration, Transportation, Parks & Recreation, Communications). The node/cost data exists only inside `Progression`.
- **`Policies`** — https://cs2.paradoxwikis.com/Policies — headings `City policies` · `District policies` · `Building policies` · `References`. **Only 14 policies documented** (6 city, 7 district, 1 building) with icon, name, milestone requirement, effect. Tabular but thin relative to the current game — a likely staleness casualty.
- **`Landmarks`**, **`Attractions`**, **`Leisure Venues`**, **`Beach Properties`**, **`Bridges and Ports`**, **`City Stations`**, **`Skyscrapers`**, **`Urban Promenades`**, **`Modern Architecture`**, **`Mediterranean Heritage`**, **`San Francisco Set`**, **`Office Evolution`**, **`Supply Chains`** — these are **DLC/CCP product pages**, not mechanics pages (confirmed for `Supply Chains`: headings `Official description` · `Features` · `Official screenshots` · `Official videos` · `Bundles` · `Developer diaries` · `Trivia` · `Patch notes` · `References`). `Supply Chains` does contain a **resource-processing chain chart across 8 resource types**, which is genuinely useful domain data hiding inside a marketing page.
- **`Service building data test`** — https://cs2.paradoxwikis.com/Service_building_data_test — **an unlinked raw data dump**, last edited **2 August 2023** (pre-release). ~100 buildings with circular flag, width, depth, cost, XP, upkeep, electricity/water consumption, garbage accumulation, workplace count, complexity level, service range, capacity, magnitude. **Almost certainly the stalest quantitative page on the wiki** — it predates launch. Flag it as a trap: the schema is informative, the values are not trustworthy.

### 4.3 DLC content-item pages (~90, all thin)

Individual signature buildings and radio stations from CCPs and music packs. Examples: `Architect's Mansion`, `CO10 Condos`, `Cane Residences`, `Century Castle`, `Chemical Plant`, `Colossal Tower`, `Constellation Apartments`, `Corundum Condos`, `Dairy House`, `Deluxe Relax Station`, `Dragon Gate`, `Ember Suites`, `Epicurean Garden`, `Extreme Athlete's Villa`, `Fashion Square`, `Figura Building`, `Film Actor Mansion`, `Food Station`, `Fuel Plant`, `Gatehouse Residences`, `Golfer's Villa`, `Halo Heights`, `Industry Mogul's Mansion`, `Ironpress Building`, `Ludo Square`, `Mollari Palace`, `Multistory Multimedia`, `Muscle Car Garage`, `Oil Refinery`, `Old Factory Condos`, `One Stop Station`, `Painter Mansion`, `Paper Factory`, `Polaris Suites`, `Pop Musician Mansion`, `Real Estate Agent's Mansion`, `Rock Musician Mansion`, `Royal Villa`, `Rubique Apartments`, `Sculptor Mansion`, `Square Center`, `Streamline Diner`, `Stylus Tower`, `The Capacitor Building`, `The Emerald Building`, `The Grass Crown`, `The Marvelous Marble`, `Theater Actor Mansion`, `Vehicle Factory`, `Vertigo Square`, `Villa City`, `Vista Building`, `Watanabe Tower`, `Waterfall Array`, `Waveform Tower`, `Activity Plaza`, `Coder Park`, `Ground Earth`, `Baltar Pines`, `Cituese`, `Incaserium`, `Principiis`, `Switchon`.
Radio stations: `Atmospheric Piano Channel`, `Cloud Lounge FM`, `Cold Wave Channel`, `Feelgood Funk Radio`, `Jade Road Radio`, `Skyrail Radio`, `Smooth Vibes FM`, `Soft Rock Radio`, `Synth & Steel Radio` / `Synth and Steel Radio` (duplicate pair).
**Zero value for a modding skill beyond confirming that individual prefabs get wiki pages.** Aggregate data for these already lives in the `Signature buildings` tables.

---

## 5. Meta / player

- `Cities Skylines II` — the game's main article. `Cities Skylines 2` is a variant.
- `Cities Skylines II Wiki` — the Main Page. `Main Page`, `Main Page/links`, `Main Page/news` are its infrastructure. `Cities Skylines II Wiki:Style` — style guidelines.
- `Beginner's guide` — https://cs2.paradoxwikis.com/Beginner%27s_guide — headings `Basics` (Road-building basics, Basic services and early budget management, Transportation, Services, Planning) · `Principles` (Value Zones, Transit First, Mixed Use, Road Tiers, Green Links, Buffer Zones, Water Base, Staged Growth) · `Other`. Substantial — but **explicitly says it was adapted from the _Cities: Skylines 1_ guide and "not all of it might apply to Cities Skylines II."** Treat as low-trust for CS2 mechanics.
- `Tutorial videos`
- `Patches` — https://cs2.paradoxwikis.com/Patches — `Version history`, 35 patch links. **Latest: 1.6.0f1 "Summer Solstice", 2026-06-22.** Plus grouping pages `Patch 1.0.X`, `Patch 1.1.X`, `Patch 1.2.X`, `Patch 1.3.X`, `Patch 1.4.X`, `Patch 1.5.X`, `Patch 1.6.X`, and stragglers `Patch 1.0.9f1`, `Patch 1.3`, `Patch 1.4`, `Patch 1.5`, `Patch 1.6`. Version stub pages `1.2`, `1.3`, `1.4`, `1.5`, `1.6`.
- `Developer diaries`
- `Achievements`
- `DLC` / `Downloadable content` (duplicate pair) · `Pre-order pack` · `Region packs`
- `Colossal Order` · `Iceflake Studios` · `Paradox` · `Paradox Interactive` · `Contact us`
- `Map` — likely disambiguation vs `Maps`.

---

## 6. Flags (step 5)

### Substantial and worth reading in full

`Modding Toolchain`, `Options UI`, `UI Modding`, `Mod Key Binding`, `Localize your mod`, `PrefabSystem` + `Prefab - Quick Start`, `Creating a Tool`, `Queries`, `ECS - Entity Component System`, `Systems and Components catalog`, `Common ECS Components`, `How To Avoid Memory Leaks`, `Debugging`, `Logging`, `Naming Folder And Files`, `Roads`, `Service buildings`, `Signature buildings`, `Citizens`, `Economy`, `Zoning`, `Progression`, `Maps`, `Info views`, `Notifications`, `Transportation`, `Asset Creation Guide`, `Editor: Interface`.

### Stubs — including some that matter

- **`Commonly units in the game`** — the _only_ source for the simulation time unit, and 90% unwritten. **Highest-impact stub on the wiki.**
- **`Vehicle`** — no stat table at all.
- **`Development Tree`** — images only, no node data.
- **`Districts`** — one paragraph for a mechanic that gates service scoping.
- **`Mods`** — installation section explicitly requests content.
- `Companies`/`Efficiency` sections inside `Economy` — no profitability or efficiency formulas.
- `Traffic` — substantial prose but **zero numbers** for the most numerically interesting system in the game.
- `Pollution` — same problem: good relationships, no coefficients.
- `Systems` — placeholder `<insert infographic here>` where the update-phase order should be.

### Stale / contradictory

| Page                                                        | Issue                                                                                                                                                                                                                                             |
| ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Modding Toolchain`                                         | Verified 1.1.12f1; game is at 1.6.0f1.                                                                                                                                                                                                            |
| `ECS - Entity Component System`, `Systems`                  | Marked WIP, verification pending for **1.0**.                                                                                                                                                                                                     |
| `Citizens`, `Pollution`, `Services`, `Traffic`, `Districts` | "Last verified for version 1.0" banners.                                                                                                                                                                                                          |
| `Progression`                                               | "Potentially outdated"; contains its own 1.1.5f1 before/after table, so numbers elsewhere on the page may be from either era.                                                                                                                     |
| `Service building data test`                                | Last edited **2 August 2023** — pre-launch data. Do not trust values.                                                                                                                                                                             |
| `Debugging`                                                 | Documents that the automated NuGet path is **broken as of 1.5.7f1** — an internal contradiction with its own recommendation.                                                                                                                      |
| `Developer mode` vs `Launch Parameters`                     | `-developerMode` (single dash) vs `--developerMode` (double dash).                                                                                                                                                                                |
| `Beginner's guide`                                          | Self-declared as CS1 content of uncertain applicability.                                                                                                                                                                                          |
| `Policies`                                                  | 14 policies only; near-certainly incomplete for 1.6.                                                                                                                                                                                              |
| Wiki-wide                                                   | Category **"Potentially outdated" = 30 members**, **"Need editing" = 16**, **"Under construction" = 7**, **"To be split" = 2**, **"Verification needed" = 2**, **"Articles with potentially outdated sections" = 3**, **"…outdated tables" = 2**. |

### Duplicate / near-duplicate titles

`Community-Made Guides` ↔ `Community-made guides` · `DLC` ↔ `Downloadable content` · `Verifying game files` ↔ `Veryifying game files` (typo) · `Synth & Steel Radio` ↔ `Synth and Steel Radio` · `Natural Resources` ↔ `Natural resources` ↔ `Natural resource` · `Fertile Land` ↔ `Fertile land` · `Garbage` ↔ `Garbage Management` ↔ `Garbage management` · `Fire & Rescue` ↔ `Fire & rescue` · `Water & Sewage` ↔ `Water & sewage` · `Specialized Industry` ↔ `Specialized industry` · `Disaster Control` ↔ `Disaster control` · `PrefabQuickGuide` ↔ `Prefab - Quick Start` · `Tool Systems` ↔ `Creating a Tool` · `Cities Skylines 2` ↔ `Cities Skylines II` · `Milestone` ↔ `Milestones` · `Post` ↔ `Postal` ↔ `Mail`. Also a typo _inside_ content: `CitizeSleepJob` in the Systems catalog.

### Pages carrying hard numbers, formulas, unit definitions or tables (the extraction targets)

1. `Roads` — 60+ road variants × ~10 numeric columns; parking table. **Largest.**
2. `Service buildings` — 200+ rows of building stats.
3. `Signature buildings` — ~115 entries × unlock/effect columns.
4. `Economy` — tax/fee/loan constants + the three-tier production graph tables.
5. `Citizens` — wages, benefits, school fees, thresholds, multipliers.
6. `Maps` — 26 maps × climate/area/resource quantities.
7. `Progression` — 20 milestones, dev-point totals, tile math (441 tiles, 0.4 km², +₡125/tile).
8. `Notifications` — 90+ failure states with tiers.
9. `Info views` — 33 simulation dimensions.
10. `Transportation` — per-mode cost/upkeep/capacity.
11. `Commonly units in the game` — the frameIndex/UpdateInterval conversion (182.04 ≈ 1 min; 16,384 = 90 min; power-of-2 constraint).
12. `Options UI` / `Mod Key Binding` — complete attribute catalogs.
13. `Creating a Tool` — raycast mask enum tables.
14. `Systems and Components catalog` / `Common ECS Components` — namespace/name/property tables.
15. `Asset Creation Guide` — mesh/texture/naming specs.
16. `Modding Toolchain` — tool versions and paths.
17. `Climate` — temperature/consumption curve endpoints.
18. `Localize your mod` — vanilla translation key namespaces.
19. `Supply Chains` (DLC page) — the 8-resource processing chart.
20. `Service building data test` — schema good, values pre-launch.

### Pages describing relationships between features ("how does X affect Y")

Ranked by density:

1. **`Zoning`** — the demand loop and the rent/upkeep/land-value/level-up loop. The best single source for feedback structure.
2. **`Citizens`** — education → job level → work efficiency → company output; happiness → efficiency and crime.
3. **`Economy` → Production** — the full materials → material goods → immaterial goods graph, with which zone type produces and consumes each.
4. **`Traffic`** — pathfinding cost weights (Time/Comfort/Money/Behavior) varying by demographic; service dispatch by path cost.
5. **`Pollution`** — the five-way cross-contamination web (ground → groundwater, ground → fertile land, sewage → water + ground, garbage → multiple).
6. **`Services`** — budget → efficiency → coverage → land value → rent; fee → consumption elasticity (+0.2% / −0.4% per point).
7. **`Climate`** — temperature → electricity, cloud → solar, weather → leisure choice, season → tourism.
8. **`Systems and Components catalog`** — the _implementation_ of several of the above (`CitizenBehaviorSystem`, `ResourceBuyerSystem`, `PathfindQueueSystem`, `HandleBuyersJob`/`BuyJob`).
9. **`Notifications`** — the inverse view: which relationships break and how the game reports it.
10. **`Tourism`** — attractiveness → tourist volume → lodging demand → commercial revenue.
11. **`Natural resources`** — extraction → depletion → specialized industry viability.

---

## 7. What the wiki's own structure says about the domain (step 6)

### The Main Page navbox — the community's mental model

The main page groups the wiki into six buckets, and this is the clearest statement of how the community decomposes the game:

- **Getting Started** — Beginner's guide, Tutorial videos
- **Governance** — Progression, Zoning, Services, Policies, Traffic, Info views
- **Infrastructure** — Roads, Transportation, Service buildings, Signature buildings, Landscaping
- **Concepts** — Citizens, Economy, Climate, Natural resources, Pollution
- **Misc** — Maps, **Modding (Landing Page)**, Community-made guides, Developer diaries, Patches, Achievements, Downloadable Content
- **Game History** — recent patches and DLC

Two observations matter for structure design. First, the split between **Governance** (things the player _decides_) and **Concepts** (things the simulation _does_) is exactly the split a mod author needs — Governance pages map to player-facing UI and tools, Concepts pages map to simulation systems and components. Second, **Modding sits in "Misc"** — the wiki treats modding as an appendix to the game, not a peer of it. Our skill has to invert that relationship, which means we cannot simply mirror this tree.

### The Modding page's own tree

`Modding` decomposes as Official (Code Mods / Editor / Maps / Assets) × Community (General / Asset Guides / Developing Code Mods → Setting Up | Guides | Knowledgebase | Tips and Tricks). The **Setting Up / Guides / Knowledgebase / Tips-and-Tricks** quadrisection is a good skeleton: it separates one-time environment work, task recipes, reference material, and pitfalls. That maps cleanly onto a skill with a short SKILL.md (setup + recipes) and deeper `references/` files (knowledgebase + pitfalls).

### The category system

~156 categories, but the semantically meaningful ones are few. Content categories: `Modding` (84), `Assets` (34), `Extra assets importer` (34), `Community-made modding guides` (11), `Asset modding guides` (3), `Mods` (13), `Game concepts` (20), `Services` (16), `Transportation` (8), `Patches` (8), `DLC` (126), `Zoning` (1), `Road Builder` (4), `Pathways` (6), `Pedestrian` (6), `Subway` (17), `Supply Chains` (39), `Skyscrapers` (26), `Timeless` (36), `Music Pack` (38), `Leisure Venues` (36).

**The overwhelming majority of categories are image categories**, not content categories — `Images © Paradox` (1,242), `Service upgrade icons` (131), `Service building icons` (117), `Signature building unlock images` (117), `Road icons` (111), `Official screenshots` (110), `Icons` (95), `Notification icons` (82), `Signature building icons` (81), `Modding Screenshots` (67). The category system is primarily a media-asset filing system; it is **not** a reliable index of conceptual structure. The navbox is a much better signal.

Note also the aspirational-but-empty scaffolding: ten `Community-made map * guides` categories all sit at **0 members**, as does `Info view screenshots`. The community planned a map-guide taxonomy and never filled it.

### The decomposition I'd draw from this

The wiki, once you collapse its redirects, resolves into a small number of real objects:

1. **A toolchain layer** — build, publish, debug, log, configure, localize, bind keys. Mechanical, well-documented, version-fragile. One page dominates (`Modding Toolchain`) and it is the stalest important page.
2. **An ECS layer** — entities, three component kinds, archetypes, queries, systems, update phases, allocators. Well-covered conceptually, with one glaring hole (update-phase ordering) and one glaring unit gap (frameIndex).
3. **A prefab layer** — the authoring / prefab-entity / instance triad. Documented twice, well, by the same author. This is the layer where most mods actually intervene, and where the wiki is most explicitly cautionary.
4. **A simulation-domain layer** — and critically, the wiki's own `Info views` page (33 views) and `Systems and Components catalog` (~12 systems, ~60 components) describe _the same domain from two sides_. The info views are the player-visible projection of the component data. That correspondence is the strongest available bridge between gameplay knowledge and modding knowledge, and it is the thing I'd build the reference structure around: for each simulation dimension, pair the gameplay hub article (mechanics, numbers, relationships) with the component/system names that implement it.
5. **A content/balance-data layer** — `Roads`, `Service buildings`, `Signature buildings`, `Economy`'s production tables, `Maps`. Pure numbers. Useful as extractable data, not as prose.
6. **Asset/editor/map authoring** — genuinely separable, and the wiki already separates it cleanly (it's the only part with translations and the only part Paradox has verified against a recent version, 1.5.2f1). Safe to inventory and exclude.

The one structural thing the wiki does _not_ give us, and which our skill will have to supply: nothing on the wiki explains **which vanilla systems to hook or patch to achieve a given gameplay change**. `PrefabSystem` gestures at Harmony once. The gap between "here is the ECS" and "here is how the economy works" is never bridged with "and therefore, to change X, you modify Y." That bridge is the actual value our skill would add.
