using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Core.Rendering.Materials
{
    public abstract class MaterialProperty
    {
        public string Name { get; protected set; }
        public abstract void Apply(MaterialPass pass);
    }
    public class TextureProperty : MaterialProperty
    {
        public Texture Texture { get; set; }
        public uint Binding { get; }

        public TextureProperty(string name, uint binding, Texture defaultTexture = null)
        {
            Name = name;
            Binding = binding;
            Texture = defaultTexture;
        }

        public override void Apply(MaterialPass pass)
        {
            if (Texture != null) { }
                //pass.BindResource(2, Binding, Texture);
        }
    }
    public class FloatProperty : MaterialProperty
    {
        public float Value { get; set; }
        public string PushConstantName { get; }

        public FloatProperty(string name, string pushConstantName, float defaultValue = 0f)
        {
            Name = name;
            PushConstantName = pushConstantName;
            Value = defaultValue;
        }

        public override void Apply(MaterialPass pass)
        {
            pass.PushConstant(PushConstantName, Value);
        }
    }
}
