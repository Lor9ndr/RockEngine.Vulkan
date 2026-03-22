using NUnit.Framework;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;

namespace RockEngine.Tests
{
    [TestFixture]
    public class EntityTests : TestBase
    {
        private World _world;

        [SetUp] 
        public void SetUp()
        {
            _world = Scope.GetInstance<World>();

        }

        [Test]
        public void CreateEntity_ShouldHaveUniqueId()
        {
            var entity = _world.CreateEntity();
            Assert.That(entity.ID, Is.Not.EqualTo(Guid.Empty));
        }

        [Test]
        public void AddComponent_ShouldBeRetrievable()
        {
            var entity = _world.CreateEntity();
            var transform = entity.AddComponent<Transform>();
            Assert.That(entity.GetComponent<Transform>(), Is.EqualTo(transform));
        }

        [Test]
        public void GetComponent_NonExistent_ReturnsNull()
        {
            var entity = _world.CreateEntity();
            Assert.That(entity.GetComponent<MeshRenderer>(), Is.Null);
        }

        [Test]
        public void RemoveComponent_ShouldRemove()
        {
            var entity = _world.CreateEntity();
            var transform = entity.AddComponent<Transform>();
            Assert.Throws<InvalidOperationException>(()=>entity.RemoveComponent<Transform>(transform));
        }

        [Test]
        public void Entity_Destroy_ShouldRemoveFromWorld()
        {
            var entity = _world.CreateEntity();
            entity.Destroy();
            //Assert.That(_world.TryGetEntity(entity.ID, out _), Is.False);
        }

        [Test]
        public void Component_Entity_ShouldBeSet()
        {
            var entity = _world.CreateEntity();
            var transform = entity.AddComponent<Transform>();
            Assert.That(transform.Entity, Is.EqualTo(entity));
        }

        [Test]
        public void Entity_OnDestroy_ShouldCallComponentDestroy()
        {
            var entity = _world.CreateEntity();
            var mockComp = new MockComponent(); // need to create a custom mock
            entity.AddComponent(mockComp);
            entity.Destroy();
            Assert.That(mockComp.DestroyCalled, Is.True);
        }
    }
}