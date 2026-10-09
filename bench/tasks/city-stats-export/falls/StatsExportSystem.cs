using System;
using System.IO;
using Colossal.PSI.Environment;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.PSI;
using Game.Simulation;
using Unity.Entities;

namespace CityStatsExport
{
    public partial class StatsExportSystem : GameSystemBase
    {
        private const string kNotificationId = "CityStatsExport";

        private CitySystem m_CitySystem;
        private CityConfigurationSystem m_CityConfigurationSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_CityConfigurationSystem = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (mode != GameMode.Game || m_CitySystem.City == Entity.Null)
            {
                return;
            }

            var city = m_CitySystem.City;
            var population = EntityManager.GetComponentData<Population>(city);
            var money = EntityManager.GetComponentData<PlayerMoney>(city).money;
            var cityName = m_CityConfigurationSystem.cityName;
            var path = Path.Combine(
                EnvPath.kUserDataPath, "ModsData", "CityStatsExport", "city-stats.csv");
            var line = string.Join(",",
                DateTime.Now.ToString("s"),
                cityName,
                population.m_Population,
                money,
                population.m_AverageHappiness) + Environment.NewLine;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, line);
            }
            catch (IOException e)
            {
                Mod.Log.Error(e, $"Could not append to {path}");
                return;
            }

            Mod.Log.Info($"Exported {cityName} to {path}");

            NotificationSystem.Push(
                kNotificationId,
                title: $"{cityName} exported",
                text: $"Stats appended to {path}");
        }

        protected override void OnUpdate()
        {
        }
    }
}
