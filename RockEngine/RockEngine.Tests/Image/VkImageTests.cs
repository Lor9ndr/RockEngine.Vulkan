using NUnit.Framework;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Tests.Image
{
    [TestFixture]
    public class VkImageTests : TestBase
    {
        [Test]
        public void Create_SimpleColorImage_ShouldSucceed()
        {
            uint width = 256, height = 256;
            var format = Format.R8G8B8A8Unorm;
            var usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit;

            var image = VkImage.Create(
                _context,
                width, height,
                format,
                ImageTiling.Optimal,
                usage,
                MemoryPropertyFlags.DeviceLocalBit,
                ImageLayout.Undefined,
                mipLevels: 1,
                arrayLayers: 1,
                samples: SampleCountFlags.Count1Bit,
                aspectFlags: ImageAspectFlags.ColorBit);

            Assert.That(image.VkObjectNative.Handle, Is.Not.EqualTo(0));
            Assert.That(image.Extent.Width, Is.EqualTo(width));
            Assert.That(image.Extent.Height, Is.EqualTo(height));
            Assert.That(image.Format, Is.EqualTo(format));
            Assert.That(image.MipLevels, Is.EqualTo(1));
            Assert.That(image.ArrayLayers, Is.EqualTo(1));
            Assert.That(image.AspectFlags, Is.EqualTo(ImageAspectFlags.ColorBit));
            Assert.That(image.ImageMemory, Is.Not.Null);
            Assert.That(image.ImageMemory.VkObjectNative.Handle, Is.Not.EqualTo(0));
        }

        [Test]
        public void Create_DepthImage_ShouldSucceed()
        {
            uint width = 512, height = 512;
            var format = Format.D32Sfloat;
            var usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit;

            using var image = VkImage.Create(
                _context,
                width, height,
                format,
                ImageTiling.Optimal,
                usage,
                MemoryPropertyFlags.DeviceLocalBit,
                ImageLayout.Undefined,
                mipLevels: 1,
                arrayLayers: 1,
                samples: SampleCountFlags.Count1Bit,
                aspectFlags: ImageAspectFlags.DepthBit);

            Assert.That(image.AspectFlags, Is.EqualTo(ImageAspectFlags.DepthBit));
            Assert.That(image.Format, Is.EqualTo(format));
            Assert.That(image.Usage.HasFlag(ImageUsageFlags.DepthStencilAttachmentBit), Is.True);
        }

        [Test]
        public void Create_WithMipLevels_ShouldSucceed()
        {
            uint mipLevels = 4;
            var image = VkImage.Create(
                _context,
                128, 128,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                ImageLayout.Undefined,
                mipLevels: mipLevels);

            Assert.That(image.MipLevels, Is.EqualTo(mipLevels));
        }

        [Test]
        public void Create_ArrayLayers_ShouldSucceed()
        {
            uint arrayLayers = 6; // cube map
            var image = VkImage.Create(
                _context,
                64, 64,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                ImageLayout.Undefined,
                mipLevels: 1,
                arrayLayers: arrayLayers);

            Assert.That(image.ArrayLayers, Is.EqualTo(arrayLayers));
        }

        [Test]
        public void GetOrCreateView_SameParameters_ReturnsSameView()
        {
            var image = VkImage.Create(
                _context,
                128, 128,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                aspectFlags: ImageAspectFlags.ColorBit);

            var view1 = image.GetOrCreateView(ImageAspectFlags.ColorBit);
            var view2 = image.GetOrCreateView(ImageAspectFlags.ColorBit);

            Assert.That(view1, Is.SameAs(view2));
            view1.Dispose();
            // view2 is the same object, already disposed, so we don't double dispose.
        }

        [Test]
        public void GetOrCreateView_DifferentParameters_CreatesDifferentViews()
        {
            var image = VkImage.Create(
                _context,
                128, 128,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                mipLevels: 2,
                aspectFlags: ImageAspectFlags.ColorBit);

            var view1 = image.GetOrCreateView(ImageAspectFlags.ColorBit, baseMipLevel: 0, levelCount: 1);
            var view2 = image.GetOrCreateView(ImageAspectFlags.ColorBit, baseMipLevel: 0, levelCount: 2, baseArrayLayer: 0, layerCount: 1);

            Assert.That(view1, Is.Not.SameAs(view2));
            view1.Dispose();
            view2.Dispose();
        }

        [Test]
        public void GetView_ConvenienceMethod_ReturnsCachedView()
        {
            var image = VkImage.Create(
                _context,
                128, 128,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                aspectFlags: ImageAspectFlags.ColorBit);

            var view1 = image.GetView();
            var view2 = image.GetView();

            Assert.That(view1, Is.SameAs(view2));
            view1.Dispose();
        }

        [Test]
        public void Resize_ChangesExtent_AndNotifiesObservers()
        {
            var image = VkImage.Create(
                _context,
                64, 64,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.ColorAttachmentBit,
                MemoryPropertyFlags.DeviceLocalBit,
                aspectFlags: ImageAspectFlags.ColorBit);

            bool notified = false;
            using var subscription = image.Subscribe(new DelegateObserver(_ => notified = true));

            var newExtent = new Extent3D(128, 128, 1);
            image.Resize(newExtent);

            Assert.That(image.Extent.Width, Is.EqualTo(newExtent.Width));
            Assert.That(image.Extent.Height, Is.EqualTo(newExtent.Height));
            Assert.That(notified, Is.True);
        }

        [Test]
        public void Resize_ArrayLayers_UpdatesLayerCount()
        {
            var image = VkImage.Create(
                _context,
                32, 32,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit | ImageUsageFlags.ColorAttachmentBit,
                MemoryPropertyFlags.DeviceLocalBit,
                arrayLayers: 2,
                aspectFlags: ImageAspectFlags.ColorBit);

            image.Resize(new Extent3D(64, 64, 1), newArrayLayers: 4);

            Assert.That(image.ArrayLayers, Is.EqualTo(4));
        }

        [Test]
        public async Task GenerateMipmaps_OnSupportedFormat_DoesNotThrow()
        {
            // Check format blit support
            var format = Format.R8G8B8A8Unorm;
            var formatProps = _context.Device.PhysicalDevice.GetFormatProperties(format);
            bool blitSupported = (formatProps.OptimalTilingFeatures & FormatFeatureFlags.BlitSrcBit) != 0 &&
                                 (formatProps.OptimalTilingFeatures & FormatFeatureFlags.BlitDstBit) != 0;
            if (!blitSupported)
            {
                Assert.Ignore("Format does not support blitting for mipmap generation");
            }

            var image = VkImage.Create(
                _context,
                256, 256,
                format,
                ImageTiling.Optimal,
                ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                mipLevels: 4,
                aspectFlags: ImageAspectFlags.ColorBit);
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            // Transition to TransferDstOptimal for first mip (needed for blit destination)
            image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            bool result = image.GenerateMipmaps(batch);
            batch.Submit();
            await batch.SubmitContext.Submit();
            Assert.That(result, Is.True);
        }

        [Test]
        public async Task TransitionImageLayout_WithQueueFamilyTransfer_DoesNotThrow()
        {
            // This test uses dummy queue families; in a real scenario, you'd have separate queues.
            // For simplicity, we just ensure the method handles queue family indices.
            var image = VkImage.Create(
                _context,
                64, 64,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                aspectFlags: ImageAspectFlags.ColorBit);
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            Assert.DoesNotThrow(() =>
            {
                image.TransitionImageLayout(
                    batch,
                    ImageLayout.Undefined,
                    ImageLayout.ShaderReadOnlyOptimal,
                    srcQueueFamilyIndex: 0,
                    dstQueueFamilyIndex: 1);
            });
            batch.Submit();
            await batch.SubmitContext.Submit();


        }

        [Test]
        public void GetMipView_ReturnsViewForSpecificMip()
        {
            var image = VkImage.Create(
                _context,
                64, 64,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                mipLevels: 3,
                aspectFlags: ImageAspectFlags.ColorBit);

            var view = image.GetMipView(1);
            Assert.That(view.BaseMipLevel, Is.EqualTo(1));
            Assert.That(view.LevelCount, Is.EqualTo(1));
            view.Dispose();
        }

        [Test]
        public void Dispose_ReleasesVulkanResources()
        {
            var image = VkImage.Create(
                _context,
                128, 128,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit);

            var handle = image.VkObjectNative.Handle;
            Assert.That(handle, Is.Not.EqualTo(0));

            image.Dispose();

            // ObjectDisposedException on operations after dispose
            Assert.Throws<ObjectDisposedException>(() => image.GetView());
        }

        private class DelegateObserver(Action<ulong> onChanged) : IResourceObserver
        {
            public void OnResourceChanged(ulong resourceId, ResourceChangeType changeType) => onChanged(resourceId);
        }
    }
}