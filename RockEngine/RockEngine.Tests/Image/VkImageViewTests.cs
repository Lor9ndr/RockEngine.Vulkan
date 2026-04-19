using NUnit.Framework;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Tests.Image
{
    [TestFixture]
    public class VkImageViewTests : TestBase
    {
        private VkImage? _testImage;

        [SetUp]
        public void Setup()
        {
            _testImage = VkImage.Create(
                _context,
                256, 256,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                aspectFlags: ImageAspectFlags.ColorBit);
        }

        [TearDown]
        public void TearDown()
        {
            _testImage?.Dispose();
        }

        [Test]
        public void Create_FromImage_ShouldSucceed()
        {
            using var view = VkImageView.Create(
                _context,
                _testImage,
                Format.R8G8B8A8Unorm,
                ImageAspectFlags.ColorBit);

            Assert.That(view.VkObjectNative.Handle, Is.Not.EqualTo(0));
            Assert.That(view.Image, Is.SameAs(_testImage));
            Assert.That(view.Format, Is.EqualTo(Format.R8G8B8A8Unorm));
            Assert.That(view.AspectFlags, Is.EqualTo(ImageAspectFlags.ColorBit));
        }

        [Test]
        public void Create_WithCustomSubresource_ShouldSetProperties()
        {
            // Create an image with sufficient mip levels to support the view's subresource range.
            using var image = VkImage.Create(
                _context,
                256, 256,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                mipLevels: 3,                     // Enough for baseMipLevel 1 + levelCount 2
                aspectFlags: ImageAspectFlags.ColorBit);

            using var view = VkImageView.Create(
                _context,
                image,
                Format.R8G8B8A8Unorm,
                ImageAspectFlags.ColorBit,
                ImageViewType.Type2D,
                baseMipLevel: 1,
                levelCount: 2,
                baseArrayLayer: 0,
                arrayLayers: 1);

            Assert.That(view.BaseMipLevel, Is.EqualTo(1));
            Assert.That(view.LevelCount, Is.EqualTo(2));
            Assert.That(view.BaseArrayLayer, Is.EqualTo(0));
            Assert.That(view.LayerCount, Is.EqualTo(1));
        }
        [Test]
        public void View_ReactsToImageResize_RecreatesHandle()
        {
            // Create an image with ColorAttachmentBit to allow TransitionToDefaultLayout during Resize
            using var image = VkImage.Create(
                _context,
                256, 256,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.SampledBit | ImageUsageFlags.ColorAttachmentBit,
                MemoryPropertyFlags.DeviceLocalBit,
                aspectFlags: ImageAspectFlags.ColorBit);

            using var view = VkImageView.Create(
                _context,
                image,
                Format.R8G8B8A8Unorm,
                ImageAspectFlags.ColorBit);

            var originalHandle = view.VkObjectNative.Handle;

            // Resize the image; view should automatically recreate itself
            image.Resize(new Extent3D(512, 512, 1));

            Assert.That(view.VkObjectNative.Handle, Is.Not.EqualTo(originalHandle));
            Assert.That(view.VkObjectNative.Handle, Is.Not.EqualTo(0));
        }

        [Test]
        public void Dispose_RemovesFromImageCache()
        {
            var view = _testImage.GetOrCreateView(ImageAspectFlags.ColorBit);
            var cachedView = _testImage.GetOrCreateView(ImageAspectFlags.ColorBit);
            Assert.That(cachedView, Is.SameAs(view));

            view.Dispose();

            var newView = _testImage.GetOrCreateView(ImageAspectFlags.ColorBit);
            Assert.That(newView, Is.Not.SameAs(view));
            newView.Dispose();
        }

        [Test]
        public void Subscribe_ObserverNotifiedOnUpdate()
        {
            using var view = VkImageView.Create(
                _context,
                _testImage,
                Format.R8G8B8A8Unorm,
                ImageAspectFlags.ColorBit);

            bool notified = false;
            using var subscription = view.Subscribe(new DelegateObserver(_ => notified = true));

            view.Update();

            Assert.That(notified, Is.True);
        }

        private class DelegateObserver(Action<ulong> onChanged) : IResourceObserver
        {
            public void OnResourceChanged(ulong resourceId, ResourceChangeType changeType) => onChanged(resourceId);
        }
    }
}