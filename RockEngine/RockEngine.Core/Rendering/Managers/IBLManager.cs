using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Maths;
using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
     public class IBLManager
    {
        private readonly VulkanContext _context;
        private readonly ComputeShaderManager _computeManager;
        private readonly BindingManager _bindingManager;
        
        private VkPipeline _irradiancePipeline;
        private VkPipeline _prefilterPipeline;
        private VkPipeline _brdfPipeline;

        public IBLManager(
            VulkanContext context,
            ComputeShaderManager computeManager,
            BindingManager bindingManager)
        {
            _context = context;
            _computeManager = computeManager;
            _bindingManager = bindingManager;
        }

        public async Task InitializeAsync()
        {
            // Create all compute pipelines
            _irradiancePipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders/irradiance.comp.spv",
                "IrradianceGen");

            _prefilterPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders/prefilter.comp.spv",
                "PrefilterGen");

            _brdfPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders/brdf.comp.spv",
                "BRDFGen");
        }

        public async Task<Texture> GenerateIrradianceMap(Texture envMap, uint size = 128)
        {
            var output = await CreateCubeTexture(size, Format.R16G16B16A16Sfloat, "Irradiance");
            var batch = _context.SubmitComputeContext.CreateBatch();
            var fence = VkFence.CreateNotSignaled(_context);

            var cmd = batch.CommandBuffer;
            cmd.LabelObject("Irradiance cmd");
            // Transition layouts
            envMap.Image.TransitionImageLayout(cmd, ImageLayout.General, 0, envMap.Image.MipLevels, 0, 6);
            output.Image.TransitionImageLayout(cmd, ImageLayout.General, 0, 1, 0, 6);

            // Create material with required bindings
            var material = new Material(_irradiancePipeline);
            material.Bind(new TextureBinding(0, 0, default, envMap));
            material.Bind(new StorageImageBinding([output], 0, 1));

            // Set push constants
            const uint sampleCount = 1024u;
            material.PushConstant("pc", new IrradiancePushConstants()
            {
                OutputSize = new Vector2D<int>((int)size),
                DeltaPhi = (2f * MathF.PI) / sampleCount,
                DeltaTheta = (0.5f * MathF.PI) / sampleCount
            });
            material.CmdPushConstants(cmd);

            // Dispatch compute
            uint groupsX = (size + 31) / 32;
            uint groupsY = (size + 31) / 32;
            _bindingManager.BindResourcesForMaterial(0,material, cmd, true);
            cmd.BindPipeline(_irradiancePipeline, PipelineBindPoint.Compute);
            _computeManager.Dispatch(cmd, groupsX, groupsY, 6);
            // Transition and return
            output.Image.TransitionImageLayout(cmd, ImageLayout.ShaderReadOnlyOptimal, 0, 1, 0, 6);
            envMap.Image.TransitionImageLayout(cmd, ImageLayout.ShaderReadOnlyOptimal, 0, envMap.Image.MipLevels, 0, 6);


            var semaphore = VkSemaphore.Create(_context);
            semaphore.LabelObject("IRRADIANCE SEMAPHORE");
            batch.AddSignalSemaphore(semaphore);
            batch.Submit();
            _context.SubmitContext.AddWaitSemaphore(semaphore, PipelineStageFlags.FragmentShaderBit);
            await _context.SubmitComputeContext.FlushAsync(fence);
            return output;
        }

        public async Task<Texture> GeneratePrefilterMap(Texture envMap, uint size = 512)
        {
            var output = await CreateCubeTexture(size, Format.R16G16B16A16Sfloat, "PreFilter", true);
            uint mipLevels = output.Image.MipLevels;
            var batch = _context.SubmitComputeContext.CreateBatch();
            var cmd = batch.CommandBuffer;
            cmd.LabelObject("Prefilter cmd");
            // Transition base mip of input/output images
            if (envMap.Image.GetMipLayout(0,0) != ImageLayout.General)
            {
                envMap.Image.TransitionImageLayout(cmd, ImageLayout.General, 0, envMap.Image.MipLevels, 0, 6);
            }

            cmd.BindPipeline(_prefilterPipeline, PipelineBindPoint.Compute);

            for (uint mip = 0; mip < mipLevels; mip++)
            {
                var mipSize = (uint)(size * Math.Pow(0.5, mip));
                float roughness = mip / (float)(mipLevels - 1);

                // Transition current mip to GENERAL before use
                output.Image.TransitionImageLayout(
                    cmd,
                    ImageLayout.General,
                    baseMipLevel: mip,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: 6
                );

                // Create per-mip material
                var material = new Material(_prefilterPipeline);
                material.Bind(new TextureBinding(0, 0, default, envMap));
                material.Bind(new StorageImageBinding(output, 0, 1, mip));

                // Set push constants
                material.PushConstant("pc", new PrefilterPushConstants()
                {
                    OutputSize = new Vector2D<int>((int)mipSize),
                    MipLevel = mip,
                    Roughness = roughness
                });
                material.CmdPushConstants(cmd);

                // Bind and dispatch
                _bindingManager.BindResourcesForMaterial(0,material, cmd, true);
                uint groups = (mipSize + 31) / 32;
                _computeManager.Dispatch(cmd, groups, groups, 6);
            }

            // Transition all mip levels to SHADER_READ_ONLY_OPTIMAL
            output.Image.TransitionImageLayout(
                cmd,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: mipLevels,
                baseArrayLayer: 0,
                layerCount: 6
            );
            envMap.Image.TransitionImageLayout(cmd, ImageLayout.ShaderReadOnlyOptimal, 0, envMap.Image.MipLevels, 0, 6);

            var semaphore = VkSemaphore.Create(_context);
            semaphore.LabelObject("PREFILTER SEMAPHORE");

            batch.AddSignalSemaphore(semaphore);
            batch.Submit();
            var fence = VkFence.CreateNotSignaled(_context);
            await _context.SubmitComputeContext.FlushAsync(fence);
            _context.SubmitContext.AddWaitSemaphore(semaphore, PipelineStageFlags.FragmentShaderBit);
            fence.Reset();
            return output;
        }
        public async Task<Texture> GenerateBRDFLUT(uint size = 512)
        {
            var batch = _context.SubmitComputeContext.CreateBatch();
            var cmd = batch.CommandBuffer;
            cmd.LabelObject("BRDFLUT cmd");

            var output = new Texture.Builder(_context)
                .SetSize(new Extent2D(size, size))
                .SetFormat(Format.R16G16Sfloat)
                .SetUsage(ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit)
                .Build();

            output.Image.TransitionImageLayout(cmd, ImageLayout.General);
            cmd.BindPipeline(_brdfPipeline, PipelineBindPoint.Compute);

            var material = new Material(_brdfPipeline);
            material.Bind(new StorageImageBinding(output, 0, 0));

            // Ensure proper descriptor set binding
            _bindingManager.BindResourcesForMaterial(0, material, cmd, true);

            uint groups = (size + 31) / 32;
            _computeManager.Dispatch(cmd, groups, groups, 1);

            output.Image.TransitionImageLayout(cmd, ImageLayout.ShaderReadOnlyOptimal);
            var semaphore = VkSemaphore.Create(_context);
            semaphore.LabelObject("BRDFLUT SEMAPHORE");
            batch.AddSignalSemaphore(semaphore);
            batch.Submit();
            var fence = VkFence.CreateNotSignaled(_context);

            await _context.SubmitComputeContext.FlushAsync(fence);
            _context.SubmitContext.AddWaitSemaphore(semaphore, PipelineStageFlags.FragmentShaderBit);
            await fence.WaitAsync();
            fence.Reset();
            return output;
        }

        private  Task<Texture> CreateCubeTexture(uint size, Format format, string name, bool mipmaps = false)
        {
            var builder = new Texture.Builder(_context)
                .SetSize(new Extent2D(size, size))
                .SetFormat(format)
                .SetCubemap(true)
                .SetUsage(ImageUsageFlags.StorageBit | 
                         ImageUsageFlags.SampledBit | 
                         ImageUsageFlags.TransferSrcBit)
                .WaitCompute();

            if (mipmaps)
            {
                builder.WithMipmaps(true)
                // Ensure we request mipmapped storage usage
                .SetUsage(ImageUsageFlags.StorageBit |
                         ImageUsageFlags.SampledBit |
                         ImageUsageFlags.TransferSrcBit |
                         ImageUsageFlags.TransferDstBit);
            }

            var texture =  builder.Build();
            texture.Image.LabelObject(name);
            _context.SubmitContext.Flush();
            return Task.FromResult(texture);
        }

        // For irradiance shader
        [StructLayout(LayoutKind.Sequential)]
        private struct IrradiancePushConstants
        {
            public Vector2D<int> OutputSize;
            public float DeltaPhi;
            public float DeltaTheta;
        }

        // For prefilter shader
        [StructLayout(LayoutKind.Sequential)]
        private struct PrefilterPushConstants
        {
            public Vector2D<int> OutputSize;
            public float Roughness;
            public uint MipLevel;
        }
    }
}
