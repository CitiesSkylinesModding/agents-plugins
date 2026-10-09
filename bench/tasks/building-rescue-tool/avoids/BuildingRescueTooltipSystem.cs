using Game.Buildings;
using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Unity.Entities;

namespace BuildingRescue
{
    public partial class BuildingRescueTooltipSystem : TooltipSystemBase
    {
        private ToolSystem m_ToolSystem;
        private BuildingRescueToolSystem m_RescueTool;
        private IntTooltip m_Tooltip;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_RescueTool = World.GetOrCreateSystemManaged<BuildingRescueToolSystem>();
            m_Tooltip = new IntTooltip
            {
                path = "buildingRescueCondition",
                label = LocalizedString.Value("Condition"),
                signed = true,
            };
        }

        protected override void OnUpdate()
        {
            if (m_ToolSystem.activeTool != m_RescueTool
                || m_RescueTool.hoveredBuilding == Entity.Null)
            {
                return;
            }

            var condition = EntityManager
                .GetComponentData<BuildingCondition>(m_RescueTool.hoveredBuilding)
                .m_Condition;

            m_Tooltip.value = condition;
            m_Tooltip.color = condition < 0 ? TooltipColor.Warning : TooltipColor.Info;
            AddMouseTooltip(m_Tooltip);
        }
    }
}
