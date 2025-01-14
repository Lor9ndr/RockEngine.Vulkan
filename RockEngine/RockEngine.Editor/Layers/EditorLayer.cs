using RockEngine.Core;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Editor.EditorComponents;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace RockEngine.Editor.Layers
{
    public class EditorLayer : ILayer
    {
        private readonly World _world;
        private readonly RenderingContext _context;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly Renderer _renderer;
        private readonly IInputContext _inputContext;

        private VkPipelineLayout _pipelineLayout;
        private VkPipeline _pipeline;

        public EditorLayer(World world, RenderingContext context, GraphicsEngine graphicsEngine, Renderer renderer, IInputContext inputContext)
        {
            _world = world;
            _context = context;
            _graphicsEngine = graphicsEngine;
            _renderer = renderer;
            _inputContext = inputContext;
        }

        public async Task OnAttach()
        {
            await CretePipeline();
            using var pool = VkCommandPool.Create(_context, new CommandPoolCreateInfo 
            { 
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit
            });
            AssimpLoader assimpLoader = new AssimpLoader();
            var meshes = await assimpLoader.LoadMeshesAsync("F:\\RockEngine.Vulkan\\RockEngine\\RockEngine.Editor\\Resources\\Models\\SponzaAtrium\\scene.gltf", _context, pool.AllocateCommandBuffer());
            var cam = _world.CreateEntity();
            var debugCam = cam.AddComponent<DebugCamera>();
            debugCam.SetInputContext(_inputContext);
            
            foreach (var item in meshes)
            {
                var entity = _world.CreateEntity();
                entity.Transform.Scale = new System.Numerics.Vector3(0.1f);
                entity.Transform.Position = new System.Numerics.Vector3(0);
                var mesh = entity.AddComponent<Mesh>();
                mesh.SetMeshData(item.Vertices, item.Indices);
                mesh.Material = new Material(_pipeline, item.textures);
            }
        }

        private async Task CretePipeline()
        {
            VkShaderModule vkShaderModuleFrag =
                await VkShaderModule.CreateAsync(_context, "Shaders\\Shader.frag.spv", ShaderStageFlags.FragmentBit);

            VkShaderModule vkShaderModuleVert =
                await VkShaderModule.CreateAsync(_context, "Shaders\\Shader.vert.spv", ShaderStageFlags.VertexBit);

            _pipelineLayout = VkPipelineLayout.Create(_context, vkShaderModuleVert, vkShaderModuleFrag);

            var binding_desc = new VertexInputBindingDescription();
            binding_desc.Stride = (uint)Unsafe.SizeOf<Vertex>();
            binding_desc.InputRate = VertexInputRate.Vertex;

            var color_attachment = new PipelineColorBlendAttachmentState();
            color_attachment.BlendEnable = new Silk.NET.Core.Bool32(true);
            color_attachment.SrcColorBlendFactor = BlendFactor.SrcAlpha;
            color_attachment.DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha;
            color_attachment.ColorBlendOp = BlendOp.Add;
            color_attachment.SrcAlphaBlendFactor = BlendFactor.One;
            color_attachment.DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha;
            color_attachment.AlphaBlendOp = BlendOp.Add;
            color_attachment.ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit;

            using GraphicsPipelineBuilder pipelineBuilder = new GraphicsPipelineBuilder(_context, "Main")
                 .WithShaderModule(vkShaderModuleVert)
                 .WithShaderModule(vkShaderModuleFrag)
                 .WithRasterizer(new VulkanRasterizerBuilder())
                 .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                 .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                     .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                 .WithViewportState(new VulkanViewportStateInfoBuilder()
                     .AddViewport(new Viewport() { Height = _graphicsEngine.Swapchain.Surface.Size.Y, Width = _graphicsEngine.Swapchain.Surface.Size.X })
                     .AddScissors(new Rect2D() 
                     {
                         Offset = new Offset2D(),
                         Extent = new Extent2D((uint?)_graphicsEngine.Swapchain.Surface.Size.X, (uint?)_graphicsEngine.Swapchain.Surface.Size.Y) 
                         }))
                 .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                 .WithColorBlendState(new VulkanColorBlendStateBuilder()
                     .AddAttachment(color_attachment))
                 .AddRenderPass(_renderer.RenderPass.RenderPass)
                 .WithPipelineLayout(_pipelineLayout)
                 .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor)
                    )
                 .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo()
                 {
                     DepthTestEnable = true,
                     DepthWriteEnable = true,
                     DepthCompareOp = CompareOp.Less,
                     DepthBoundsTestEnable = false,
                     MinDepthBounds = 0.0f,
                     MaxDepthBounds = 1.0f,
                     StencilTestEnable = false,
                 });
            _pipeline = _renderer.PipelineManager.Create(pipelineBuilder);
        }

        

        public void OnDetach()
        {
        }

        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
            
        }
        private float _totalElapsedTime = 0f; // Track total elapsed time
        private const float ANIMATION_DURATION = 5f; // Animation cycle duration in seconds
        public void OnUpdate()
        {
            /*_totalElapsedTime += Time.DeltaTime; // Update elapsed time

            // Calculate animation progress (0.0 to 1.0)
            float animationProgress = (_totalElapsedTime % ANIMATION_DURATION) / ANIMATION_DURATION;

            // Apply transformations to the entity (assuming index 5)
            var targetEntity = _world.GetEntities().ElementAtOrDefault(5);
            if (targetEntity != null)
            {
                // Example transformations (adjust as needed)
                targetEntity.Transform.Rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.01f); // Rotate over time
                float scaleFactor = 1f + 0.5f * (float)Math.Sin(animationProgress * 2f * Math.PI); // Scale up and down
                targetEntity.Transform.Scale = new Vector3(scaleFactor);
                targetEntity.Transform.Position = new Vector3(10f * (float)Math.Cos(animationProgress * 2f * Math.PI), 0, 0); // Move in a circle
            }*/
        }
    }
}
