using NUnit.Framework;
using RockEngine.Vulkan;
using SimpleInjector;

namespace RockEngine.Tests
{
    [TestFixture]
    public abstract class TestBase
    {
        protected Scope Scope => GlobalTestSetup.Scope;
        protected VulkanContext _context => GlobalTestSetup.VulkanContext;
    }
}