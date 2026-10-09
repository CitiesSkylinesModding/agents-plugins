using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace FireLedger
{
    /// <summary>
    /// What the ledger remembers about one building: how many fires it has had, and whether the
    /// last one is still burning, so a fire lasting several checks is counted once.
    /// </summary>
    public struct FireRecord
    {
        public int m_Fires;
        public bool m_Burning;
    }

    public partial class FireLedgerSystem : GameSystemBase
    {
        [BurstCompile]
        private struct UpdateLedgerJob : IJob
        {
            [ReadOnly]
            public NativeList<Entity> m_Burning;

            [ReadOnly]
            public ComponentLookup<OnFire> m_OnFireData;

            public NativeParallelHashMap<Entity, FireRecord> m_Ledger;

            public void Execute()
            {
                for (int i = 0; i < m_Burning.Length; i++)
                {
                    Entity building = m_Burning[i];

                    // Still the same fire as last check: already counted.
                    if (m_Ledger.TryGetValue(building, out FireRecord record) && record.m_Burning)
                    {
                        continue;
                    }

                    record.m_Fires++;
                    record.m_Burning = true;
                    m_Ledger[building] = record;
                }

                // A building whose fire went out can be counted again next time it catches fire.
                NativeArray<Entity> buildings = m_Ledger.GetKeyArray(Allocator.Temp);

                for (int i = 0; i < buildings.Length; i++)
                {
                    FireRecord record = m_Ledger[buildings[i]];

                    if (record.m_Burning && !m_OnFireData.HasComponent(buildings[i]))
                    {
                        record.m_Burning = false;
                        m_Ledger[buildings[i]] = record;
                    }
                }
            }
        }

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_BurningQuery;
        private ComponentLookup<OnFire> m_OnFireData;
        private NativeParallelHashMap<Entity, FireRecord> m_Ledger;

        // The ledger is the mod's own container, so the world's dependency graph knows nothing of
        // it: this handle is what orders each update's job after the previous one's.
        private JobHandle m_LedgerDependency;

        private uint m_LastReportFrame;
        private int m_LastRepeatOffenders = -1;
        private int m_LastWorst = -1;

        // A fire burns for a good while, so a check every 512 frames never misses one.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 512;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_BurningQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<OnFire>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
            m_OnFireData = GetComponentLookup<OnFire>(true);

            // One world and one instance of this system span the whole session, every city the
            // player opens included, so the ledger is allocated here once and emptied per city.
            m_Ledger = new NativeParallelHashMap<Entity, FireRecord>(256, Allocator.Persistent);
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            // Entities belong to the city about to load, so the previous city's entries go, once
            // the last job that touched them is done.
            m_LedgerDependency.Complete();
            m_Ledger.Clear();
            m_LastReportFrame = 0;
            m_LastRepeatOffenders = -1;
            m_LastWorst = -1;
        }

        protected override void OnDestroy()
        {
            m_Ledger.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            ReportIfDue();

            m_OnFireData.Update(this);

            NativeList<Entity> burning = m_BurningQuery.ToEntityListAsync(
                World.UpdateAllocator.ToAllocator, out JobHandle gatherHandle);

            JobHandle dependencies = JobHandle.CombineDependencies(
                base.Dependency, gatherHandle, m_LedgerDependency);

            JobHandle jobHandle = new UpdateLedgerJob
            {
                m_Burning = burning,
                m_OnFireData = m_OnFireData,
                m_Ledger = m_Ledger,
            }.Schedule(dependencies);

            m_LedgerDependency = jobHandle;
            base.Dependency = jobHandle;
        }

        /// <summary>
        /// Once per in-game day, reads the ledger back and logs it if the numbers moved.
        /// Runs before this update's job is scheduled: the previous update's job finished long
        /// ago, so completing its handle waits on nothing.
        /// </summary>
        private void ReportIfDue()
        {
            uint frame = m_SimulationSystem.frameIndex;

            if (frame - m_LastReportFrame < TimeSystem.kTicksPerDay)
            {
                return;
            }

            m_LastReportFrame = frame;
            m_LedgerDependency.Complete();

            int repeatOffenders = 0;
            int worst = 0;
            NativeArray<FireRecord> records = m_Ledger.GetValueArray(Allocator.Temp);

            for (int i = 0; i < records.Length; i++)
            {
                if (records[i].m_Fires > 1)
                {
                    repeatOffenders++;
                }

                if (records[i].m_Fires > worst)
                {
                    worst = records[i].m_Fires;
                }
            }

            records.Dispose();

            if (repeatOffenders == m_LastRepeatOffenders && worst == m_LastWorst)
            {
                return;
            }

            m_LastRepeatOffenders = repeatOffenders;
            m_LastWorst = worst;

            Mod.Log.InfoFormat(
                "Fire ledger: {0} buildings have burned more than once, the worst {1} times.",
                repeatOffenders,
                worst);
        }
    }
}
