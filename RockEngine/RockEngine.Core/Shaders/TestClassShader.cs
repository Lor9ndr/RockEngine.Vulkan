using RockEngine.Core.Attributes;
using RockEngine.Mathematics;
using RockEngine.Shading;


namespace RockEngine.Core.Shaders
{
    [GLSLShader(ShaderType.Vertex)]
    public partial class VertexShader
    {
        [Uniform] public Matrix4 uModelViewProjection;
        [Uniform] public Matrix4 uNormalMatrix;

        [In] public Vector3 aPosition;
        [In] public Vector3 aNormal;
        [In] public Vector2 aTexCoord;

        [Out] public Vector3 vNormal;
        [Out] public Vector3 vPosition;
        [Out] public Vector2 vTexCoord;

        [ShaderMain]
        public void  Main()
        {
            // This is actual C# code that gets converted to GLSL!
            Vector4 position = new Vector4(aPosition, 1.0f);

            // Built-in variable assignment
            gl_Position = uModelViewProjection * position;

            // Normal transformation
            Vector3 worldNormal = ShaderMath.Normalize((uNormalMatrix * aNormal).XYZ);

            // Pass data to fragment shader
            vNormal = worldNormal;
            vPosition = position.XYZ;
            vTexCoord = aTexCoord;
        }

        // Built-in variables
        private Vector4 gl_Position;
    }
}