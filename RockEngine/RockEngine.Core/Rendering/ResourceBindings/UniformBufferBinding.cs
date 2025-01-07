namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class UniformBufferBinding : ResourceBinding
    {
        public UniformBuffer Buffer { get; }
        public uint BindingLocation { get; }

        public UniformBufferBinding(UniformBuffer buffer, uint bindingLocation, uint setLocation):base(setLocation)
        {
            Buffer = buffer;
            BindingLocation = bindingLocation;
        }

    }
}
