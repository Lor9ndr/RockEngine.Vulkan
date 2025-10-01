using RockEngine.Core.Builders;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public abstract class PassStrategyBase:IRenderPassStrategy
    {
        protected readonly IRenderSubPass[] _subPasses;
        protected readonly VulkanContext _context;

        /// <summary>
        /// Builded renderpass
        /// </summary>
        protected RckRenderPass _renderPass;

        public abstract int Order { get; }

        public IReadOnlyCollection<IRenderSubPass> SubPasses => _subPasses;
        protected PassStrategyBase(VulkanContext context, IEnumerable<IRenderSubPass> subPasses)
        {
            _context = context;
            _subPasses = subPasses.ToArray();
        }

        public RckRenderPass BuildRenderPass()
        {
            if (_renderPass is not null)
            {
                return _renderPass;
            }
            var builder = new RenderPassBuilder(_context);

            // Collect all attachment descriptions
            foreach (var pass in _subPasses)
            {
                pass.SetupAttachmentDescriptions(builder);
            }

            // Configure subpasses
            for (uint i = 0; i < _subPasses.Length; i++)
            {
                var subpassBuilder = builder.BeginSubpass();
                _subPasses[i].SetupSubpassDescription(subpassBuilder);
                subpassBuilder.EndSubpass();
            }

            // Configure dependencies
            for (uint i = 0; i < _subPasses.Length; i++)
            {
                _subPasses[i].SetupDependencies(builder, i);
            }
           
            _renderPass = new RckRenderPass(builder.Build(),  _subPasses);
            return _renderPass;
        }

        public virtual void Dispose()
        {
        }

        public abstract Task Execute(SubmitContext submitContext, CameraManager cameraManager, Renderer renderer);

        public void InitializeSubPasses()
        {
            foreach (var item in SubPasses)
            {
                item.Initilize();
            }
        }

        public virtual ValueTask Update()
        {
            return ValueTask.CompletedTask;
        }
    }
}