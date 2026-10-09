using Game;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
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
        private ClimateSystem m_ClimateSystem;

        // Four reports per in-game day.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / 4;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_GroundPollutionSystem = World.GetOrCreateSystemManaged<GroundPollutionSystem>();
            m_AirPollutionSystem = World.GetOrCreateSystemManaged<AirPollutionSystem>();
            m_NoisePollutionSystem = World.GetOrCreateSystemManaged<NoisePollutionSystem>();
            m_GroundWaterSystem = World.GetOrCreateSystemManaged<GroundWaterSystem>();
            m_NaturalResourceSystem = World.GetOrCreateSystemManaged<NaturalResourceSystem>();
            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();
        }

        protected override void OnUpdate()
        {
            NativeArray<GroundPollution> ground =
                m_GroundPollutionSystem.GetMap(readOnly: true, out _);
            NativeArray<AirPollution> air =
                m_AirPollutionSystem.GetMap(readOnly: true, out _);
            NativeArray<NoisePollution> noise =
                m_NoisePollutionSystem.GetMap(readOnly: true, out _);
            NativeArray<GroundWater> groundWater =
                m_GroundWaterSystem.GetMap(readOnly: true, out _);
            NativeArray<NaturalResourceCell> resources =
                m_NaturalResourceSystem.GetMap(readOnly: true, out _);

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

            // Polluted groundwater as a share of the aquifer's capacity.
            long polluted = 0;
            long capacity = 0;
            for (int i = 0; i < groundWater.Length; i++)
            {
                polluted += groundWater[i].m_Polluted;
                capacity += groundWater[i].m_Max;
            }
            float pollutedShare = capacity > 0 ? (float)polluted / capacity : 0f;

            // Fertility, ore, oil, fish: what the map holds minus what has been used.
            float4 available = float4.zero;
            for (int i = 0; i < resources.Length; i++)
            {
                available += resources[i].GetBaseResources() - resources[i].GetUsedResources();
            }

            float2 wind = m_ClimateSystem.wind;
            float windStrength = math.length(wind);
            float windHeading = math.degrees(math.atan2(wind.x, wind.y));

            Mod.Log.Info(
                $"Environment report: ground pollution {groundTotal}, air {airTotal}, "
                + $"noise {noiseTotal}; "
                + $"groundwater {pollutedShare:P1} polluted; "
                + $"wind heading {windHeading:F0} deg, strength {windStrength:F3}; "
                + $"available fertile land {available.x:F0}, ore {available.y:F0}, "
                + $"oil {available.z:F0}, fish {available.w:F0}.");
        }
    }
}
