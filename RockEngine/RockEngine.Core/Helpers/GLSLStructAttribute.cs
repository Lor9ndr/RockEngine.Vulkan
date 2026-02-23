namespace RockEngine.Core.Helpers
{
    public enum GLSLMemoryLayout
    {
        Std140,
        Std430,
        Scalar
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public class GLSLStructAttribute : Attribute
    {
        public GLSLMemoryLayout Layout { get; }

        public GLSLStructAttribute(GLSLMemoryLayout layout = GLSLMemoryLayout.Std140)
        {
            Layout = layout;
        }
    }
}