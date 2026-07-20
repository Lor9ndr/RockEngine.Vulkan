using System.Numerics;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components.UI;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using ZLinq;

namespace RockEngine.Core.Rendering.Managers
{
    public class UIManager : IDisposable
    {
        private readonly VulkanContext _vk;
        private readonly BindingManager _bindingManager;
        private readonly List<UICanvas> _canvases = new();

        private VkBuffer? _vertexBuffer;
        private VkBuffer? _indexBuffer;
        private uint _vertexCount;
        private uint _indexCount;

        // Separate counts for image and text draws
        public uint ImageIndexCount { get; private set; }
        public uint TextIndexCount { get; private set; }
        public uint TextFirstIndex { get; private set; } // start index in index buffer

        public UIManager(VulkanContext vk, BindingManager bindingManager)
        {
            _vk = vk;
            _bindingManager = bindingManager;
        }

        public void CollectFromWorld()
        {
            _canvases.Clear();
            var world = IoC.Container.GetInstance<World>();
            if (world == null)
            {
                return;
            }

            foreach (var entity in world.GetEntitiesWithComponent<UICanvas>())
            {
                CollectCanvasesRecursive(entity);
            }
        }

        private void CollectCanvasesRecursive(Entity entity)
        {
            if (entity.TryGetComponent<UICanvas>(out var canvas) && entity.IsActive)
            {
                _canvases.Add(canvas);
            }

            foreach (var child in entity.Children)
            {
                CollectCanvasesRecursive(child);
            }
        }

        public unsafe void RebuildBuffers()
        {
            // Separate counting
            int imageQuads = 0, textQuads = 0;
            foreach (var canvas in _canvases)
            {
                if (canvas.Camera == null)
                {
                    continue;
                }

                foreach (var uiEntity in canvas.UIEntities)
                {
                    if (!uiEntity.IsActive)
                    {
                        continue;
                    }

                    if (!uiEntity.TryGetComponent<UIRenderable>(out var renderable) ||
                        !uiEntity.TryGetComponent<RectTransform>(out _))
                    {
                        continue;
                    }

                    if (renderable is UIText text)
                    {
                        textQuads += text.Text.Length;
                    }
                    else
                    {
                        imageQuads++;
                    }
                }
            }

            int totalQuads = imageQuads + textQuads;
            int vertexCount = totalQuads * 4;
            int indexCount = totalQuads * 6;

            if (vertexCount == 0)
            {
                _vertexCount = _indexCount = ImageIndexCount = TextIndexCount = 0;
                TextFirstIndex = 0;
                return;
            }

            ulong vertexBufferSize = (ulong)(vertexCount * UIVertex.SizeInBytes);
            ulong indexBufferSize = (ulong)(indexCount * sizeof(uint));

            // Resize buffers if needed
            if (_vertexBuffer == null || _vertexBuffer.Size < vertexBufferSize)
            {
                _vertexBuffer?.Dispose();
                _vertexBuffer = VkBuffer.Create(_vk, vertexBufferSize,
                    BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit);
                _vertexBuffer.LabelObject("UI.VertexBuffer");
            }
            if (_indexBuffer == null || _indexBuffer.Size < indexBufferSize)
            {
                _indexBuffer?.Dispose();
                _indexBuffer = VkBuffer.Create(_vk, indexBufferSize,
                    BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit);
                _indexBuffer.LabelObject("UI.IndexBuffer");
            }

            using var stagingVert = VkBuffer.Create(_vk, vertexBufferSize,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            using var stagingIdx = VkBuffer.Create(_vk, indexBufferSize,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            using (var memVert = stagingVert.MapMemory())
            using (var memIdx = stagingIdx.MapMemory())
            {
                var vertSpan = new Span<UIVertex>((void*)memVert.Pointer, vertexCount);
                var idxSpan = new Span<uint>((void*)memIdx.Pointer, indexCount);

                int vIdx = 0, iIdx = 0;
                uint baseVertex = 0;

                // --- Images first ---
                foreach (var canvas in _canvases)
                {
                    if (canvas.Camera == null)
                    {
                        continue;
                    }

                    Vector2 canvasSize = new(canvas.Camera.RenderTarget.Viewport.Width,
                                             canvas.Camera.RenderTarget.Viewport.Height);
                    foreach (var uiEntity in canvas.UIEntities)
                    {
                        if (!uiEntity.IsActive)
                        {
                            continue;
                        }

                        if (uiEntity.TryGetComponent<UIRenderable>(out var renderable) &&
                            uiEntity.TryGetComponent<RectTransform>(out var rt) &&
                            renderable is not UIText)
                        {
                            var vertSlice = vertSpan.Slice(vIdx, 4);
                            renderable.WriteVertexData(vertSlice, canvasSize, rt);
                            vIdx += 4;

                            idxSpan[iIdx++] = baseVertex;
                            idxSpan[iIdx++] = baseVertex + 1;
                            idxSpan[iIdx++] = baseVertex + 2;
                            idxSpan[iIdx++] = baseVertex + 2;
                            idxSpan[iIdx++] = baseVertex + 3;
                            idxSpan[iIdx++] = baseVertex;
                            baseVertex += 4;
                        }
                    }
                }

                ImageIndexCount = (uint)(imageQuads * 6);
                TextFirstIndex = ImageIndexCount; // texts start after images

                // --- Texts second ---
                baseVertex = (uint)(imageQuads * 4);
                foreach (var canvas in _canvases)
                {
                    if (canvas.Camera == null)
                    {
                        continue;
                    }

                    Vector2 canvasSize = new(canvas.Camera.RenderTarget.Viewport.Width,
                                             canvas.Camera.RenderTarget.Viewport.Height);
                    foreach (var uiEntity in canvas.UIEntities)
                    {
                        if (!uiEntity.IsActive)
                        {
                            continue;
                        }

                        if (uiEntity.TryGetComponent<UIText>(out var text) &&
                            uiEntity.TryGetComponent<RectTransform>(out var rt))
                        {
                            int charCount = text.Text.Length;
                            var vertSlice = vertSpan.Slice(vIdx, charCount * 4);
                            text.WriteVertexData(vertSlice, canvasSize, rt);
                            vIdx += charCount * 4;

                            for (int q = 0; q < charCount; q++)
                            {
                                uint start = baseVertex + (uint)(q * 4);
                                idxSpan[iIdx++] = start;
                                idxSpan[iIdx++] = start + 1;
                                idxSpan[iIdx++] = start + 2;
                                idxSpan[iIdx++] = start + 2;
                                idxSpan[iIdx++] = start + 3;
                                idxSpan[iIdx++] = start;
                            }
                            baseVertex += (uint)(charCount * 4);
                        }
                    }
                }
                TextIndexCount = (uint)(textQuads * 6);
            }

            var batch = _vk.GraphicsSubmitContext.CreateBatch();
            stagingVert.CopyTo(_vertexBuffer!, batch, 0, 0);
            stagingIdx.CopyTo(_indexBuffer!, batch, 0, 0);
            batch.Submit();

            _vertexCount = (uint)vertexCount;
            _indexCount = (uint)indexCount;
        }

        public unsafe void RecordDrawCommands(
            UploadBatch batch,
            Material material,
            MaterialPass pass,
            uint frameIndex,
            Matrix4x4 projection,
            uint indexCount,
            uint firstIndex)
        {
            if (indexCount == 0 || _vertexBuffer == null || _indexBuffer == null)
            {
                return;
            }

            batch.BindPipeline(pass.Pipeline);
            _bindingManager.BindResourcesForMaterial(frameIndex, material, pass, batch);
            material.SetPushConstant("projection", projection);
            _indexBuffer.BindIndexBuffer(batch, 0, IndexType.Uint32);
            _vertexBuffer.BindVertexBuffer(batch);

            // Draw with firstIndex offset
            batch.DrawIndexed(indexCount, 1, firstIndex, 0, 0);
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
        }
    }
}