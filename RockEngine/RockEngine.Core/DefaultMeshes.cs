namespace RockEngine.Core
{
    public static class DefaultMeshes
    {
        public static readonly MeshData Cube = new MeshData()
        {
            Vertices = 
            [
                 // Front face (Z+)
               new Vertex(-1.0f, -1.0f,  1.0f,0,0,0,0,0), // 0
               new Vertex(  1.0f, -1.0f,  1.0f, 0, 0, 0, 0, 0), // 1
               new Vertex(  1.0f,  1.0f,  1.0f, 0, 0, 0, 0, 0), // 2
               new Vertex( -1.0f,  1.0f,  1.0f, 0, 0, 0, 0, 0), // 3
    
                // Back face (Z-)
               new Vertex( -1.0f, -1.0f, -1.0f,0,0,0,0,0), // 4
               new Vertex(  1.0f, -1.0f, -1.0f,0,0,0,0,0), // 5
               new Vertex(  1.0f,  1.0f, -1.0f,0,0,0,0,0), // 6
               new Vertex( -1.0f,  1.0f, -1.0f,0,0,0,0,0), // 7
            ],
            Indices = [
                // Front face
                0, 1, 2, 2, 3, 0,
    
                // Back face
                5, 4, 7, 7, 6, 5,
    
                // Left face
                4, 0, 3, 3, 7, 4,
    
                // Right face
                1, 5, 6, 6, 2, 1,
    
                // Top face
                3, 2, 6, 6, 7, 3,
    
                // Bottom face
                4, 5, 1, 1, 0, 4
            ],
             Name = "Cube"
        };
    }
}
