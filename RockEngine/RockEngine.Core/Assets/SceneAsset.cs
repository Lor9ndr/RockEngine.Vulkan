// Updated SceneAsset.cs
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NLog;

using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;

using System.Collections.Concurrent;

using ZLinq;

namespace RockEngine.Core.Assets
{
    public sealed class SceneAsset : Asset<SceneData>
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [JsonIgnore]
        public ConcurrentDictionary<ulong, Entity> Entities { get; private set; } = new();

        public override string Type => "Scene";

        [JsonIgnore]
        public bool IsLoaded { get; private set; }

        public Entity CreateEntity(string name = null, Entity parent = null)
        {
            var entity = World.GetCurrent().CreateEntity(name);
            Entities[entity.ID] = entity;

            if (parent != null)
            {
                parent.AddChild(entity);
            }

            return entity;
        }

        public void RemoveEntity(Entity entity)
        {
            Entities.TryRemove(entity.ID, out _);
            World.GetCurrent().RemoveEntity(entity);
        }

        public void Unload()
        {
            if (!IsLoaded)
            {
                return;
            }

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
            var serializer = IoC.Container.GetInstance<IAssetSerializer>();

            // Convert entities to serializable data, starting from root entities
            var rootEntities = Entities.Values.Where(e => e.Parent == null).ToList();

            Data = new SceneData
            {
                Entities = SerializeEntityHierarchy(rootEntities, serializer)
            };
        }

        private List<SceneEntityData> SerializeEntityHierarchy(List<Entity> entities, IAssetSerializer serializer)
        {
            var result = new List<SceneEntityData>();

            foreach (var entity in entities)
            {
                var entityData = new SceneEntityData
                {
                    ID = entity.ID,
                    Name = entity.Name,
                    ParentID = entity.Parent?.ID,
                    RenderLayerType = entity.Layer,
                    Components = entity.Components.AsValueEnumerable().Select(comp =>
                    {
                        try
                        {
                            return new SceneComponentData
                            {
                                TypeName = comp.GetType().AssemblyQualifiedName,
                                Data = JObject.FromObject(comp, serializer.Serializer)
                            };
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, $"Failed to serialize component {comp.GetType().Name} for entity {entity.Name}");
                            return null;
                        }
                    }).Where(c => c!= null).ToList()
                };

                result.Add(entityData);

                // Recursively serialize children
                if (entity.Children.Count > 0)
                {
                    result.AddRange(SerializeEntityHierarchy(entity.Children.ToList(), serializer));
                }
            }

            return result;
        }

        public override void AfterSaving()
        {
            // Optional: Clear data after saving to free memory
            // Data = null;
        }

        public void InstantiateEntities()
        {
            if (Data == null || IsLoaded)
            {
                return;
            }

            try
            {
                var serializer = IoC.Container.GetInstance<IAssetSerializer>();
                var entityMap = new Dictionary<ulong, Entity>();

                // First pass: create all entities without setting hierarchy
                foreach (var entityData in Data.Entities)
                {
                    var entity = CreateEntity(entityData.Name);
                    entityMap[entityData.ID] = entity;
                    entity.Layer = entityData.RenderLayerType;

                    foreach (var componentData in entityData.Components)
                    {
                        var componentType = System.Type.GetType(componentData.TypeName);
                        if (componentType != null && typeof(IComponent).IsAssignableFrom(componentType))
                        {
                            try
                            {
                                var component = (IComponent)componentData.Data.ToObject(componentType, serializer.Serializer);
                                if (component is Transform componentTransform)
                                {
                                    entity.Transform.Position = componentTransform.Position;
                                    entity.Transform.Rotation = componentTransform.Rotation;
                                    entity.Transform.Scale = componentTransform.Scale;
                                }
                                entity.AddComponent(component);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to deserialize component of type {Type} for entity: {Name}",
                                    componentType.Name, entity.Name);
                            }
                        }
                    }
                }

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
                _logger.Debug($"Instantiated {Entities.Count} entities for scene: {Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to instantiate entities for scene: {Name}");
                throw;
            }
        }
    }

    public class SceneData
    {
        public List<SceneEntityData> Entities { get; set; } = new List<SceneEntityData>();
    }

    public class SceneEntityData
    {
        public ulong ID { get; set; }
        public string Name { get; set; } = string.Empty;
        public ulong? ParentID { get; set; }
        public RenderLayer RenderLayerType { get; set; }
        public List<SceneComponentData> Components { get; set; } = new List<SceneComponentData>();
    }

    public class SceneComponentData
    {
        public string TypeName { get; set; } = string.Empty;
        public JObject Data { get; set; }
    }
}