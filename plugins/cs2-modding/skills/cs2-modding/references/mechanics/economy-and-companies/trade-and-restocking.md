# Trade and restocking

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A company holds no money field: money is the `Resource.Money` row of its `Game.Economy.Resources` buffer, written through `EconomyUtils.AddResources` and `EconomyUtils.SetResources` (`src/Game/Game.Economy/EconomyUtils.cs`).
A grep for call sites passing `Resource.Money` is the first sweep on the routing below, not a census: a resource-agnostic loop over a whole buffer moves the money row without ever naming it (`src/Game/Game.Simulation/PartnerSystem.cs` merging one household's buffer into another is the shape).
The table is the company-facing flows; household money is `citizens-and-households`.

| Flow | Where it moves |
| --- | --- |
| A sale | `ResourceBuyerSystem` debits the buyer and credits a non-storage seller with a property (below) |
| A delivery | `DeliveryTruckAISystem` credits the cost payer and debits the target, both skipped for a `StorageCompany`; a refund delivery books the same money pair without delivering the goods; a storage transfer charges the cost payer only the transport cost |
| A storage transfer's two ends | `StorageTransferSystem` |
| A weightless export | `ResourceExporterSystem` pays immediately (below) |
| Office output | `OfficeAISystem` credits the office company directly (below) |
| Lodging and leisure | `LodgingProviderSystem` and `LeisureSystem` debit the visitor and credit the provider |
| Wages | `PayWageSystem` debits the company per employee |
| Dividends, rent, tax, fees | `CompanyDividendSystem`, `PropertyRenterSystem`, `TaxSystem`, `UtilityFeeSystem` and `ServiceFeeSystem` debit the company |
| Building upkeep | `BuildingUpkeepSystem` splits the non-material share of each pass's upkeep slice across the renters and debits each — a second recurring property cost beside the rent, skipped entirely when the renters together cannot cover it (the building's condition drops instead; `zoning-buildings-and-land-value` owns the system) |
| Robbery, starting capital | `CriminalSystem`; `CompanyInitializeSystem` |

## A sale

Sources: `src/Game/Game.Simulation/ResourceBuyerSystem.cs`, `src/Game/Game.Economy/EconomyUtils.cs`.

```
ResourceBuyerSystem.BuyJob, per queued sale:
  price = (seller is commercial ? marketPrice : industrialPrice)(resource) * amount
  seller has a TradeCost buffer:
    price += amount * seller TradeCost.m_BuyCost
    perUnit = observed transport cost / (1 + amount)
    seller not an outside connection and not commercial:
      m_SellCost = lerp(m_SellCost, perUnit + buyer's m_SellCost, 0.5)
    buyer has a TradeCost buffer and is not an outside connection:
      m_BuyCost pulled toward perUnit + seller's m_BuyCost -- an improvement taken outright, a worsening lerped at 0.5
  if the seller's stock of the resource is <= 0: abort the sale here -- the trade costs above have already moved, and nobody pays
  commercial seller (ServiceAvailable present) with a property:
    price *= GetServicePriceMultiplier(m_ServiceAvailable, m_MaxService)
             = lerp(0.7, 1.3, saturate(1 - available / max))
    ServiceAvailable = max(0, round(available - amount))
    m_MeanPriority = min(1, lerp(m_MeanPriority, available / max, 0.1)), assigned without the lerp when it was <= 0
  seller stock -= min(stock, amount) unless the seller is a StorageCompany
  buyer pays round(price); a non-storage seller with a property is credited the same
  a SaleFlags.Virtual sale adds the resource straight to the buying company's buffer, with no delivery
```

**A shop with empty shelves charges 1.3x and a full one 0.7x** — the opposite of a reader's intuition, and the demand-side price signal on a sale.
Source: `src/Game/Game.Economy/EconomyUtils.cs`, `src/Game/Game.Simulation/ResourceBuyerSystem.cs`.

## Restocking

Sources: `src/Game/Game.Simulation/BuyingCompanySystem.cs`.

```
BuyingCompanySystem (constants: kNotificationCostLimit = 5, kResourceLowStockAmount = 4000, kResourceMinimumRequestAmount = 2000):
  slots = 1, or 2 with a second input; +1 if output differs from input1 AND has weight
  slotCapacity = StorageLimitData.m_Limit / slots
  a need fires when on-hand + buying-truck loads + pending Purpose.Shopping trips falls below max(4000, slotCapacity * 0.25)
  request = min(slotShare - onHand, min(storageLeft, truckCapacity)) where slotShare is limit/3 with a second input, the whole limit for a single-input pass-through or weightless output, else limit/2; abandoned at or below 2000
  a weightless input skips the sizing and requests a flat 2000
  the need is computed for inputs only; the output pass just consumes storageLeft
  result, only for a company renting a property with a Transform: a ResourceBuyer { m_Payer, m_AmountNeeded = min(request, selected truck's capacity), Industrial|Import, m_Location, m_ResourceNeeded } on the company
```

**The "no inputs" notification is priced, not stocked.**
It fires when the input's `TradeCost.m_BuyCost` exceeds `kNotificationCostLimit`, and the icon goes on the property rather than the company — so a company with a full warehouse can show it and one starving on a cheap route cannot.
Source: `src/Game/Game.Simulation/BuyingCompanySystem.cs`.

## Trade costs and outside connections

Sources: `src/Game/Game.Simulation/TradeSystem.cs`, `src/Game/Game.Prefabs/OutsideTradeParameterData.cs`.

A goods outside connection is a `StorageCompany` by declaration: the `OutsideConnection` prefab component's `GetArchetypeComponents` adds `Game.Companies.StorageCompany` beside `Resources`, `TradeCost` and `StorageTransferRequest` (`src/Game/Game.Prefabs/OutsideConnection.cs`).
The electricity and water connection prefabs declare no company at all, so they never enter this system; `TradeSystem` queries `StorageCompany` + `Game.Objects.OutsideConnection`.

```
TradeSystem (kUpdatesPerDay = 128, no UpdateFrame; kRefreshRate = 0.01):
  per resource: m_TradeBalances[i] = round(0.99 * m_TradeBalances[i])   // 1% decay per pass
  buy  = weightCost(type) * weight;  if (balance < 0) buy  *= 1 + distanceCost(type) * max(50, sqrt(-balance))
  sell = weightCost(type) * weight;  if (balance > 0) sell *= 1 + distanceCost(type) * max(50, sqrt(balance))
  CityModifierType.ImportCost / ExportCost applied; cached per resource x transfer type
  per connection (StorageLimitData combined with installed upgrades), per resource the prefab stores -- plus every office resource, stored or not:
    OutgoingMail: stock set to 0, nothing else happens
    target = limit / storedResourceCount, but 0 for an office resource
      (garbage: GarbageFacilityData.m_GarbageCapacity)
    delta = target/2 - current stock; ratio = |delta / (target/2)|
    move  = ratio > 1 ? delta : (int)(delta * ratio / 128) * 8 -- an office resource's zero target makes ratio infinite, so its stock is wiped every pass
    m_TradeBalances[resource] -= move, feeding the sqrt cost above
    each move books StatisticType.Trade and the import/export accumulator, except the three mail resources (the mask 28672)
    the connection's TradeCost is rewritten from the cheapest qualifying transfer type
```

So an outside connection's stock is driven toward half its capacity, the cost of trading rises with the square root of the standing balance in that direction, and the weight and distance coefficients are `OutsideTradeParameterData` fields per `OutsideConnectionTransferType`.

## Exporting, and where the outside world's money comes from

Sources: `src/Game/Game.Simulation/ResourceExporterSystem.cs`, `src/Game/Game.Simulation/DeliveryTruckAISystem.cs`, `src/Game/Game.Simulation/TradeSystem.cs`.

```
ResourceExporterSystem, per ResourceExporter:
  weightless: pick a random outside connection; if it is a StorageCompany and the seller has a TradeCost buffer, credit the seller industrialPrice * amount plus a trade-cost term and add the stock to the connection
  weighted:   pathfind to a storage target; on success the export becomes a Purpose.Exporting TripNeeded and a CurrentTrading entry, and the money arrives on delivery
  either way the stock leaves the seller now -- when the weightless guard fails, the stock is gone and nobody paid
```

**An export credits the seller and debits nobody, so the outside world is a money source by construction.**
`DeliveryTruckAISystem` debits the delivery's target unless it is a `StorageCompany`, and every goods outside connection is one — symmetrically an import debits the buyer and credits nobody, which is the mechanism behind an export-led city getting rich.
Source: `src/Game/Game.Simulation/DeliveryTruckAISystem.cs`, `src/Game/Game.Simulation/TradeSystem.cs`.

## Office sales

Sources: `src/Game/Game.Simulation/OfficeAISystem.cs`, `src/Game/Game.Simulation/ProcessingCompanySystem.cs`.

```
OfficeAISystem (kUpdatesPerDay = 32; kMinStorageAllow = 30000, kMinimumTradeResource = 2000):
  total = the shared counter ProcessingCompanySystem fills with every unit produced by non-commercial companies not renting an office property this tick
  per office company with stock > 30000:
    sold = min(stock, ceil(total / officeCount) * EconomyParameterData.m_OfficeResourceConsumedPerIndustrialUnit)
    credit itself ceil(sold * industrialPrice(output)) directly -- no delivery, no pathfind
    a ResourceExporter is added for the surplus above 2/3 of kMaxVirtualResourceStorage (plus the 2000 minimum)
```

An office company's customer is the industrial sector in aggregate, with no per-company matching; the scaling parameter is `m_OfficeResourceConsumedPerIndustrialUnit`, not the dead field whose tooltip describes this mechanism ([economy-and-companies.md](economy-and-companies.md), Traps).

(VOLATILE: every system, component, field, constant and `Source:` path this file names — their declarations in `Game.Simulation`, `Game.Economy`, `Game.Companies`, `Game.Prefabs`, `Game.Citizens`, `Game.Objects` and `Game.City` under `src/Game/`, at the files the sections cite.)
