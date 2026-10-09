using Game;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace EnvironmentReport
{
    public partial class EnvironmentReportSystem : GameSystemBase
    {
        private GroundPollutionSystem m_GroundPollutionSystem;
        private AirPollutionSystem m_AirPollutionSystem;
        private NoisePollutionSystem m_NoisePollutionSystem;
        private GroundWaterSystem m_GroundWaterSystem;
        private NaturalResourceSystem m_NaturalResourceSystem;
        private WindSimulationSystem m_WindSimulationSystem;

        // Four reports per in-game day, a power of two as the update system requires.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / 4;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_GroundPollutionSystem = World.GetOrCreateSystemManaged<GroundPollutionSystem>();
            m_AirPollutionSystem = World.GetOrCreateSystemManaged<AirPollutionSystem>();
            m_NoisePollutionSystem = World.GetOrCreateSystemManaged<NoisePollutionSystem>();
            m_GroundWaterSystem = World.GetOrCreateSystemManaged<GroundWaterSystem>();
            m_NaturalResourceSystem = World.GetOrCreateSystemManaged<NaturalResourceSystem>();
            m_WindSimulationSystem = World.GetOrCreateSystemManaged<WindSimulationSystem>();
        }

        protected override void OnUpdate()
        {
            // GetMap hands the array back whether or not the owning system's jobs have finished
            // with it, so each handle is completed before a cell is read on the main thread.
            NativeArray<GroundPollution> ground =
                m_GroundPollutionSystem.GetMap(readOnly: true, out JobHandle groundDeps);
            groundDeps.Complete();
            NativeArray<AirPollution> air =
                m_AirPollutionSystem.GetMap(readOnly: true, out JobHandle airDeps);
            airDeps.Complete();
            NativeArray<NoisePollution> noise =
                m_NoisePollutionSystem.GetMap(readOnly: true, out JobHandle noiseDeps);
            noiseDeps.Complete();
            NativeArray<GroundWater> groundWater =
                m_GroundWaterSystem.GetMap(readOnly: true, out JobHandle groundWaterDeps);
            groundWaterDeps.Complete();
            NativeArray<NaturalResourceCell> resources =
                m_NaturalResourceSystem.GetMap(readOnly: true, out JobHandle resourceDeps);
            resourceDeps.Complete();

            long groundTotal = 0;
            for (int i = 0; i < ground.Length; i++)
            {
                groundTotal += ground[i].m_Pollution;
            }

            long airTotal = 0;
            for (int i = 0; i < air.Length; i++)
            {
                airTotal += air[i].m_Pollution;
            }

            long noiseTotal = 0;
            for (int i = 0; i < noise.Length; i++)
            {
                noiseTotal += noise[i].m_Pollution;
            }

            // m_Polluted is an absolute amount of the water a cell currently holds (m_Amount),
            // not a percentage and not a share of its capacity (m_Max), which is how the pumping
            // station reads it too.
            long polluted = 0;
            long amount = 0;
            for (int i = 0; i < groundWater.Length; i++)
            {
                polluted += groundWater[i].m_Polluted;
                amount += groundWater[i].m_Amount;
            }
            float pollutedShare = amount > 0 ? (float)polluted / amount : 0f;

            // Available is base minus used, read field by field: the cell's GetUsedResources()
            // helper repeats oil's usage in its fish slot.
            long fertility = 0;
            long ore = 0;
            long oil = 0;
            long fish = 0;
            for (int i = 0; i < resources.Length; i++)
            {
                NaturalResourceCell cell = resources[i];
                fertility += cell.m_Fertility.m_Base - cell.m_Fertility.m_Used;
                ore += cell.m_Ore.m_Base - cell.m_Ore.m_Used;
                oil += cell.m_Oil.m_Base - cell.m_Oil.m_Used;
                fish += cell.m_Fish.m_Base - cell.m_Fish.m_Used;
            }

            // The prevailing wind is a map property held by the wind simulation; ClimateSystem
            // also exposes a wind property, but nothing in the game ever writes it.
            float2 wind = m_WindSimulationSystem.constantWind;
            float windStrength = math.length(wind);
            float windHeading = math.degrees(math.atan2(wind.x, wind.y));

            Mod.Log.Info(
                $"Environment report: ground pollution {groundTotal}, air {airTotal}, "
                + $"noise {noiseTotal}; "
                + $"groundwater {pollutedShare:P1} polluted; "
                + $"wind heading {windHeading:F0} deg, strength {windStrength:F3}; "
                + $"available fertile land {fertility}, ore {ore}, oil {oil}, fish {fish}.");
        }
    }
}
