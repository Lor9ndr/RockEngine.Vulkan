namespace RockEngine.Vulkan.ECS
{
    public abstract class System
    {
        public async Task Update(List<Entity> entities)
        {
            foreach(var item in entities)
            {
                await item.Update();
            }
        }
    }
}
