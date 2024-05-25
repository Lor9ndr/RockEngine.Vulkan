namespace RockEngine.Vulkan.VkObjects
{
    internal interface IBegginable<T>
    {
        public void Begin(T beginInfo);

        public void End();

    }
}
