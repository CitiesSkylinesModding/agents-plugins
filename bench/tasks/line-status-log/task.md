# A line status log whose columns disagree with the line panel

## Kind

bug report

## Prompt

I'm making a Cities: Skylines II mod that writes one status line per transit line to the log a few times per in-game day, so I can watch how my network is doing over a long session without clicking through every line. For each line it prints the line number, whether it's a passenger or cargo line, whether it's running normally, day only, night only or switched off, how many vehicles are on it and how many of those are out on the line right now, how many are full, how much is aboard against total capacity (passengers, or cargo for a cargo line), how many people are waiting along the line, and how many vehicles need maintenance. The code is in this workspace.

The output doesn't match what the game shows. Waiting is 0 on every line, every time. Nothing is ever counted as full, even on lines where the line panel shows every bus packed. On my metro and train lines the amount aboard is a fraction of what the line panel shows for the same line, and the total capacity is too small as well. The two lines I set to night only print as switched off. And on cargo lines the maintenance count is nonsense: brand-new cargo trains get counted as needing maintenance.

## Checklist

### A full vehicle is one with no free space, not one flagged `Full`

- Falls in: the code tests `PublicTransportFlags.Full` against a vehicle's state to decide whether it is full.
- Avoids: the code never reads `Full`; it compares what is aboard (the `Passenger` buffer length, or the summed `Resources` amounts) against the capacity from `PublicTransportVehicleData.m_PassengerCapacity` or `CargoTransportVehicleData.m_CargoCapacity`.
- Reference: cs2-modding/references/mechanics/transportation-and-vehicles/stops-and-boarding.md
- Source: src/Game/Game.Simulation/TransportCarAISystem.cs:312

### Waiting passengers are read off the line's waypoint, not the stop

- Falls in: the code resolves the stop from the waypoint through `Connected.m_Connected`, or queries `TransportStop` entities, and reads `WaitingPassengers` on the stop entity.
- Avoids: the code reads `WaitingPassengers` on each `RouteWaypoint.m_Waypoint` entity itself, or on a stop's `ConnectedRoute.m_Waypoint` entries.
- Reference: cs2-modding/references/mechanics/transportation-and-vehicles/transportation-and-vehicles.md
- Source: src/Game/Game.Prefabs/TransportLinePrefab.cs:89

### Load and capacity are summed over the consist

- Falls in: the `Passenger` or `Resources` buffer and the prefab's capacity are read on the `RouteVehicle.m_Vehicle` entity alone, with no `LayoutElement` walk.
- Avoids: the code walks the vehicle's `LayoutElement` buffer, summing each car's `Passenger` or `Resources` and each car's prefab capacity, with the vehicle itself as the fallback when it has no layout; or it calls `TransportUIUtils.GetRouteVehiclesCount`.
- Reference: cs2-modding/references/mechanics/transportation-and-vehicles/lines-and-fleet.md
- Source: src/Game/Game.UI.InGame/TransportUIUtils.cs:101

### Each payload component is tested with its own flags enum

- Falls in: `CargoTransport.m_State` is cast to `PublicTransportFlags` (or `PublicTransport.m_State` to `CargoTransportFlags`) before a flag is tested, or only `PublicTransport` is read so cargo vehicles contribute nothing to the counts.
- Avoids: both `PublicTransport` and `CargoTransport` are read, each tested with its own enum (`PublicTransportFlags.RequiresMaintenance` on one, `CargoTransportFlags.RequiresMaintenance` on the other), the absent component defaulting to zero.
- Reference: cs2-modding/references/mechanics/transportation-and-vehicles/transportation-and-vehicles.md
- Source: src/Game/Game.Vehicles/CargoTransportFlags.cs:12

### A route option is a shifted ordinal, not a bit value

- Falls in: `Route.m_OptionMask` is masked with `(uint)RouteOption.Inactive`, `(uint)RouteOption.Night` or another member cast directly, treating the enum value as the bit.
- Avoids: each option is read through `RouteUtils.CheckOption(route, option)`, or as `m_OptionMask & (1u << (int)option)`.
- Reference: cs2-modding/references/mechanics/transportation-and-vehicles/lines-and-fleet.md
- Source: src/Game/Game.Routes/RouteUtils.cs:543

## Roots

- decompile
