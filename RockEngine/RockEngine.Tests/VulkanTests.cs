using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RockEngine.Vulkan;
using RockEngine.Vulkan.DeviceFeatures;
using Silk.NET.Vulkan;

namespace RockEngine.Tests
{
    [TestFixture]
    public class VulkanHeadlessTests
    {
        private VulkanContext _context;
        private AppSettings _settings;
        private FeatureRegistry _featureRegistry;

        [OneTimeSetUp]
        public void Setup()
        {
            // Minimal settings for headless operation
            _settings = new AppSettings
            {
                Name = "TestApp",
                EnableValidationLayers = true,
                MaxFramesPerFlight = 2
            };

            _featureRegistry = new FeatureRegistry();
            // Register required features (same as CoreModule)
            _featureRegistry.RequestFeature(new SamplerAnisotropyFeature() { IsRequired = true });
            _featureRegistry.RequestFeature(new MultiDrawIndirectFeature() { IsRequired = true });
            _featureRegistry.RequestFeature(new Synchronization2Feature() { IsRequired = true });

            // Create headless Vulkan context (no window)
            _context = new VulkanContext(null,_settings, _featureRegistry);
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            _context?.Dispose();
        }

        [Test]
        public void CreateHeadlessContext_ShouldSucceed()
        {
            Assert.That(_context, Is.Not.Null);
            Assert.That(_context.Instance.VkObjectNative, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(_context.Device.VkObjectNative, Is.Not.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void Queues_ShouldBeValid()
        {
            Assert.That(_context.Device.GraphicsQueue, Is.Not.Null);
            Assert.That(_context.Device.ComputeQueue, Is.Not.Null);
            Assert.That(_context.Device.TransferQueue, Is.Not.Null);
            // Present queue should be null because no surface
            Assert.That(_context.Device.PresentQueue, Is.Null);
        }

        [Test]
        public void DeviceFeatures_ShouldBeEnabled()
        {
            var physicalDevice = _context.Device.PhysicalDevice;
            var features = VulkanContext.Vk.GetPhysicalDeviceFeatures(physicalDevice);

            Assert.AreEqual(features.SamplerAnisotropy, true);
            // Check that requested features are present (others may be false)
        }

        [Test]
        public unsafe void CreateBufferAndUploadData_ShouldSucceed()
        {
            var vk = VulkanContext.Vk;
            var device = _context.Device;
            var physicalDevice = device.PhysicalDevice;

            var data = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
            var bufferSize = (ulong)(data.Length * sizeof(float));

            var memoryProperties = vk.GetPhysicalDeviceMemoryProperties(physicalDevice);

            var bufferCreateInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = bufferSize,
                Usage = BufferUsageFlags.TransferSrcBit,
                SharingMode = SharingMode.Exclusive
            };

            vk.CreateBuffer(device, in bufferCreateInfo, null, out Silk.NET.Vulkan.Buffer buffer)
                .VkAssertResult();

            vk.GetBufferMemoryRequirements(device, buffer, out MemoryRequirements memReqs);

            uint memoryTypeIndex = uint.MaxValue;
            for (uint i = 0; i < memoryProperties.MemoryTypeCount; i++)
            {
                if ((memReqs.MemoryTypeBits & (1 << (int)i)) != 0 &&
                    (memoryProperties.MemoryTypes[(int)i].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) == MemoryPropertyFlags.HostVisibleBit)
                {
                    memoryTypeIndex = i;
                    break;
                }
            }
            Assert.That(memoryTypeIndex, Is.Not.EqualTo(uint.MaxValue));

            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReqs.Size,
                MemoryTypeIndex = memoryTypeIndex
            };

            // Use default allocator (null) for simplicity and to avoid allocator mismatch
            vk.AllocateMemory(device, in allocInfo, null, out DeviceMemory memory)
                .VkAssertResult();

            vk.BindBufferMemory(device, buffer, memory, 0).VkAssertResult();

            // Map memory and copy data
            void* mappedData;
            vk.MapMemory(device, memory, 0, bufferSize, 0, &mappedData).VkAssertResult();
            Marshal.Copy(data, 0, (IntPtr)mappedData, data.Length);
            vk.UnmapMemory(device, memory);

            // Cleanup with default allocator (null)
            vk.DestroyBuffer(device, buffer, null);
            vk.FreeMemory(device, memory, null);

            Assert.Pass("Buffer created and data uploaded successfully.");
        }
    }
}