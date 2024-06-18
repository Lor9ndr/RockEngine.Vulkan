using Silk.NET.SDL;

using System;

namespace RockEngine.Vulkan.Utils
{
    internal static class DefaultMesh
    {
        public static Vertex[] CubeVertices = new Vertex[]
        {
               new Vertex(-1.0f, -1.0f, -1.0f,  0.0f,  0.0f, -1.0f, 0.0f, 0.0f), // bottom-left
               new Vertex( 1.0f,  1.0f, -1.0f,  0.0f,  0.0f, -1.0f, 1.0f, 1.0f), // top-right
               new Vertex( 1.0f, -1.0f, -1.0f,  0.0f,  0.0f, -1.0f, 1.0f, 0.0f), // bottom-right         
               new Vertex( 1.0f,  1.0f, -1.0f,  0.0f,  0.0f, -1.0f, 1.0f, 1.0f), // top-right
               new Vertex(-1.0f, -1.0f, -1.0f,  0.0f,  0.0f, -1.0f, 0.0f, 0.0f), // bottom-left
               new Vertex(-1.0f,  1.0f, -1.0f,  0.0f,  0.0f, -1.0f, 0.0f, 1.0f), // top-left
               new Vertex(-1.0f, -1.0f,  1.0f,  0.0f,  0.0f,  1.0f, 0.0f, 0.0f), // bottom-left
               new Vertex( 1.0f, -1.0f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f, 0.0f), // bottom-right
               new Vertex( 1.0f,  1.0f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f, 1.0f), // top-right
               new Vertex( 1.0f,  1.0f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f, 1.0f), // top-right
               new Vertex(-1.0f,  1.0f,  1.0f,  0.0f,  0.0f,  1.0f, 0.0f, 1.0f), // top-left
               new Vertex(-1.0f, -1.0f,  1.0f,  0.0f,  0.0f,  1.0f, 0.0f, 0.0f), // bottom-left
               new Vertex(-1.0f,  1.0f,  1.0f, -1.0f,  0.0f,  0.0f, 1.0f, 0.0f), // top-right
               new Vertex(-1.0f,  1.0f, -1.0f, -1.0f,  0.0f,  0.0f, 1.0f, 1.0f), // top-left
               new Vertex(-1.0f, -1.0f, -1.0f, -1.0f,  0.0f,  0.0f, 0.0f, 1.0f), // bottom-left
               new Vertex(-1.0f, -1.0f, -1.0f, -1.0f,  0.0f,  0.0f, 0.0f, 1.0f), // bottom-left
               new Vertex(-1.0f, -1.0f,  1.0f, -1.0f,  0.0f,  0.0f, 0.0f, 0.0f), // bottom-right
               new Vertex(-1.0f,  1.0f,  1.0f, -1.0f,  0.0f,  0.0f, 1.0f, 0.0f), // top-right
               new Vertex( 1.0f,  1.0f,  1.0f,  1.0f,  0.0f,  0.0f, 1.0f, 0.0f), // top-left
               new Vertex( 1.0f, -1.0f, -1.0f,  1.0f,  0.0f,  0.0f, 0.0f, 1.0f), // bottom-right
               new Vertex( 1.0f,  1.0f, -1.0f,  1.0f,  0.0f,  0.0f, 1.0f, 1.0f), // top-right         
               new Vertex( 1.0f, -1.0f, -1.0f,  1.0f,  0.0f,  0.0f, 0.0f, 1.0f), // bottom-right
               new Vertex( 1.0f,  1.0f,  1.0f,  1.0f,  0.0f,  0.0f, 1.0f, 0.0f), // top-left
               new Vertex( 1.0f, -1.0f,  1.0f,  1.0f,  0.0f,  0.0f, 0.0f, 0.0f), // bottom-left     
               new Vertex(-1.0f, -1.0f, -1.0f,  0.0f, -1.0f,  0.0f, 0.0f, 1.0f), // top-right
               new Vertex( 1.0f, -1.0f, -1.0f,  0.0f, -1.0f,  0.0f, 1.0f, 1.0f), // top-left
               new Vertex( 1.0f, -1.0f,  1.0f,  0.0f, -1.0f,  0.0f, 1.0f, 0.0f), // bottom-left
               new Vertex( 1.0f, -1.0f,  1.0f,  0.0f, -1.0f,  0.0f, 1.0f, 0.0f), // bottom-left
               new Vertex(-1.0f, -1.0f,  1.0f,  0.0f, -1.0f,  0.0f, 0.0f, 0.0f), // bottom-right
               new Vertex(-1.0f, -1.0f, -1.0f,  0.0f, -1.0f,  0.0f, 0.0f, 1.0f), // top-right
               new Vertex(-1.0f,  1.0f, -1.0f,  0.0f,  1.0f,  0.0f, 0.0f, 1.0f), // top-left
               new Vertex( 1.0f,  1.0f , 1.0f,  0.0f,  1.0f,  0.0f, 1.0f, 0.0f), // bottom-right
               new Vertex( 1.0f,  1.0f, -1.0f,  0.0f,  1.0f,  0.0f, 1.0f, 1.0f), // top-right     
               new Vertex( 1.0f,  1.0f,  1.0f,  0.0f,  1.0f,  0.0f, 1.0f, 0.0f), // bottom-right
               new Vertex(-1.0f,  1.0f, -1.0f,  0.0f,  1.0f,  0.0f, 0.0f, 1.0f), // top-left
               new Vertex(-1.0f,  1.0f,  1.0f,  0.0f,  1.0f,  0.0f, 0.0f, 0.0f) // bottom-left        
           };

        public static Vertex[] PlaneVertices = new Vertex[]
        {
               new Vertex(-0.5f, -0.5f, 0, 1.0f, 0.0f, 0.0f, 1.0f, 0.0f),
                 new Vertex(0.5f, -0.5f, 0, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f),
                 new Vertex(0.5f, 0.5f, 0, 0.0f, 0.0f, 1.0f, 0.0f, 1.0f),
                 new Vertex(-0.5f, 0.5f, 0, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f)
        };

        public static uint[] PlaneIndices = new uint[] {
            0, 1, 2, 2, 3, 0
        };

    }
}
