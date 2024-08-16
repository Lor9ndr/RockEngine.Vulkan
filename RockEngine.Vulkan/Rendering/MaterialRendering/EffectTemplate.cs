namespace RockEngine.Vulkan.Rendering.MaterialRendering
{
    public class EffectTemplate
    {
        public PerPassData<ShaderPass> PassShaders = new PerPassData<ShaderPass>();
        public Dictionary<string, object> DefaultParameters = new Dictionary<string, object>();

        public void AddShaderPass(MeshpassType passType,  ShaderPass pass)
        {
            PassShaders[passType] = pass;
        }
    }
}
