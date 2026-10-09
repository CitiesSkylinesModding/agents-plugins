using Colossal.Entities;
using Game.Buildings;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BuildingRescue
{
    public partial class BuildingRescueToolSystem : ToolBaseSystem
    {
        private static readonly Color kOutline = new Color(0.3f, 0.9f, 0.4f, 1f);
        private static readonly Color kFill = new Color(0.3f, 0.9f, 0.4f, 0.15f);

        private OverlayRenderSystem m_OverlayRenderSystem;
        private OverlayRenderSystem.Buffer m_OverlayBuffer;
        private ProxyAction m_ToggleAction;
        private ToolBaseSystem m_PreviousTool;

        public override string toolID => "Building Rescue Tool";

        public Entity hoveredBuilding { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Grab the overlay buffer once; the lists behind it are persistent.
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_OverlayBuffer = m_OverlayRenderSystem.GetBuffer(out _);

            m_ToggleAction = Mod.Settings.GetAction(nameof(BuildingRescueSettings.ToggleTool));
            m_ToggleAction.shouldBeEnabled = true;
            m_ToggleAction.onInteraction += (action, phase) =>
            {
                if (phase == InputActionPhase.Performed)
                {
                    Toggle();
                }
            };
        }

        protected override void OnStopRunning()
        {
            hoveredBuilding = Entity.Null;
            base.OnStopRunning();
        }

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects;
        }

        public void Toggle()
        {
            if (m_ToolSystem.activeTool == this)
            {
                Restore();
            }
            else
            {
                // Remember what the player had open so cancelling can hand it back.
                m_PreviousTool = m_ToolSystem.activeTool;
                m_ToolSystem.activeTool = this;
            }
        }

        private void Restore()
        {
            m_ToolSystem.activeTool = m_PreviousTool ?? m_DefaultToolSystem;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (cancelAction.WasPressedThisFrame())
            {
                Restore();
                return inputDeps;
            }

            hoveredBuilding = Entity.Null;

            if (!GetRaycastResult(out Entity entity, out RaycastHit hit)
                || !EntityManager.HasComponent<Building>(entity)
                || !EntityManager.HasComponent<BuildingCondition>(entity))
            {
                return inputDeps;
            }

            hoveredBuilding = entity;

            if (applyAction.WasPressedThisFrame())
            {
                EntityManager.SetComponentData(entity, new BuildingCondition { m_Condition = 0 });
            }

            var position = EntityManager
                .GetComponentData<Game.Objects.Transform>(entity)
                .m_Position;

            m_OverlayBuffer.DrawCircle(
                kOutline,
                kFill,
                1f,
                OverlayRenderSystem.StyleFlags.Projected,
                new float2(0f, 1f),
                position,
                GetDiameter(entity));

            return inputDeps;
        }

        private float GetDiameter(Entity entity)
        {
            if (EntityManager.TryGetComponent(entity, out PrefabRef prefabRef)
                && EntityManager.TryGetComponent(
                    prefabRef.m_Prefab, out ObjectGeometryData geometry))
            {
                return math.max(geometry.m_Size.x, geometry.m_Size.z) + 4f;
            }

            return 16f;
        }
    }
}
