using Colossal.Entities;
using Game;
using Game.Common;
using Game.Economy;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;

namespace LineStatusLog
{
    /// <summary>
    /// Writes one status line per transit line to the log, a few times per in-game day.
    /// </summary>
    public partial class LineStatusLogSystem : GameSystemBase
    {
        private EntityQuery m_LineQuery;

        // Sixteen reports a day, a power of two as the update system requires.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / 16;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_LineQuery = GetEntityQuery(
                ComponentType.ReadOnly<Route>(),
                ComponentType.ReadOnly<TransportLine>(),
                ComponentType.ReadOnly<RouteNumber>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            var lines = m_LineQuery.ToEntityArray(Allocator.Temp);

            foreach (var line in lines)
            {
                var route = EntityManager.GetComponentData<Route>(line);
                var number = EntityManager.GetComponentData<RouteNumber>(line).m_Number;
                var linePrefab = EntityManager.GetComponentData<PrefabRef>(line).m_Prefab;
                var lineData = EntityManager.GetComponentData<TransportLineData>(linePrefab);
                var kind = lineData.m_CargoTransport ? "cargo" : "passenger";

                CountVehicles(line, out var total, out var enRoute, out var full,
                    out var maintenance, out var load, out var capacity);

                Mod.Log.Info(
                    $"Line {number} ({kind}, {DescribeSchedule(route)}): {total} vehicles, "
                    + $"{enRoute} en route, {full} full, {load}/{capacity} aboard, "
                    + $"{CountWaiting(line)} waiting, {maintenance} need maintenance.");
            }

            lines.Dispose();
        }

        // RouteOption is an ordinal, not a bit: the mask is read through RouteUtils.CheckOption,
        // which shifts by the option's value.
        private static string DescribeSchedule(Route route)
        {
            if (RouteUtils.CheckOption(route, RouteOption.Inactive))
            {
                return "switched off";
            }

            if (RouteUtils.CheckOption(route, RouteOption.Day))
            {
                return "day only";
            }

            if (RouteUtils.CheckOption(route, RouteOption.Night))
            {
                return "night only";
            }

            return "running";
        }

        private void CountVehicles(Entity line, out int total, out int enRoute, out int full,
            out int maintenance, out int load, out int capacity)
        {
            var vehicles = EntityManager.GetBuffer<RouteVehicle>(line, true);
            total = vehicles.Length;
            enRoute = 0;
            full = 0;
            maintenance = 0;
            load = 0;
            capacity = 0;

            foreach (var entry in vehicles)
            {
                var vehicle = entry.m_Vehicle;

                // A passenger vehicle carries PublicTransport and a cargo one CargoTransport; the
                // two flag enums share names but not bit values past the first four, so each is
                // tested with its own enum and the absent component defaults to zero, the shape
                // TransportLineSystem.CheckVehicles uses.
                EntityManager.TryGetComponent(
                    vehicle, out Game.Vehicles.PublicTransport publicTransport);
                EntityManager.TryGetComponent(
                    vehicle, out Game.Vehicles.CargoTransport cargoTransport);

                if ((publicTransport.m_State & PublicTransportFlags.EnRoute) != 0
                    || (cargoTransport.m_State & CargoTransportFlags.EnRoute) != 0)
                {
                    enRoute++;
                }

                if ((publicTransport.m_State & PublicTransportFlags.RequiresMaintenance) != 0
                    || (cargoTransport.m_State & CargoTransportFlags.RequiresMaintenance) != 0)
                {
                    maintenance++;
                }

                MeasureLoad(vehicle, out var vehicleLoad, out var vehicleCapacity);
                load += vehicleLoad;
                capacity += vehicleCapacity;

                // PublicTransportFlags.Full is only ever set on evacuation and prisoner transport,
                // so a full line vehicle is one with no free space left.
                if (vehicleCapacity > 0 && vehicleLoad >= vehicleCapacity)
                {
                    full++;
                }
            }
        }

        // A train, tram or subway is a consist: the route entry is the controller, and each car
        // in its LayoutElement buffer holds its own passengers and its own capacity. The same walk
        // the line panel does in TransportUIUtils.
        private void MeasureLoad(Entity vehicle, out int load, out int capacity)
        {
            load = 0;
            capacity = 0;

            if (EntityManager.TryGetBuffer(vehicle, true, out DynamicBuffer<LayoutElement> layout))
            {
                foreach (var car in layout)
                {
                    MeasureUnitLoad(car.m_Vehicle, ref load, ref capacity);
                }
            }
            else
            {
                MeasureUnitLoad(vehicle, ref load, ref capacity);
            }
        }

        private void MeasureUnitLoad(Entity unit, ref int load, ref int capacity)
        {
            var unitPrefab = EntityManager.GetComponentData<PrefabRef>(unit).m_Prefab;

            if (EntityManager.TryGetComponent(
                unitPrefab, out PublicTransportVehicleData passengerData))
            {
                capacity += passengerData.m_PassengerCapacity;

                if (EntityManager.TryGetBuffer(unit, true, out DynamicBuffer<Passenger> passengers))
                {
                    load += passengers.Length;
                }
            }
            else if (EntityManager.TryGetComponent(
                unitPrefab, out CargoTransportVehicleData cargoData))
            {
                capacity += cargoData.m_CargoCapacity;

                if (EntityManager.TryGetBuffer(unit, true, out DynamicBuffer<Resources> resources))
                {
                    foreach (var resource in resources)
                    {
                        load += resource.m_Amount;
                    }
                }
            }
        }

        // The queue sits on the line's own waypoint, not on the stop it links to: two lines
        // sharing a stop have two queues, one per waypoint.
        private int CountWaiting(Entity line)
        {
            var waiting = 0;
            var waypoints = EntityManager.GetBuffer<RouteWaypoint>(line, true);

            foreach (var entry in waypoints)
            {
                if (EntityManager.TryGetComponent(entry.m_Waypoint, out WaitingPassengers queue))
                {
                    waiting += queue.m_Count;
                }
            }

            return waiting;
        }
    }
}
