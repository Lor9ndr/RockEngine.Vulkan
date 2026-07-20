using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using NLog;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class ShadowManager : IDisposable
    {
        private uint _capacity = 0;               // current max number of slots
        private readonly List<uint> _freeIndices = new(); // indices available inside current capacity
        private readonly Dictionary<Light, uint> _lightShadowMapIndices = new();

        // GPU resources – created/resized together
        private StorageBuffer<Matrix4x4>? _shadowMatricesUbo;
        private UniformBuffer? _csmDataUbo;
        private Texture? _shadowMapArray;
        private Texture? _pointShadowMapArray;

        // Bindings that are handed out to the renderer – must be recreated on resize
        private TextureBinding? _shadowMapsBinding;
        private TextureBinding? _pointShadowMapsBinding;
        private StorageBufferBinding<Matrix4x4>? _shadowMatricesBinding;
        private UniformBufferBinding? _csmDataBinding;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly VulkanContext _context;

        [Flags]
        private enum UploadFlags
        {
            None = 0,
            Point = 1 << 0,
            Csm = 1 << 1
        }

        public ShadowManager(VulkanContext context)
        {
            _context = context;
        }
        public void EnsureCapacity(int minSlots)
        {
            if (minSlots <= _capacity)
            {
                return;
            }

            uint newCapacity = Math.Max((uint)minSlots, _capacity == 0 ? 2 : _capacity * 2);
            Resize(newCapacity);
        }
        private void Resize(uint newCapacity)
        {
            // Dispose old resources if they exist
            _shadowMatricesUbo?.Dispose();
            _csmDataUbo?.Dispose();
            _shadowMapArray?.Dispose();
            _pointShadowMapArray?.Dispose();

            // Create new UBOs sized for the new capacity
            _shadowMatricesUbo = new StorageBuffer<Matrix4x4>(_context, (ulong)6 * newCapacity);
            _shadowMatricesBinding = new StorageBufferBinding<Matrix4x4>(_shadowMatricesUbo, 0, 0);

            _csmDataUbo = new UniformBuffer(_context, (ulong)(newCapacity * Marshal.SizeOf<CSMData>()));
            _csmDataBinding = new UniformBufferBinding(_csmDataUbo, 0, 0);

            // Create shadow map arrays with the correct number of layers
            _shadowMapArray = CreateShadowMapArray(newCapacity, 1024, false, "ShadowMapArray");
            _pointShadowMapArray = CreateShadowMapArray(newCapacity, 1024, true, "PointShadowMapArray");

            // Build new bindings (descriptor set bindings will need to be updated!)
            _shadowMapsBinding = new TextureBinding(
                4, 0, 0, 1, ImageLayout.ShaderReadOnlyOptimal, 0,
                _shadowMapArray.Image.ArrayLayers, _shadowMapArray);

            _pointShadowMapsBinding = new TextureBinding(
                4, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, 0,
                _pointShadowMapArray.Image.ArrayLayers, _pointShadowMapArray);

            // Update the free list – all slots from old capacity..newCapacity-1 are free
            _freeIndices.Clear();
            for (uint i = 0; i < newCapacity; i++)
            {
                // Don't re‑assign indices that are still in use – we handle that separately
                if (!_lightShadowMapIndices.ContainsValue(i))
                {
                    _freeIndices.Add(i);
                }
            }

            _capacity = newCapacity;
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

            // Ensure we have at least one free slot
            if (_freeIndices.Count == 0)
            {
                EnsureCapacity((int)_capacity + 1);
                // After resize, _freeIndices was repopulated with all free slots
            }

            var index = _freeIndices[0];
            _freeIndices.RemoveAt(0);
            _lightShadowMapIndices[light] = index;
            return index;
        }

        public void ReleaseShadowMapIndex(Light light)
        {
            if (_lightShadowMapIndices.Remove(light, out var index))
            {
                _freeIndices.Add(index);
                // Optional: shrink if no lights remain
                if (_lightShadowMapIndices.Count == 0 && _capacity > 0)
                {
                    Resize(0); // completely free GPU resources, or keep a minimum
                }
            }
        }

        public void UpdateShadowTexture(UploadBatch batch, Light light, VkImage shadowImage)
        {
            if (!_lightShadowMapIndices.TryGetValue(light, out var shadowIndex) || shadowIndex == uint.MaxValue)
            {
                return;
            }

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

        public TextureBinding GetShadowMapsBinding()
        {
            if (_shadowMapsBinding is null)
            {
                EnsureCapacity(1); // lazy init
            }

            return _shadowMapsBinding!;
        }

        public TextureBinding GetPointShadowMapsBinding()
        {
            if(_pointShadowMapsBinding is null)
            {
                EnsureCapacity(1);
            }
            return _pointShadowMapsBinding!;
        }

        public StorageBufferBinding<Matrix4x4> GetShadowMatricesBinding()
        {
            if (_shadowMatricesBinding is null)
            {
                EnsureCapacity(1);
            }
            return _shadowMatricesBinding!;
        }

        public UniformBufferBinding GetCSMDataBinding()
        {
            if (_csmDataBinding is null)
            {
                EnsureCapacity(1);
            }
            return _csmDataBinding!;
        }

        public void UpdateShadowMatrices(List<Light> shadowCastingLights, Camera mainCamera)
        {
            if (shadowCastingLights.Count == 0)
            {
                return;
            }
            EnsureCapacity(shadowCastingLights.Count);
            var csmDataArray = ArrayPool<CSMData>.Shared.Rent((int)_capacity);
            try
            {
                var batch = _context.GraphicsSubmitContext.CreateBatch();
                var uploadFlags = UploadFlags.None;

                foreach (var light in shadowCastingLights)
                {
                    var shadowIndex = AssignShadowMapIndex(light);
                    if (shadowIndex == uint.MaxValue)
                    {
                        continue;
                    }

                    light.SetShadowIndices(shadowIndex);

                    if (light.Type == LightType.Directional && light.CascadeCount > 1)
                    {
                        // CSM directional
                        var cascadeMatrices = mainCamera.ComputeCSMMatrices(light);
                        light.SetDirectionalShadowMatrices(cascadeMatrices);
                        var cascadeSplits = mainCamera.ComputeCascadeSplits(light.ShadowDistance, light.CascadeCount);

                        csmDataArray[shadowIndex] = new CSMData
                        {
                            CascadeMatrices0 = cascadeMatrices.Length > 0 ? cascadeMatrices[0] : Matrix4x4.Identity,
                            CascadeMatrices1 = cascadeMatrices.Length > 1 ? cascadeMatrices[1] : Matrix4x4.Identity,
                            CascadeMatrices2 = cascadeMatrices.Length > 2 ? cascadeMatrices[2] : Matrix4x4.Identity,
                            CascadeMatrices3 = cascadeMatrices.Length > 3 ? cascadeMatrices[3] : Matrix4x4.Identity,
                            CascadeSplits = new Vector4(
                                cascadeSplits.Length > 0 ? cascadeSplits[0] : 0,
                                cascadeSplits.Length > 1 ? cascadeSplits[1] : 0,
                                cascadeSplits.Length > 2 ? cascadeSplits[2] : 0,
                                cascadeSplits.Length > 3 ? cascadeSplits[3] : 0),
                            CSMParams = new Vector4(light.CascadeCount, light.ShadowMapSize, light.CSMShadowBias, light.NormalOffset),
                            ViewMatrix = mainCamera.ViewMatrix
                        };
                        uploadFlags |= UploadFlags.Csm;
                    }
                    else if (light.Type == LightType.Point)
                    {
                        var pointMatrices = light.GetShadowMatrix();
                        _shadowMatricesUbo.StageData(
                            batch,
                            pointMatrices,
                            startIndex: shadowIndex * 6);
                        uploadFlags |= UploadFlags.Point;
                    }
                }

                // Stage CSM data if needed
                if (uploadFlags.HasFlag(UploadFlags.Csm))
                {
                    // Important here to setup Marshal.SizeOf<CSMData>() * _maxShadowMaps not Marshal.SizeOf<CSMData>() * csmDataArray.Length
                    // because ArrayPool.Rent will return minimum array length.
                    // So it can be larger
                    batch.StageToBuffer(csmDataArray, _csmDataUbo.Buffer, 0,
                        (ulong)(Marshal.SizeOf<CSMData>() * _capacity));
                }

                // Build barriers according to flags
                var barriers = new List<BufferMemoryBarrier2>();
                if (uploadFlags.HasFlag(UploadFlags.Point))
                {
                    barriers.Add(new BufferMemoryBarrier2
                    {
                        SType = StructureType.BufferMemoryBarrier2,
                        SrcAccessMask = AccessFlags2.TransferWriteBit,
                        DstAccessMask = AccessFlags2.ShaderReadBit,
                        Buffer = _shadowMatricesUbo.Buffer,
                        Offset = 0,
                        Size = Vk.WholeSize,
                        SrcStageMask = PipelineStageFlags2.TransferBit,
                        DstStageMask = PipelineStageFlags2.GeometryShaderBit
                    });
                }
                if (uploadFlags.HasFlag(UploadFlags.Csm))
                {
                    barriers.Add(new BufferMemoryBarrier2
                    {
                        SType = StructureType.BufferMemoryBarrier2,
                        SrcAccessMask = AccessFlags2.TransferWriteBit,
                        DstAccessMask = AccessFlags2.UniformReadBit,
                        Buffer = _csmDataUbo.Buffer,
                        Offset = 0,
                        Size = Vk.WholeSize,
                        SrcStageMask = PipelineStageFlags2.TransferBit,
                        DstStageMask = PipelineStageFlags2.AllGraphicsBit
                    });
                }

                if (barriers.Count > 0)
                {
                    batch.PipelineBarrier(bufferMemoryBarriers: barriers.ToArray());
                }

                batch.Submit();
            }
            finally
            {
                ArrayPool<CSMData>.Shared.Return(csmDataArray, false);

            }
        }


        public void Dispose()
        {
            _lightShadowMapIndices.Clear();

            _shadowMatricesUbo?.Dispose();
            _csmDataUbo?.Dispose();
            _shadowMapArray?.Dispose();
            _pointShadowMapArray?.Dispose();
        }
    }


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