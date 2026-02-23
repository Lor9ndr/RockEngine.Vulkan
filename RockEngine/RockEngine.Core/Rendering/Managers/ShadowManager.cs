using NLog;

using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
    public class ShadowManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly UniformBuffer _shadowMatricesUbo;
        private readonly UniformBuffer _csmDataUbo;
        private readonly Texture _shadowMapArray;
        private readonly Texture _pointShadowMapArray;
        private readonly TextureBinding _shadowMapsBinding;
        private readonly TextureBinding _pointShadowMapsBinding;
        private readonly UniformBufferBinding _shadowMatricesBinding;
        private readonly UniformBufferBinding _csmDataBinding;

        private readonly Dictionary<Light, uint> _lightShadowMapIndices = new();
        private readonly Queue<uint> _availableShadowIndices = new();
        private readonly uint _maxShadowMaps = 20;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public ShadowManager(VulkanContext context)
        {
            _context = context;

            // Create uniform buffer for shadow matrices
            _shadowMatricesUbo = new UniformBuffer(_context, (uint)(Unsafe.SizeOf<Matrix4x4>() * 6 * (int)_maxShadowMaps));
            _shadowMatricesBinding = new UniformBufferBinding(_shadowMatricesUbo, 0, 0);

            // Create uniform buffer for CSM data
            _csmDataUbo = new UniformBuffer(_context, (ulong)(_maxShadowMaps * Marshal.SizeOf<CSMData>()));
            _csmDataBinding = new UniformBufferBinding(_csmDataUbo, 0, 0);

            // Create shadow map arrays
            _shadowMapArray = CreateShadowMapArray(_maxShadowMaps, 1024, false, "ShadowMapArray");
            _pointShadowMapArray = CreateShadowMapArray(_maxShadowMaps, 1024, true, "PointShadowMapArray");

            // Create texture bindings
            _shadowMapsBinding = new TextureBinding(4,
                                                    0,
                                                    0,
                                                    1,
                                                    ImageLayout.ShaderReadOnlyOptimal,
                                                    0,
                                                    _shadowMapArray.Image.ArrayLayers,
                                                    _shadowMapArray);
            _pointShadowMapsBinding = new TextureBinding(4, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal,0, _pointShadowMapArray.Image.ArrayLayers, _pointShadowMapArray);

            // Initialize available indices
            for (uint i = 0; i < _maxShadowMaps; i++)
            {
                _availableShadowIndices.Enqueue(i);
            }
        }

        private Texture CreateShadowMapArray(uint arrayLayers, uint size, bool isPointLight, string name)
        {
            var format = Format.D32Sfloat;
            var usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit;

            Texture texture;
            if (isPointLight)
            {
                texture = Texture2D.CreatePointShadowMapArray(_context, size, arrayLayers, format, usage, name);
            }
            else
            {
                texture = Texture2D.CreateShadowMapArray(_context, size, arrayLayers, format, usage, name);
            }

            // Ensure the entire array starts in ShaderReadOnlyOptimal
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            texture.Image.TransitionImageLayout(
                batch,
                ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: arrayLayers * (isPointLight ? 6u : 1u)
            );
            batch.Submit();

            return texture;
        }

        public uint AssignShadowMapIndex(Light light)
        {
            if (_lightShadowMapIndices.TryGetValue(light, out var existingIndex))
            {
                return existingIndex;
            }

            if (_availableShadowIndices.Count > 0)
            {
                var index = _availableShadowIndices.Dequeue();
                _lightShadowMapIndices[light] = index;
                return index;
            }

            return uint.MaxValue;
        }

        public void ReleaseShadowMapIndex(Light light)
        {
            if (_lightShadowMapIndices.Remove(light, out var index))
            {
                _availableShadowIndices.Enqueue(index);
            }
        }

        public void UpdateShadowTexture(UploadBatch batch, Light light, VkImage shadowImage)
        {
            if (!_lightShadowMapIndices.TryGetValue(light, out var shadowIndex) || shadowIndex == uint.MaxValue)
                return;

            uint layerCount = GetLayerCountForLight(light);
            var targetTexture = light.Type == LightType.Point ? _pointShadowMapArray : _shadowMapArray;

            uint destinationBaseLayer = light.Type == LightType.Point ? shadowIndex * 6 : shadowIndex;

            if (destinationBaseLayer + layerCount > targetTexture.Image.ArrayLayers)
            {
                _logger.Warn($"Shadow map copy would exceed array bounds. Light: {light.Entity.Name}, Index: {shadowIndex}");
                return;
            }



            // Transition destination array layers to TransferDstOptimal
            targetTexture.Image.TransitionImageLayout(
                batch,
                ImageLayout.ShaderReadOnlyOptimal,
                ImageLayout.TransferDstOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: destinationBaseLayer,
                layerCount: layerCount
            );

            // Copy all layers at once
            batch.CopyImage(
                source: shadowImage,
                srcLayout: ImageLayout.TransferSrcOptimal,
                destination: targetTexture.Image,
                dstLayout: ImageLayout.TransferDstOptimal,
                srcLayer: 0,
                dstLayer: destinationBaseLayer,
                layerCount: layerCount
            );

            // Transition destination back to ShaderReadOnlyOptimal for sampling
            targetTexture.Image.TransitionImageLayout(
                batch,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: destinationBaseLayer,
                layerCount: layerCount
            );

            //Transition source back to DepthStencilAttachmentOptimal for next frame's rendering
            shadowImage.TransitionImageLayout(
                batch,
                ImageLayout.TransferSrcOptimal,
                ImageLayout.DepthStencilAttachmentOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: layerCount
            );
        }

        // Helper method to get correct layer count for different light types
        private uint GetLayerCountForLight(Light light)
        {
            return light.Type switch
            {
                LightType.Point => 6u,
                LightType.Directional when light.CascadeCount > 1 => (uint)light.CascadeCount, // CSM uses multiple layers
                _ => 1u
            };
        }

        public TextureBinding GetShadowMapsBinding() => _shadowMapsBinding;
        public TextureBinding GetPointShadowMapsBinding() => _pointShadowMapsBinding;
        public UniformBufferBinding GetShadowMatricesBinding() => _shadowMatricesBinding;
        public UniformBufferBinding GetCSMDataBinding() => _csmDataBinding;

        public void UpdateShadowMatrices(List<Light> shadowCastingLights, Camera mainCamera)
        {
            if (shadowCastingLights.Count == 0) return;

            var shadowMatrices = new List<Matrix4x4>();
            var csmDataArray = new CSMData[_maxShadowMaps];

            foreach (var light in shadowCastingLights)
            {
                if (shadowMatrices.Count >= _maxShadowMaps * 16) break;

                var shadowIndex = AssignShadowMapIndex(light);
                if (shadowIndex == uint.MaxValue) continue;

                light.SetShadowMapIndex(shadowIndex);

                if (light.Type == LightType.Directional && light.CascadeCount > 1)
                {
                    // Update cascade splits using camera far plane
                    light.UpdateCascadeSplits(mainCamera.FarClip);

                    var cascadeMatrices = light.CalculateCSMMatrices(mainCamera, light.Entity.Transform.EulerAngles);

                    // Create enhanced CSM data
                    var csmData = new CSMData
                    {
                        CascadeMatrices0 = cascadeMatrices.Length > 0 ? cascadeMatrices[0] : Matrix4x4.Identity,
                        CascadeMatrices1 = cascadeMatrices.Length > 1 ? cascadeMatrices[1] : Matrix4x4.Identity,
                        CascadeMatrices2 = cascadeMatrices.Length > 2 ? cascadeMatrices[2] : Matrix4x4.Identity,
                        CascadeMatrices3 = cascadeMatrices.Length > 3 ? cascadeMatrices[3] : Matrix4x4.Identity,
                        CascadeSplits = new Vector4(
                            light.CascadeSplits[0],
                            light.CascadeSplits[1],
                            light.CascadeSplits[2],
                            light.CascadeSplits[3]),
                        CSMParams = new Vector4(
                            light.CascadeCount,
                            light.ShadowMapSize,
                            light.CSMShadowBias,
                            light.NormalOffset),
                        ViewMatrix = mainCamera.ViewMatrix
                    };

                    csmDataArray[shadowIndex] = csmData;

                    // Add cascade matrices (only CascadeCount, not +1)
                    for (int i = 0; i < light.CascadeCount; i++)
                    {
                        shadowMatrices.Add(cascadeMatrices[i]);
                    }

                    // Pad to 16 matrices for UBO alignment
                    int matricesToAdd = 16 - light.CascadeCount;
                    for (int i = 0; i < matricesToAdd; i++)
                    {
                        shadowMatrices.Add(Matrix4x4.Identity);
                    }
                }
                else if (light.Type == LightType.Point)
                {
                    // Point lights - 6 matrices
                    var pointMatrices = light.GetShadowMatrix();
                    for (int i = 0; i < 6; i++)
                    {
                        shadowMatrices.Add(pointMatrices[i]);
                    }
                    // Pad to 16
                    for (int i = 6; i < 16; i++)
                    {
                        shadowMatrices.Add(Matrix4x4.Identity);
                    }
                }
                else
                {
                    // Single matrix for spot/non-CSM directional
                    var singleMatrix = light.GetShadowMatrix();
                    shadowMatrices.Add(singleMatrix[0]);
                    // Pad to 16
                    for (int i = 1; i < 16; i++)
                    {
                        shadowMatrices.Add(Matrix4x4.Identity);
                    }
                }
            }

            // Update GPU buffers
            if (shadowMatrices.Count > 0)
            {
                var batch = _context.GraphicsSubmitContext.CreateBatch();

                // Update shadow matrices buffer (16 matrices per light)
                batch.StageToBuffer(
                    shadowMatrices.ToArray(),
                    _shadowMatricesUbo.Buffer,
                    0,
                    (uint)(Unsafe.SizeOf<Matrix4x4>() * shadowMatrices.Count)
                );

                // Update CSM data buffer
                batch.StageToBuffer(csmDataArray, _csmDataUbo.Buffer, 0, (ulong)(Unsafe.SizeOf<CSMData>() * csmDataArray.Length));

                // Add pipeline barrier
                var bufferBarrier = new BufferMemoryBarrier2
                {
                    SType = StructureType.BufferMemoryBarrier2,
                    SrcAccessMask = AccessFlags2.TransferWriteBit,
                    DstAccessMask = AccessFlags2.UniformReadBit | AccessFlags2.ShaderReadBit,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Buffer = _shadowMatricesUbo.Buffer,
                    Offset = 0,
                    Size = Vk.WholeSize,
                    SrcStageMask = PipelineStageFlags2.TransferBit,
                    DstStageMask = PipelineStageFlags2.VertexShaderBit,
                };

                batch.PipelineBarrier(
                    bufferMemoryBarriers: [bufferBarrier]
                );

                batch.Submit();
            }
        }


        public void Dispose()
        {
            _lightShadowMapIndices.Clear();
            _availableShadowIndices.Clear();

            _shadowMatricesUbo?.Dispose();
            _csmDataUbo?.Dispose();
            _shadowMapArray?.Dispose();
            _pointShadowMapArray?.Dispose();
        }
    }

    [GLSLStruct(GLSLMemoryLayout.Scalar)]

    public struct CSMData
    {
        public Matrix4x4 CascadeMatrices0;
        public Matrix4x4 CascadeMatrices1;
        public Matrix4x4 CascadeMatrices2;
        public Matrix4x4 CascadeMatrices3;
        public Vector4 CascadeSplits;
        public Vector4 CSMParams; // x: cascadeCount, y: shadowMapSize, z: bias, w: normalOffset
        public Matrix4x4 ViewMatrix;
    }
}