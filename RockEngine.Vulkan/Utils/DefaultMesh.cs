namespace RockEngine.Vulkan.Utils
{
    internal static class DefaultMesh
    {
        public static Vertex[] CubeVertices = new Vertex[]
        {
               new Vertex(-1.0f, -1.0f, -1.0f, -1.0f, 0.0f, 0.0f), // bottom-left
               new Vertex( 1.0f,  1.0f, -1.0f, -1.0f, 1.0f, 1.0f), // top-right
               new Vertex( 1.0f, -1.0f, -1.0f, -1.0f, 1.0f, 0.0f), // bottom-right         
               new Vertex( 1.0f,  1.0f, -1.0f, -1.0f, 1.0f, 1.0f), // top-right
               new Vertex(-1.0f, -1.0f, -1.0f, -1.0f, 0.0f, 0.0f), // bottom-left
               new Vertex(-1.0f,  1.0f, -1.0f, -1.0f, 0.0f, 1.0f), // top-left
               new Vertex(-1.0f, -1.0f,  1.0f,  1.0f, 0.0f, 0.0f), // bottom-left
               new Vertex( 1.0f, -1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // bottom-right
               new Vertex( 1.0f,  1.0f,  1.0f,  1.0f, 1.0f, 1.0f), // top-right
               new Vertex( 1.0f,  1.0f,  1.0f,  1.0f, 1.0f, 1.0f), // top-right
               new Vertex(-1.0f,  1.0f,  1.0f,  1.0f, 0.0f, 1.0f), // top-left
               new Vertex(-1.0f, -1.0f,  1.0f,  1.0f, 0.0f, 0.0f), // bottom-left
               new Vertex(-1.0f,  1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // top-right
               new Vertex(-1.0f,  1.0f, -1.0f,  1.0f, 1.0f, 1.0f), // top-left
               new Vertex(-1.0f, -1.0f, -1.0f,  1.0f, 0.0f, 1.0f), // bottom-left
               new Vertex(-1.0f, -1.0f, -1.0f,  1.0f, 0.0f, 1.0f), // bottom-left
               new Vertex(-1.0f, -1.0f,  1.0f,  1.0f, 0.0f, 0.0f), // bottom-right
               new Vertex(-1.0f,  1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // top-right
               new Vertex( 1.0f,  1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // top-left
               new Vertex( 1.0f, -1.0f, -1.0f,  1.0f, 0.0f, 1.0f), // bottom-right
               new Vertex( 1.0f,  1.0f, -1.0f,  1.0f, 1.0f, 1.0f), // top-right         
               new Vertex( 1.0f, -1.0f, -1.0f,  1.0f, 0.0f, 1.0f), // bottom-right
               new Vertex( 1.0f,  1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // top-left
               new Vertex( 1.0f, -1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // bottom-left     
               new Vertex(-1.0f, -1.0f, -1.0f,  1.0f, 0.0f, 1.0f), // top-right
               new Vertex( 1.0f, -1.0f, -1.0f,  1.0f, 1.0f, 1.0f), // top-left
               new Vertex( 1.0f, -1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // bottom-left
               new Vertex( 1.0f, -1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // bottom-left
               new Vertex(-1.0f, -1.0f,  1.0f,  1.0f, 0.0f, 0.0f), // bottom-right
               new Vertex(-1.0f, -1.0f, -1.0f,  1.0f, 0.0f, 1.0f), // top-right
               new Vertex(-1.0f,  1.0f, -1.0f,  1.0f, 0.0f, 1.0f), // top-left
               new Vertex( 1.0f,  1.0f , 1.0f,  1.0f, 1.0f, 0.0f), // bottom-right
               new Vertex( 1.0f,  1.0f, -1.0f,  1.0f, 1.0f, 1.0f), // top-right     
               new Vertex( 1.0f,  1.0f,  1.0f,  1.0f, 1.0f, 0.0f), // bottom-right
               new Vertex(-1.0f,  1.0f, -1.0f,  1.0f, 0.0f, 1.0f), // top-left
               new Vertex(-1.0f,  1.0f,  1.0f,  1.0f, 0.0f, 0.0f) // bottom-left        
           };
    }
}
