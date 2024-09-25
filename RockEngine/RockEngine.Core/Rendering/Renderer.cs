using RockEngine.Core.ECS.Components;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class Renderer
    {
        private readonly GraphicsEngine _graphicsEngine;
        private readonly RenderingContext _vulkanContext;

        public Camera CurrentCamera { get; private set; }
        public VkCommandBuffer CurrentCommandBuffer { get; private set; }
        public VkDescriptorPool DescriptorPool { get; internal set; }
        public VkCommandPool CommandPool { get; internal set; }
        private VkPipeline _pipeline;

        public Renderer(RenderingContext context, GraphicsEngine graphicsEngine)
        {
            _vulkanContext = context;
            _graphicsEngine = graphicsEngine;
        }

      /*  internal void BindPipeline(VkPipeline pipeline)
        {
            CurrentCommandBuffer.BindPipeline(pipeline);
            _pipeline = pipeline;
        }

        internal void DrawMesh(VkBuffer vertexBuffer, uint length)
        {
            vertexBuffer.BindVertexBuffer(CurrentCommandBuffer);
            CurrentCommandBuffer.Draw(length, 1, 0, 0);
        }

        internal void DrawMesh(VkBuffer vertexBuffer, VkBuffer indexBuffer, uint countVertices, uint countIndices)
        {
            vertexBuffer.BindVertexBuffer(CurrentCommandBuffer);
            indexBuffer.BindIndexBuffer(CurrentCommandBuffer, 0, IndexType.Uint32);
            CurrentCommandBuffer.DrawIndexed(countIndices, 1, 0, 0, 0);
        }

        public unsafe void UseMaterial(Material material)
        {
           *//* var descriptors = stackalloc DescriptorSet[material.Textures.Length];
            foreach (var setLayout in _pipeline.Layout.DescriptorSetLayouts)
            {
                foreach (var in collection)
                {

                }
            }

            RenderingContext.Vk.CmdBindDescriptorSets(CurrentCommandBuffer, PipelineBindPoint.Graphics, material.Pipeline.Layout, 0, )*//*
        }*/
    }
}
