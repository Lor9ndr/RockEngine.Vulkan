using RockEngine.Shading;

namespace RockEngine.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GLSLShaderAttribute : Attribute
    {
        public ShaderType Type { get; }

        public GLSLShaderAttribute(ShaderType type)
        {
            Type = type;
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ShaderMainAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class UniformAttribute : Attribute
    {
        public string Name { get; }

        public UniformAttribute(string name = null)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class InAttribute : Attribute
    {
        public string Name { get; }

        public InAttribute(string name = null)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class OutAttribute : Attribute
    {
        public string Name { get; }

        public OutAttribute(string name = null)
        {
            Name = name;
        }
    }
}
