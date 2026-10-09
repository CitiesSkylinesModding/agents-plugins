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
        private struct DrawHoverJob : IJob
        {
            public OverlayRenderSystem.Buffer m_Buffer;
            public float3 m_Position;
            public float m_Diameter;

            public void Execute()
            {
                m_Buffer.DrawCircle(
                    new Color(0.3f, 0.9f, 0.4f, 1f),
                    new Color(0.3f, 0.9f, 0.4f, 0.15f),
                    1f,
                    OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f),
                    m_Position,
                    m_Diameter);
            }
        }

        private OverlayRenderSystem m_OverlayRenderSystem;
        private ProxyAction m_ToggleAction;
        private ToolBaseSystem m_PreviousTool;

        public override string toolID => "Building Rescue Tool";

        public Entity hoveredBuilding { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();

            // Every route that switches the active tool fires this, so the tool handed back on
            // cancel is right even when the switch came from the toolbar or another mod.
            // EventToolChanged is a plain delegate field: a bare assignment would wipe every
            // other subscriber.
            m_PreviousTool = m_DefaultToolSystem;
            m_ToolSystem.EventToolChanged += tool =>
            {
                if (tool != this)
                {
                    m_PreviousTool = tool;
                }
            };

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

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // The base enables nothing for a tool outside the game assembly (its UpdateActions
            // is empty and private protected), and every read on these actions gates on
            // shouldBeEnabled; OnStopRunning's base switches them back off.
            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
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

            // The buffer's handle is every writer already registered this frame; draw after it and
            // register back, and the seal publishes the returned handle as Dependency.
            var buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle bufferDeps);
            var job = new DrawHoverJob
            {
                m_Buffer = buffer,
                m_Position = EntityManager
                    .GetComponentData<Game.Objects.Transform>(entity)
                    .m_Position,
                m_Diameter = GetDiameter(entity),
            };

            var handle = job.Schedule(JobHandle.CombineDependencies(inputDeps, bufferDeps));
            m_OverlayRenderSystem.AddBufferWriter(handle);

            return handle;
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
