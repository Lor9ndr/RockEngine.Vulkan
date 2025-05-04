using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.Builders;

public class RenderPassBuilder : DisposableBuilder
{
    private readonly VulkanContext _context;
    private readonly List<AttachmentDescription> _attachments = new();
    private readonly List<VkSubpassDescription> _subpasses = new();
    private readonly List<SubpassDependency> _dependencies = new();

    public RenderPassBuilder(VulkanContext context) => _context = context;

    public AttachmentConfigurer ConfigureAttachment(Format format, SampleCountFlags samples = SampleCountFlags.Count1Bit)
        => new AttachmentConfigurer(this, format, samples);

    public SubpassConfigurer BeginSubpass() => new SubpassConfigurer(this, _subpasses.Count);
    public DependencyConfigurer AddDependency() => new DependencyConfigurer(this);

    public unsafe VkRenderPass Build()
    {
        var nativeSubpasses = new SubpassDescription[_subpasses.Count];

        for (int i = 0; i < _subpasses.Count; i++)
        {
            nativeSubpasses[i] = _subpasses[i].ToNativeSubpass();
        }

        fixed (AttachmentDescription* pAttachments = _attachments.ToArray())
        fixed (SubpassDescription* pSubpasses = nativeSubpasses.ToArray())
        fixed (SubpassDependency* pDependencies = _dependencies.ToArray())
        {
            var createInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint)_attachments.Count,
                PAttachments = pAttachments,
                SubpassCount = (uint)nativeSubpasses.Length,
                PSubpasses = pSubpasses,
                DependencyCount = (uint)_dependencies.Count,
                PDependencies = pDependencies
            };

            return VkRenderPass.Create(_context, in createInfo);
        }

    }

    private unsafe SubpassDescription ConvertToNativeSubpass(VkSubpassDescription desc)
    {
        fixed (AttachmentReference* pColor = desc.ColorAttachments.ToArray())
        fixed (AttachmentReference* pInput = desc.InputAttachments.ToArray())
        fixed (AttachmentReference* pResolve = desc.ResolveAttachments.ToArray())
        fixed (AttachmentReference* pDepth = desc.DepthStencilAttachment.ToArray())
        {
            return new SubpassDescription
            {
                PipelineBindPoint = desc.PipelineBindPoint,
                ColorAttachmentCount = (uint)desc.ColorAttachments.Count,
                PColorAttachments = pColor,
                InputAttachmentCount = (uint)desc.InputAttachments.Count,
                PInputAttachments = pInput,
                PResolveAttachments = desc.ResolveAttachments?.Count > 0 ? pResolve : null,
                PDepthStencilAttachment = desc.DepthStencilAttachment?.Count() > 0 ? pDepth : null
            };
        }
    }

    public class AttachmentConfigurer
    {
        private readonly RenderPassBuilder _parent;
        private AttachmentDescription _desc;

        internal AttachmentConfigurer(RenderPassBuilder parent, Format format, SampleCountFlags samples)
        {
            _parent = parent;
            _desc = new AttachmentDescription
            {
                Format = format,
                Samples = samples,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare
            };
        }

        public AttachmentConfigurer WithColorOperations(
            AttachmentLoadOp load = AttachmentLoadOp.Clear,
            AttachmentStoreOp store = AttachmentStoreOp.Store,
            ImageLayout initialLayout = ImageLayout.Undefined,
            ImageLayout finalLayout = ImageLayout.ColorAttachmentOptimal)
        {
            _desc.LoadOp = load;
            _desc.StoreOp = store;
            _desc.InitialLayout = initialLayout;
            _desc.FinalLayout = finalLayout;
            return this;
        }

        public AttachmentConfigurer WithDepthOperations(
            AttachmentLoadOp load = AttachmentLoadOp.Clear,
            AttachmentStoreOp store = AttachmentStoreOp.Store,
            ImageLayout initialLayout = ImageLayout.Undefined,
            ImageLayout finalLayout = ImageLayout.DepthStencilAttachmentOptimal)
        {
            _desc.LoadOp = load;
            _desc.StoreOp = store;
            _desc.InitialLayout = initialLayout;
            _desc.FinalLayout = finalLayout;
            return this;
        }

        public RenderPassBuilder Add()
        {
            _parent._attachments.Add(_desc);
            return _parent;
        }
    }

    public class SubpassConfigurer
    {
        private readonly RenderPassBuilder _parent;
        private readonly VkSubpassDescription _desc;

        internal SubpassConfigurer(RenderPassBuilder parent, int index)
        {
            _parent = parent;
            _desc = new VkSubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics
            };
        }

        public SubpassConfigurer AddColorAttachment(int index, ImageLayout layout = ImageLayout.ColorAttachmentOptimal)
        {
            _desc.ColorAttachments.Add(new AttachmentReference
            {
                Attachment = (uint)index,
                Layout = layout
            });
            return this;
        }

        public SubpassConfigurer SetDepthAttachment(int index, ImageLayout layout = ImageLayout.DepthStencilAttachmentOptimal)
        {
            _desc.DepthStencilAttachment = [new AttachmentReference
        {
            Attachment = (uint)index,
            Layout = layout
        }];
            return this;
        }

        public SubpassConfigurer AddInputAttachment(int index, ImageLayout layout = ImageLayout.ShaderReadOnlyOptimal)
        {
            _desc.InputAttachments.Add(new AttachmentReference
            {
                Attachment = (uint)index,
                Layout = layout
            });
            return this;
        }

        public SubpassConfigurer AddResolveAttachment(int index, ImageLayout layout = ImageLayout.ColorAttachmentOptimal)
        {
            _desc.ResolveAttachments.Add(new AttachmentReference
            {
                Attachment = (uint)index,
                Layout = layout
            });
            return this;
        }

        public RenderPassBuilder EndSubpass()
        {
            _parent._subpasses.Add(_desc);
            return _parent;
        }
    }

    public class DependencyConfigurer
    {
        private readonly RenderPassBuilder _parent;
        private SubpassDependency _dependency = new();

        internal DependencyConfigurer(RenderPassBuilder parent)
        {
            _parent = parent;
            _dependency.DependencyFlags = DependencyFlags.ByRegionBit;
        }

        public DependencyConfigurer FromExternal()
        {
            _dependency.SrcSubpass = Vk.SubpassExternal;
            return this;
        }

        public DependencyConfigurer FromSubpass(uint subpass)
        {
            _dependency.SrcSubpass = subpass;
            return this;
        }

        public DependencyConfigurer ToSubpass(uint subpass)
        {
            _dependency.DstSubpass = subpass;
            return this;
        }
        public DependencyConfigurer ToExtenral()
        {
            _dependency.DstSubpass = Vk.SubpassExternal;
            return this;
        }

        public DependencyConfigurer WithStages(PipelineStageFlags src, PipelineStageFlags dst)
        {
            _dependency.SrcStageMask = src;
            _dependency.DstStageMask = dst;
            return this;
        }

        public DependencyConfigurer WithAccess(AccessFlags src, AccessFlags dst)
        {
            _dependency.SrcAccessMask = src;
            _dependency.DstAccessMask = dst;
            return this;
        }

        public RenderPassBuilder Add()
        {
            _parent._dependencies.Add(_dependency);
            return _parent;
        }
    }

    private class VkSubpassDescription : DisposableBuilder
    {
        public PipelineBindPoint PipelineBindPoint { get; set; }
        public List<AttachmentReference> ColorAttachments { get; } = new();
        public List<AttachmentReference> InputAttachments { get; } = new();
        public List<AttachmentReference> ResolveAttachments { get; } = new();
        public List<AttachmentReference> DepthStencilAttachment { get; set; } = new();

        public unsafe SubpassDescription ToNativeSubpass()
        {
            var color = CreateMemoryHandle(ColorAttachments.ToArray());
            var input = CreateMemoryHandle(InputAttachments.ToArray());
            var resolve = CreateMemoryHandle(ResolveAttachments.ToArray());
            var depth = CreateMemoryHandle(DepthStencilAttachment.ToArray());
            return new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint,
                ColorAttachmentCount = (uint)ColorAttachments.Count,
                PColorAttachments = ColorAttachments.Count > 0
                    ? (AttachmentReference*)color.Pointer
                    : null,
                InputAttachmentCount = (uint)InputAttachments.Count,
                PInputAttachments = InputAttachments.Count > 0
                    ? (AttachmentReference*)input.Pointer
                    : null,
                PResolveAttachments = ResolveAttachments.Count > 0
                    ? (AttachmentReference*)resolve.Pointer
                    : null,
                PDepthStencilAttachment = DepthStencilAttachment.Count > 0
                    ? (AttachmentReference*)depth.Pointer
                    : null
            };
        }
    }
}