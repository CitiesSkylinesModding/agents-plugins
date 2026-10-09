using Colossal.Entities;
using Game.Buildings;
using Game.Prefabs;
using Game.Zones;
using Unity.Entities;

namespace CheapHomes
{
    public static class LowDensity
    {
        /// <summary>
        /// Whether a zone prefab is low-density residential.
        /// No prefab stores a density: the game derives it from the zone's area type and its
        /// properties, so the same derivation is used here rather than the prefab's name.
        /// </summary>
        public static bool IsLowDensityResidential(EntityManager entityManager, Entity zonePrefab)
        {
            if (!entityManager.TryGetComponent(zonePrefab, out ZoneData zoneData)
                || zoneData.m_AreaType != AreaType.Residential
                || !entityManager.TryGetComponent(zonePrefab, out ZonePropertiesData properties))
            {
                return false;
            }

            return PropertyUtils.GetZoneDensity(zoneData, properties) == ZoneDensity.Low;
        }
    }
}
