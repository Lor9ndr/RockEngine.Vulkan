using RockEngine.Core;
using RockEngine.Core.DI;
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
            await _layerStack.PushLayer(IoC.Container.GetInstance<EditorLayer>()).ConfigureAwait(false);
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
            using (renderContext.GraphicsBatch.BeginSection("Editor UI", renderContext.FrameIndex))
            {
                _layerStack.RenderImGui(renderContext.GraphicsBatch);
            }
            using (renderContext.GraphicsBatch.BeginSection("Layer render", renderContext.FrameIndex))
            {
                _layerStack.Render(renderContext.GraphicsBatch);
            }
            await renderContext.WorldRenderer.Render(renderContext).ConfigureAwait(false);
        }
    }
}