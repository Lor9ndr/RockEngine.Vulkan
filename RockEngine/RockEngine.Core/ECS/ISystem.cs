namespace RockEngine.Core.ECS
{
    public interface ISystem
    {
        ValueTask Update(World world, float deltaTime);
        int Priority { get; } // Execution order
    }
}
