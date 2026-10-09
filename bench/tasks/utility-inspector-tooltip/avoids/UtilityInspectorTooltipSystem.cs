using Colossal.Entities;
using Game.Buildings;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Unity.Entities;

namespace UtilityInspector
{
    public partial class UtilityInspectorTooltipSystem : TooltipSystemBase
    {
        private ToolSystem m_ToolSystem;
        private DefaultToolSystem m_DefaultTool;
        private ToolRaycastSystem m_ToolRaycastSystem;
        private ClimateSystem m_ClimateSystem;
        private EntityQuery m_ElectricityParameterQuery;

        private IntTooltip m_Electricity;
        private IntTooltip m_Water;
        private FloatTooltip m_Temperature;
        private FloatTooltip m_TemperatureEffect;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_ToolRaycastSystem = World.GetOrCreateSystemManaged<ToolRaycastSystem>();
            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_ElectricityParameterQuery = GetEntityQuery(
                ComponentType.ReadOnly<ElectricityParameterData>());

            // Each tooltip names the unit its value is denominated in and carries the raw
            // simulation value. The frontend's formatter scales it, picks kW or MW, converts to
            // the player's temperature and unit-system preferences and applies the locale's
            // separators, so this system never reads a preference or converts anything itself.
            m_Electricity = new IntTooltip
            {
                path = "utilityInspectorElectricity",
                label = LocalizedString.Value("Electricity demand"),
                unit = Unit.kPower
            };
            m_Water = new IntTooltip
            {
                path = "utilityInspectorWater",
                label = LocalizedString.Value("Water use"),
                unit = Unit.kVolume
            };
            m_Temperature = new FloatTooltip
            {
                path = "utilityInspectorTemperature",
                label = LocalizedString.Value("Outdoor temperature"),
                unit = Unit.kTemperature
            };
            m_TemperatureEffect = new FloatTooltip
            {
                path = "utilityInspectorTemperatureEffect",
                label = LocalizedString.Value("Temperature effect on demand"),
                unit = Unit.kPercentage,
                signed = true
            };
        }

        protected override void OnUpdate()
        {
            if (m_ToolSystem.activeTool != m_DefaultTool
                || !m_ToolRaycastSystem.GetRaycastResult(out var result)
                || !EntityManager.HasComponent<Building>(result.m_Owner))
            {
                return;
            }

            if (EntityManager.TryGetComponent(result.m_Owner, out ElectricityConsumer electricity))
            {
                m_Electricity.value = electricity.m_WantedConsumption;
                AddMouseTooltip(m_Electricity);
            }

            if (EntityManager.TryGetComponent(result.m_Owner, out WaterConsumer water))
            {
                m_Water.value = water.m_WantedConsumption;
                AddMouseTooltip(m_Water);
            }

            // Celsius, as the climate system holds it; the temperature unit does the rest.
            float temperature = m_ClimateSystem.temperature;
            m_Temperature.value = temperature;
            AddMouseTooltip(m_Temperature);

            if (m_ElectricityParameterQuery.TryGetSingleton(out ElectricityParameterData parameters))
            {
                // The percentage unit takes percent points, as the game's own usage tooltips
                // pass them, so the multiplier's offset is scaled by 100 here.
                float multiplier = parameters.m_TemperatureConsumptionMultiplier.Evaluate(temperature);
                m_TemperatureEffect.value = 100f * (multiplier - 1f);
                AddMouseTooltip(m_TemperatureEffect);
            }
        }
    }
}
