using RockEngine.Core;
using RockEngine.Core.ECS;
using RockEngine.Core.Extensions;
using RockEngine.Core.Physics;
using RockEngine.Core.Rendering;
using RockEngine.Editor.Layers;

namespace RockEngine.Editor
{
    public class EditorContext(EditorStateManager stateManager,
                               PhysicsManager physicsManager,
                               LayerStack layerStack,
                               ImGuiLayer imGuiLayer,
                               ProjectSelectionLayer projectLayer,
                               World world,
                               WorldRenderer worldRenderer) : ApplicationContextBase
    {
        private readonly EditorStateManager _stateManager = stateManager;
        private readonly PhysicsManager _physicsManager = physicsManager;
        private readonly LayerStack _layerStack = layerStack;
        private readonly ImGuiLayer _imGuiLayer = imGuiLayer;
        private readonly ProjectSelectionLayer _projectLayer = projectLayer;
        private readonly World _world = world;
        private readonly WorldRenderer _worldRenderer = worldRenderer;

        /// <inheritdoc/>
        public override async Task InitializeAsync(GraphicsContext graphics, WorldRenderer renderer, World world)
        {
            await _layerStack.PushLayer(_imGuiLayer).ConfigureAwait(false);
            await _layerStack.PushLayer(_projectLayer).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task UpdateAsync()
        {
            _layerStack.Update();
            await _world.Update(_worldRenderer).ConfigureAwait(false);
            await _worldRenderer.UpdateFrameData().ConfigureAwait(false);

            if (_stateManager.State == EditorState.Play)
            {
                _physicsManager.Update(Time.DeltaTime);
            }
        }

        /// <inheritdoc/>
        public override async Task RenderAsync(RenderContext renderContext)
        {
            var batch = renderContext.GraphicsContext.CreateBatch();
            {
                using (batch.BeginSection("Editor UI", renderContext.FrameIndex))
                {
                    _layerStack.RenderImGui(batch);
                }
                using (batch.BeginSection("Layer render", renderContext.FrameIndex))
                {
                    _layerStack.Render(batch);
                }

                batch.Submit();
            }
            await renderContext.WorldRenderer.Render(renderContext).ConfigureAwait(false);
        }
    }
}