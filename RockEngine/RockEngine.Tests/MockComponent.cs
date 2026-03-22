using RockEngine.Core.ECS.Components;

namespace RockEngine.Tests
{
    // Helper mock component
    public class MockComponent : Component
    {
        public bool DestroyCalled { get; private set; }
        public override void Destroy() => DestroyCalled = true;
    }
}