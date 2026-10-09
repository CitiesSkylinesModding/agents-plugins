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

        // The option mask holds the line's policy toggles: inactive, day only, night only.
        private static string DescribeSchedule(Route route)
        {
            if ((route.m_OptionMask & (uint)RouteOption.Inactive) != 0)
            {
                return "switched off";
            }

            if ((route.m_OptionMask & (uint)RouteOption.Day) != 0)
            {
                return "day only";
            }

            if ((route.m_OptionMask & (uint)RouteOption.Night) != 0)
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

                // Passenger and cargo vehicles carry the same state flags under two component
                // names, so both are read as one flag set.
                var state = (PublicTransportFlags)0;

                if (EntityManager.TryGetComponent(
                    vehicle, out Game.Vehicles.PublicTransport publicTransport))
                {
                    state = publicTransport.m_State;
                }
                else if (EntityManager.TryGetComponent(
                    vehicle, out Game.Vehicles.CargoTransport cargoTransport))
                {
                    state = (PublicTransportFlags)cargoTransport.m_State;
                }

                if ((state & PublicTransportFlags.EnRoute) != 0)
                {
                    enRoute++;
                }

                if ((state & PublicTransportFlags.Full) != 0)
                {
                    full++;
                }

                if ((state & PublicTransportFlags.RequiresMaintenance) != 0)
                {
                    maintenance++;
                }

                var vehiclePrefab = EntityManager.GetComponentData<PrefabRef>(vehicle).m_Prefab;

                if (EntityManager.TryGetComponent(
                    vehiclePrefab, out PublicTransportVehicleData passengerData))
                {
                    capacity += passengerData.m_PassengerCapacity;

                    if (EntityManager.TryGetBuffer(
                        vehicle, true, out DynamicBuffer<Passenger> passengers))
                    {
                        load += passengers.Length;
                    }
                }
                else if (EntityManager.TryGetComponent(
                    vehiclePrefab, out CargoTransportVehicleData cargoData))
                {
                    capacity += cargoData.m_CargoCapacity;

                    if (EntityManager.TryGetBuffer(
                        vehicle, true, out DynamicBuffer<Resources> resources))
                    {
                        foreach (var resource in resources)
                        {
                            load += resource.m_Amount;
                        }
                    }
                }
            }
        }

        // Each waypoint of the line links to the stop it serves; the queue is read off the stop.
        private int CountWaiting(Entity line)
        {
            var waiting = 0;
            var waypoints = EntityManager.GetBuffer<RouteWaypoint>(line, true);

            foreach (var entry in waypoints)
            {
                if (EntityManager.TryGetComponent(entry.m_Waypoint, out Connected connected)
                    && EntityManager.TryGetComponent(
                        connected.m_Connected, out WaitingPassengers queue))
                {
                    waiting += queue.m_Count;
                }
            }

            return waiting;
        }
    }
}
