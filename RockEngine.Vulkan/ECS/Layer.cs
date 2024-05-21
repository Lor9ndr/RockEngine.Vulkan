namespace RockEngine.Vulkan.ECS
{
    internal class Layer
    {
        private readonly List<Scene> _scenes = new List<Scene>();

        public void AddScene(Scene scene)
        {
            _scenes.Add(scene);
        }

        public async Task Update()
        {
            foreach (var scene in _scenes)
            {
                await scene.Update();
            }
        }
    }
}
