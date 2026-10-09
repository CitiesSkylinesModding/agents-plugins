using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Events;
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
            public NativeArray<Entity> m_Burning;

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

        private EntityQuery m_BurningQuery;
        private NativeParallelHashMap<Entity, FireRecord> m_Ledger;

        // A fire burns for a good while, so a check every 512 frames never misses one.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 512;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_BurningQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<OnFire>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Entities belong to the city that was just loaded, so each city gets a fresh ledger.
            m_Ledger = new NativeParallelHashMap<Entity, FireRecord>(256, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (m_Ledger.IsCreated)
            {
                m_Ledger.Dispose();
            }

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (!m_Ledger.IsCreated)
            {
                return;
            }

            NativeArray<Entity> burning = m_BurningQuery.ToEntityArray(Allocator.TempJob);

            JobHandle jobHandle = new UpdateLedgerJob
            {
                m_Burning = burning,
                m_OnFireData = GetComponentLookup<OnFire>(true),
                m_Ledger = m_Ledger,
            }.Schedule(base.Dependency);

            jobHandle.Complete();
            burning.Dispose();

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

            Mod.Log.Info(
                $"Fire ledger: {repeatOffenders} buildings have burned more than once, " +
                $"the worst {worst} times.");
        }
    }
}
