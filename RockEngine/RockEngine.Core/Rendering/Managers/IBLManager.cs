using RockEngine.Core.Diagnostics;
using RockEngine.Core.Extensions;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class IBLManager
    {
        private readonly VulkanContext _context;
        private readonly ComputeShaderManager _computeManager;
        private readonly BindingManager _bindingManager;
        private RckPipeline _irradiancePipeline;
        private RckPipeline _prefilterPipeline;
        private RckPipeline _brdfPipeline;

        public IBLManager(
            VulkanContext context,
            ComputeShaderManager computeManager,
            BindingManager bindingManager
            )
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

            var batch = _context.ComputeSubmitContext.CreateBatch();
            using (batch.BeginSection("GenerateIrradianceMap", 0))
            {
                batch.LabelObject("Irradiance cmd");
                // Transition layouts
                envMap.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.General, baseMipLevel: 0, envMap.Image.MipLevels, 0, 6);
                output.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.General, baseMipLevel: 0, 1, 0, 6);

                // Create material with required bindings – FIXED
                MaterialPass matPass = new MaterialPass(_irradiancePipeline);
                matPass.BindResource(new TextureBinding(
                    setLocation: 0,
                    bindingLocation: 0,
                    baseMipLevel: 0,
                    levelCount: 1,                           // only base mip
                    imageLayout: ImageLayout.General,
                    arrayLayer: 0,
                    layerCount: envMap.Image.ArrayLayers,    // all 6 layers
                    envMap
                ));
                matPass.BindResource(new StorageImageBinding(
                 texture: output,
                 setLocation: 0,
                 bindingLocation: 1,
                 layout: ImageLayout.General,
                 mipLevel: 0,
                 levelCount: 1,                          // only base mip
                 arrayLayer: 0,
                 layerCount: output.Image.ArrayLayers     // 6 layers → cube view
             ));

                // Set push constants
                const uint sampleCount = 1024u;
                matPass.PushConstant("pc", new IrradiancePushConstants()
                {
                    OutputSize = new Vector2D<int>((int)size),
                    DeltaPhi = (2f * MathF.PI) / sampleCount,
                    DeltaTheta = (0.5f * MathF.PI) / sampleCount
                });
                matPass.CmdPushConstants(batch);

                // Dispatch compute
                uint groupsX = (size + 31) / 32;
                uint groupsY = (size + 31) / 32;
                _bindingManager.BindResourcesForMaterial(0, matPass, batch, true);
                batch.BindPipeline(_irradiancePipeline, PipelineBindPoint.Compute);
                batch.Dispatch(groupsX, groupsY, 6);

                // Transition and return
                output.Image.TransitionImageLayout(batch, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal, baseMipLevel: 0, 1, 0, 6);
                envMap.Image.TransitionImageLayout(batch, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal, baseMipLevel: 0, envMap.Image.MipLevels, 0, 6);
            }

            await _context.ComputeSubmitContext.SubmitSingle(batch, VkFence.CreateNotSignaled(_context));
            return output;
        }

        public async Task<Texture> GeneratePrefilterMap(Texture envMap, uint size = 512)
        {
            Texture output = await CreateCubeTexture(size, Format.R16G16B16A16Sfloat, "PreFilter", true);

            uint mipLevels = output.Image.MipLevels;
            var batch = _context.ComputeSubmitContext.CreateBatch();
            batch.LabelObject("Prefilter cmd");
            using (PerformanceTracer.BeginSection("GeneratePrefilterMap", batch, 0))
            {
                // Transition base mip of input images
                envMap.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.General, baseMipLevel: 0, envMap.Image.MipLevels, 0, 6);

                batch.BindPipeline(_prefilterPipeline, PipelineBindPoint.Compute);

                for (uint mip = 0; mip < mipLevels; mip++)
                {
                    if (mip > 0)
                    {
                        var barrier = new ImageMemoryBarrier2
                        {
                            SType = StructureType.ImageMemoryBarrier2,
                            Image = output.Image,
                            OldLayout = ImageLayout.General,
                            NewLayout = ImageLayout.General,
                            SubresourceRange = new ImageSubresourceRange
                            {
                                AspectMask = ImageAspectFlags.ColorBit,
                                BaseMipLevel = mip - 1,
                                LevelCount = 1,
                                BaseArrayLayer = 0,
                                LayerCount = 6
                            },
                            SrcAccessMask = AccessFlags2.ShaderWriteBit,
                            DstAccessMask = AccessFlags2.ShaderReadBit,
                            SrcStageMask = PipelineStageFlags2.ComputeShaderBit,
                            DstStageMask = PipelineStageFlags2.FragmentShaderBit,
                        };
                        batch.PipelineBarrier(imageMemoryBarriers: [barrier]);
                    }

                    var mipSize = (uint)(size * Math.Pow(0.5, mip));
                    float roughness = mip / (float)(mipLevels - 1);

                    // Transition current mip to GENERAL before use
                    output.Image.TransitionImageLayout(
                        batch,
                        ImageLayout.Undefined,
                        ImageLayout.General,
                        baseMipLevel: mip,
                        levelCount: 1,
                        baseArrayLayer: 0,
                        layerCount: 6
                    );

                    // Create per-mip material – FIXED envMap binding
                    var material = new MaterialPass(_prefilterPipeline);
                    material.BindResource(new TextureBinding(
                        setLocation: 0,
                        bindingLocation: 0,
                        baseMipLevel: 0,
                        levelCount: 1,                               // only base mip
                        imageLayout: ImageLayout.General,
                        arrayLayer: 0,
                        layerCount: envMap.Image.ArrayLayers,        // all 6 layers
                        envMap
                    ));
                    material.BindResource(new StorageImageBinding(
                     texture: output,
                     setLocation: 0,
                     bindingLocation: 1,
                     layout: ImageLayout.General,
                     mipLevel: mip,
                     levelCount: 1,                          // only this mip
                     arrayLayer: 0,
                     layerCount: output.Image.ArrayLayers     // 6 layers → cube view
                 ));

                    // Set push constants
                    material.PushConstant("pc", new PrefilterPushConstants()
                    {
                        OutputSize = new Vector2D<int>((int)mipSize),
                        MipLevel = mip,
                        Roughness = roughness
                    });
                    material.CmdPushConstants(batch);

                    // Bind and dispatch
                    _bindingManager.BindResourcesForMaterial(0, material, batch, true);
                    uint groups = (mipSize + 31) / 32;
                    _computeManager.Dispatch(batch, groups, groups, 6);
                }

                // Transition all mip levels to SHADER_READ_ONLY_OPTIMAL
                output.Image.TransitionImageLayout(
                    batch,
                    ImageLayout.General,
                    ImageLayout.ShaderReadOnlyOptimal,
                    baseMipLevel: 0,
                    levelCount: mipLevels,
                    baseArrayLayer: 0,
                    layerCount: 6
                );
                envMap.Image.TransitionImageLayout(batch, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal, baseMipLevel: 0, envMap.Image.MipLevels, 0, 6);
            }

            await _context.ComputeSubmitContext.SubmitSingle(batch);
            return output;
        }
        public async Task<Texture> GenerateBRDFLUT(uint size = 512)
        {
            Texture output = null;
            var batch = _context.ComputeSubmitContext.CreateBatch();
            batch.LabelObject("BRDFLUT cmd");
            output = new Texture.Builder(_context)
                  .SetSize(new Extent2D(size, size))
                  .SetFormat(Format.R16G16Sfloat)
                  .SetUsage(ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit)
                  .Build();
            using (PerformanceTracer.BeginSection("GenerateBRDFLUT", batch, 0))
            {


                output.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.General);
                batch.BindPipeline(_brdfPipeline, PipelineBindPoint.Compute);

                var material = new MaterialPass(_brdfPipeline);
                material.BindResource(new StorageImageBinding(output, 0, 0, ImageLayout.General));

                // Ensure proper descriptor set binding
                _bindingManager.BindResourcesForMaterial(0, material, batch, true);

                uint groups = (size + 31) / 32;
                _computeManager.Dispatch(batch, groups, groups, 1);

                output.Image.TransitionImageLayout(batch, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal);
                var semaphore = VkSemaphore.Create(_context);
                semaphore.LabelObject("BRDFLUT SEMAPHORE");
            }

            await _context.ComputeSubmitContext.SubmitSingle(batch);

            return output;
        }

        private Task<Texture> CreateCubeTexture(uint size, Format format, string name, bool mipmaps = false)
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

            var texture = builder.Build();
            texture.Image.LabelObject(name);
            return Task.FromResult(texture);
        }

        [GLSLStruct(GLSLMemoryLayout.Std140)]

        private struct IrradiancePushConstants
        {
            public Vector2D<int> OutputSize;
            public float DeltaPhi;
            public float DeltaTheta;
        }

        [GLSLStruct(GLSLMemoryLayout.Std140)]
        private struct PrefilterPushConstants
        {
            public Vector2D<int> OutputSize;
            public float Roughness;
            public uint MipLevel;
        }
    }
}
