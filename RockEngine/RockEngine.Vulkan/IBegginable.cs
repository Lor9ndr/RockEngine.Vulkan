namespace RockEngine.Vulkan
{
    internal interface IBegginable<T>
    {
        public void Begin(in T beginInfo);
        public void Begin(in T beginInfo, Action untilEndAction);

        public void End();

    }
}
