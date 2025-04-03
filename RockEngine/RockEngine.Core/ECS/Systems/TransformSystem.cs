namespace RockEngine.Core.ECS.Systems
{
    public sealed class TransformSystem : ISystem
    {
        public int Priority => 100;
        private readonly UniformBuffer _transformBuffer;

        public async ValueTask Update(World world, float deltaTime)
        {
            /*// Инициализация буфера при первом обновлении
            _transformBuffer ??= new UniformBuffer(
                    VulkanContext.GetCurrent(),
                    "ModelData",
                    0,
                    (ulong)(1024 * 1024 * Marshal.SizeOf<Matrix4x4>()),
                    Marshal.SizeOf<Matrix4x4>(),
                    true
                );

            // Обновление всех трансформаций
            int index = 0;
            foreach (var transform in world.GetComponents<Transform>())
            {
                var matrix = transform.GetModelMatrix();
                await _transformBuffer.UpdateAsync(matrix, (ulong)_transformBuffer.DataSize, (ulong)(index * Marshal.SizeOf<Matrix4x4>()));
                index++;
            }*/
        }

    }
}
