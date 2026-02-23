using MessagePack;

using NLog;

using RockEngine.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;

using System.Collections.Concurrent;

using ZLinq;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public sealed class SceneAsset : Asset<SceneData>, IGpuResource
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [IgnoreMember]
        public ConcurrentDictionary<ulong, Entity> Entities { get; private set; } = new();

        public override string Type => "Scene";

        [IgnoreMember]
        public bool IsLoaded { get; private set; }

        [IgnoreMember]
        public bool GpuReady => Entities.All(e =>
            !e.Value.HasComponent<MeshRenderer>() ||
            e.Value.GetComponent<MeshRenderer>()?.Mesh == null);

        public Entity CreateEntity(string name = null, Entity parent = null)
        {
            var entity = World.GetCurrent().CreateEntity(name);
            Entities[entity.ID] = entity;

            parent?.AddChild(entity);

            return entity;
        }

        public void RemoveEntity(Entity entity)
        {
            Entities.TryRemove(entity.ID, out _);
            World.GetCurrent().RemoveEntity(entity);
        }

        public async ValueTask LoadGpuResourcesAsync()
        {
            // Load GPU resources for all entities with renderable components
            foreach (var entity in Entities.Values)
            {
                if (entity.TryGetComponent<MeshRenderer>(out var renderer))
                {
                    if (renderer.Mesh != null)
                    {
                        var model = await renderer.MeshProvider.GetAsync();
                        if (model is IGpuResource gpuModel)
                        {
                            await gpuModel.LoadGpuResourcesAsync();
                        }
                    }

                    if (renderer.Material != null)
                    {
                        var material = await renderer.MaterialProvider.GetAsync();
                        if (material is IGpuResource gpuMaterial)
                        {
                            await gpuMaterial.LoadGpuResourcesAsync();
                        }
                    }
                }
            }
        }

        public void UnloadGpuResources()
        {
            // Note: Don't unload GPU resources here as they might be shared
            // Only unload scene-specific resources
        }

        public void Unload()
        {
            if (!IsLoaded) return;

            try
            {
                // Remove all entities from the world
                foreach (var entity in Entities.Values)
                {
                    World.GetCurrent().RemoveEntity(entity);
                }

                Entities.Clear();
                IsLoaded = false;

                _logger.Debug($"Unloaded scene: {Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to unload scene: {Name}");
            }
        }

        public override void BeforeSaving()
        {
            Data = new SceneData
            {
                Entities = Entities.Values.ToList()
            };
        }


        public async Task InstantiateEntities()
        {
            if (Data == null || IsLoaded) return;

            try
            {
                var entityMap = new ConcurrentDictionary<ulong, Entity>();

                // First pass: create all entities
                await Parallel.ForEachAsync(Data.Entities, async (entityData, ct) =>
                {
                    var entity = CreateEntity(entityData.Name);
                    entityMap[entityData.ID] = entity;
                    entity.Layer = entityData.Layer;

                    // Apply transform
                    entity.Transform.Position = entityData.Transform.Position;
                    entity.Transform.Rotation = entityData.Transform.Rotation;
                    entity.Transform.Scale = entityData.Transform.Scale;

                    // Deserialize components
                    foreach (var componentData in entityData.Components)
                    {

                        entity.AddComponent(componentData);

                        // Special handling for MeshRendererComponent
                        if (componentData is MeshRenderer meshRenderer)
                        {
                            // Ensure materials are loaded
                            if (meshRenderer.Material != null)
                            {
                                await meshRenderer.MaterialProvider.GetAsync();
                            }

                            // Ensure model is loaded
                            if (meshRenderer.Mesh != null)
                            {
                                await meshRenderer.MeshProvider.GetAsync();
                            }
                        }

                    }
                });

                // Second pass: establish parent-child relationships
                foreach (var entityData in Data.Entities)
                {
                    if (entityData.ParentID.HasValue &&
                        entityMap.TryGetValue(entityData.ParentID.Value, out var parent) &&
                        entityMap.TryGetValue(entityData.ID, out var child))
                    {
                        parent.AddChild(child);
                    }
                }

                IsLoaded = true;
                Data = null; // Free memory
                _logger.Debug($"Instantiated {Entities.Count} entities for scene: {Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to instantiate entities for scene: {Name}");
                throw;
            }
        }
    }
}