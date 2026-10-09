# Warn about companies heading for bankruptcy

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. Once per in-game day I want it to log every company in my city that is on course to go bankrupt and leave, so I can react (cut their taxes, fix their supply) before they actually pack up. For each one the line should say what the company produces, roughly how much it is losing per day, and how close it is to going bankrupt.

## Checklist

### Leaving is predicted from the bankruptcy condition, not read off the `MovingAway` tag

- Falls in: the system finds leaving companies by querying or testing `Game.Agents.MovingAway` on company entities, through `ComponentType.ReadOnly<MovingAway>()`, `HasComponent<MovingAway>` or the like, alone or as the "leaving" signal beside another test.
- Avoids: the system recomputes the game's condition, the company's worth below `EconomyParameterData.m_CompanyBankruptcyLimit` (optionally with `CompanyStatisticData.m_LastFrameLowIncome` for how long it has been there), and no query or lookup names `MovingAway`.
- Reference: cs2-modding/references/mechanics/economy-and-companies/production-and-profit.md
- Source: src/Game/Game.Simulation/CompanyMoveAwaySystem.cs:217

### Extractor companies are left out

- Falls in: the company query matches every `Game.Companies.ProcessingCompany` (or every holder of `CompanyStatisticData`, `TaxPayer` or `Profitability`) with no `ComponentType.Exclude<Game.Companies.ExtractorCompany>()` and no per-entity `HasComponent<ExtractorCompany>` skip.
- Avoids: the query excludes `Game.Companies.ExtractorCompany`, or the loop skips an entity carrying it.
- Reference: cs2-modding/references/mechanics/economy-and-companies/production-and-profit.md
- Source: src/Game/Game.Simulation/CompanyMoveAwaySystem.cs:339

### The bankruptcy threshold is read from the live `EconomyParameterData` singleton

- Falls in: the threshold is a literal in the mod, such as `-5000000` copied from `EconomyPrefab`'s field initializer, a `const` holding it, or any invented figure.
- Avoids: the threshold is `m_CompanyBankruptcyLimit` read from the `EconomyParameterData` singleton at update time, through `GetSingleton<EconomyParameterData>()` on a query for that component.
- Reference: cs2-modding/references/mechanics/economy-and-companies/economy-and-companies.md
- Source: src/Game/Game.Prefabs.Modes/EconomyParametersMode.cs:122

### The figure compared against the threshold is total worth, not cash

- Falls in: the comparison takes the company's money alone, `EconomyUtils.GetResources(Resource.Money, resources)` or the `Resource.Money` row of the `Resources` buffer read by hand, against the threshold.
- Avoids: the comparison takes `EconomyUtils.GetCompanyTotalWorth(...)`, or `CompanyStatisticData.m_Worth` / `m_LastUpdateWorth`, against the threshold.
- Reference: cs2-modding/references/mechanics/economy-and-companies/production-and-profit.md
- Source: src/Game/Game.Simulation/CompanyMoveAwaySystem.cs:132

### The daily loss comes from `CompanyStatisticData.m_Profit`, not from `Profitability`

- Falls in: the logged loss is `Profitability.m_Profitability`, printed as a money amount, tested for sign, or re-centred on 127 and scaled.
- Avoids: the logged loss is `CompanyStatisticData.m_Profit` (or its `m_Income` minus its cost fields), and `Profitability` is read for nothing.
- Reference: cs2-modding/references/mechanics/economy-and-companies/production-and-profit.md
- Source: src/Game/Game.Simulation/CompanyProfitabilitySystem.cs:92

### The recipe is read from the company's prefab

- Falls in: `IndustrialProcessData` is read off the company entity, through `GetComponentData<IndustrialProcessData>(company)`, `TryGetComponent(company, out IndustrialProcessData ...)` or a `ComponentLookup` indexed by the company.
- Avoids: `IndustrialProcessData` is read off the entity in the company's `PrefabRef.m_Prefab`.
- Reference: cs2-modding/references/mechanics/economy-and-companies/production-and-profit.md
- Source: src/Game/Game.Prefabs/ProcessingCompany.cs:21

## Roots

- decompile
