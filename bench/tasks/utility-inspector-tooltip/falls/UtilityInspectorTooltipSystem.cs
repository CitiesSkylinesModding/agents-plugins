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

            // The consumer components store kilowatts and cubic metres, so the unit goes in the
            // label and the number is shown as a plain integer.
            m_Electricity = new IntTooltip
            {
                path = "utilityInspectorElectricity",
                label = LocalizedString.Value("Electricity demand (kW)"),
                unit = Unit.kInteger
            };
            m_Water = new IntTooltip
            {
                path = "utilityInspectorWater",
                label = LocalizedString.Value("Water use (m³)"),
                unit = Unit.kInteger
            };
            m_Temperature = new FloatTooltip
            {
                path = "utilityInspectorTemperature",
                label = LocalizedString.Value("Outdoor temperature (°C)"),
                unit = Unit.kFloatSingleFraction
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

            float temperature = m_ClimateSystem.temperature;
            m_Temperature.value = temperature;
            AddMouseTooltip(m_Temperature);

            if (m_ElectricityParameterQuery.TryGetSingleton(out ElectricityParameterData parameters))
            {
                // The curve returns a multiplier on the base demand; 1.12 means 12 % more.
                float multiplier = parameters.m_TemperatureConsumptionMultiplier.Evaluate(temperature);
                m_TemperatureEffect.value = multiplier - 1f;
                AddMouseTooltip(m_TemperatureEffect);
            }
        }
    }
}
