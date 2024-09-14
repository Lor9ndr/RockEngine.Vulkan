namespace RockEngine.Vulkan
{
    internal interface IBegginable<T>
    {
        public void Begin(ref T beginInfo);
        public void Begin(ref T beginInfo, Action untilEndAction);

        public void End();

    }
}
