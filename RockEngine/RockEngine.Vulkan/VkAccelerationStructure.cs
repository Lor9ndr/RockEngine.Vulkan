using RockEngine.Vulkan.Extensions;

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace RockEngine.Vulkan
{
    public unsafe class VkAccelerationStructure : VkObject<AccelerationStructureKHR>
    {
        private readonly VulkanContext _context;
        private readonly VkBuffer _buffer; // backing buffer
        private readonly AccelerationStructureTypeKHR _type;

        private VkAccelerationStructure(VulkanContext context, in AccelerationStructureKHR accel, VkBuffer buffer, AccelerationStructureTypeKHR type)
            : base(accel)
        {
            _context = context;
            _buffer = buffer;
            _type = type;
        }

      
        public static VkAccelerationStructure CreateBLAS(
            VulkanContext context,
            VkBuffer vertexBuffer, ulong vertexOffset, uint vertexCount, uint vertexStride,
            VkBuffer indexBuffer, ulong indexOffset, uint indexCount,
            VkBuffer? transformBuffer = null, ulong transformOffset = 0,
            GeometryFlagsKHR flags = GeometryFlagsKHR.OpaqueBitKhr,
            BuildAccelerationStructureFlagsKHR buildFlags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr)
        {
            var device = context.Device;

            // Build geometry info
            var triangles = new AccelerationStructureGeometryTrianglesDataKHR
            {
                SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
                VertexFormat = Format.R32G32B32Sfloat,
                VertexData = new DeviceOrHostAddressConstKHR
                {
                    DeviceAddress = vertexBuffer.GetDeviceAddress() + vertexOffset
                },
                VertexStride = vertexStride,
                MaxVertex = vertexCount - 1,
                IndexType = IndexType.Uint32,
                IndexData = new DeviceOrHostAddressConstKHR
                {
                    DeviceAddress = indexBuffer.GetDeviceAddress() + indexOffset
                },
                TransformData = new DeviceOrHostAddressConstKHR
                {
                    DeviceAddress = transformBuffer?.GetDeviceAddress() + transformOffset ?? 0
                }
            };

            var geometry = new AccelerationStructureGeometryKHR
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                GeometryType = GeometryTypeKHR.TrianglesKhr,
                Geometry = new AccelerationStructureGeometryDataKHR
                {
                    Triangles = triangles
                },
                Flags = flags
            };

            uint primitiveCount = indexCount / 3;
            var buildInfo = new AccelerationStructureBuildGeometryInfoKHR
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = buildFlags,
                GeometryCount = 1,
                PGeometries = &geometry
            };

            // Get size info
            var khrAccel = new KhrAccelerationStructure(VulkanContext.Vk.Context);
            khrAccel.GetAccelerationStructureBuildSizes(
                device,
                 AccelerationStructureBuildTypeKHR.DeviceKhr,
                in buildInfo,
                in primitiveCount,
                out var sizeInfo);

            // Create buffer for the acceleration structure
            var buffer = VkBuffer.Create(
                context,
                sizeInfo.AccelerationStructureSize,
                BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit,
                MemoryPropertyFlags.DeviceLocalBit);

            // Create acceleration structure object
            var createInfo = new AccelerationStructureCreateInfoKHR
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = buffer,
                Size = sizeInfo.AccelerationStructureSize,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr
            };
            khrAccel.CreateAccelerationStructure(device, ref createInfo, null, out var accel);

            return new VkAccelerationStructure(context, accel, buffer, AccelerationStructureTypeKHR.BottomLevelKhr);
        }

        public static VkAccelerationStructure CreateTLAS(
            VulkanContext context,
            VkBuffer instancesBuffer, uint instanceCount,
            BuildAccelerationStructureFlagsKHR buildFlags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr)
        {
            var device = context.Device;

            var instances = new AccelerationStructureGeometryInstancesDataKHR
            {
                SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                ArrayOfPointers = false,
                Data = new DeviceOrHostAddressConstKHR
                {
                    DeviceAddress = instancesBuffer.GetDeviceAddress()
                }
            };

            var geometry = new AccelerationStructureGeometryKHR
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                GeometryType = GeometryTypeKHR.InstancesKhr,
                Geometry = new AccelerationStructureGeometryDataKHR
                {
                    Instances = instances
                },
                Flags = GeometryFlagsKHR.OpaqueBitKhr
            };

            uint primitiveCount = instanceCount;
            var buildInfo = new AccelerationStructureBuildGeometryInfoKHR
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.TopLevelKhr,
                Flags = buildFlags,
                GeometryCount = 1,
                PGeometries = &geometry
            };

            var khrAccel = new KhrAccelerationStructure(VulkanContext.Vk.Context); ;
            khrAccel.GetAccelerationStructureBuildSizes(
                device,
                 AccelerationStructureBuildTypeKHR.DeviceKhr,
                ref buildInfo,
                ref primitiveCount,
                out var sizeInfo
                );

            var buffer = VkBuffer.Create(
                context,
                sizeInfo.AccelerationStructureSize,
                BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit,
                MemoryPropertyFlags.DeviceLocalBit);

            var createInfo = new AccelerationStructureCreateInfoKHR
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = buffer,
                Size = sizeInfo.AccelerationStructureSize,
                Type = AccelerationStructureTypeKHR.TopLevelKhr
            };
            khrAccel.CreateAccelerationStructure(device, ref createInfo, null, out var accel);

            return new VkAccelerationStructure(context, accel, buffer, AccelerationStructureTypeKHR.TopLevelKhr);
        }

        // --------------------------------------------------------------------
        // Build (update) the acceleration structure
        // --------------------------------------------------------------------
        public void Build(
            VkCommandBuffer cmd,
            VkBuffer scratchBuffer,
            VkBuffer? instancesBuffer = null,
            uint instanceCount = 0,
            VkBuffer? vertexBuffer = null, ulong vertexOffset = 0, uint vertexStride = 0, uint vertexCount = 0,
            VkBuffer? indexBuffer = null, ulong indexOffset = 0, uint indexCount = 0,
            VkBuffer? transformBuffer = null, ulong transformOffset = 0)
        {
            var khrAccel = new KhrAccelerationStructure(VulkanContext.Vk.Context);

            // Prepare geometry info based on type
            AccelerationStructureGeometryKHR geometry;
            uint primitiveCount;

            if (_type == AccelerationStructureTypeKHR.BottomLevelKhr)
            {
                var triangles = new AccelerationStructureGeometryTrianglesDataKHR
                {
                    SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
                    VertexFormat = Format.R32G32B32Sfloat,
                    VertexData = new DeviceOrHostAddressConstKHR
                    {
                        DeviceAddress = vertexBuffer!.GetDeviceAddress() + vertexOffset
                    },
                    VertexStride = vertexStride,
                    MaxVertex = vertexCount - 1,
                    IndexType = IndexType.Uint32,
                    IndexData = new DeviceOrHostAddressConstKHR
                    {
                        DeviceAddress = indexBuffer!.GetDeviceAddress() + indexOffset
                    },
                    TransformData = new DeviceOrHostAddressConstKHR
                    {
                        DeviceAddress = transformBuffer?.GetDeviceAddress() + transformOffset ?? 0
                    }
                };
                geometry = new AccelerationStructureGeometryKHR
                {
                    SType = StructureType.AccelerationStructureGeometryKhr,
                    GeometryType = GeometryTypeKHR.TrianglesKhr,
                    Geometry = new AccelerationStructureGeometryDataKHR { Triangles = triangles },
                    Flags = GeometryFlagsKHR.OpaqueBitKhr
                };
                primitiveCount = indexCount / 3;
            }
            else // TopLevel
            {
                var instances = new AccelerationStructureGeometryInstancesDataKHR
                {
                    SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                    ArrayOfPointers = false,
                    Data = new DeviceOrHostAddressConstKHR
                    {
                        DeviceAddress = instancesBuffer!.GetDeviceAddress()
                    }
                };
                geometry = new AccelerationStructureGeometryKHR
                {
                    SType = StructureType.AccelerationStructureGeometryKhr,
                    GeometryType = GeometryTypeKHR.InstancesKhr,
                    Geometry = new AccelerationStructureGeometryDataKHR { Instances = instances },
                    Flags = GeometryFlagsKHR.OpaqueBitKhr
                };
                primitiveCount = instanceCount;
            }

            var buildInfo = new AccelerationStructureBuildGeometryInfoKHR
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = _type,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
                DstAccelerationStructure = _vkObject,
                GeometryCount = 1,
                PGeometries = &geometry,
                ScratchData = new DeviceOrHostAddressKHR
                {
                    DeviceAddress = scratchBuffer.GetDeviceAddress()
                }
            };

            var buildRange = new AccelerationStructureBuildRangeInfoKHR
            {
                PrimitiveCount = primitiveCount,
                PrimitiveOffset = 0,
                FirstVertex = 0,
                TransformOffset = 0
            };
            var pBuildRange = &buildRange;

            khrAccel.CmdBuildAccelerationStructures(cmd, 1, ref buildInfo, ref pBuildRange);
        }

        // --------------------------------------------------------------------
        // Device address (for shaders)
        // --------------------------------------------------------------------
        public ulong GetDeviceAddress()
        {
            var info = new AccelerationStructureDeviceAddressInfoKHR
            {
                SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
                AccelerationStructure = _vkObject
            };
            var khrAccel = new KhrAccelerationStructure(VulkanContext.Vk.Context);
            return khrAccel.GetAccelerationStructureDeviceAddress(_context.Device, ref info);
        }

        // --------------------------------------------------------------------
        // Cleanup
        // --------------------------------------------------------------------
        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _buffer?.Dispose();
            }

            var khrAccel = new KhrAccelerationStructure(VulkanContext.Vk.Context);
            khrAccel.DestroyAccelerationStructure(_context.Device, _vkObject, null);

            _disposed = true;
        }

        public override void LabelObject(string name)
        {
            _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.AccelerationStructureKhr, name);
            _buffer?.LabelObject(name + "_Buffer");
        }
    }
}