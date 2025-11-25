namespace RockEngine.Editor.Rendering
{
    [Flags]
    public enum DebugLayer : uint
    {
        None = 0,

        // Core visualization
        Wireframe = 1 << 0,
        BoundingVolumes = 1 << 1,
        Normals = 1 << 2,

        // Lighting & Shadows
        CascadeVisualization = 1 << 3,
        ShadowMapPreview = 1 << 4,
        LightIcons = 1 << 5,
        LightInfluence = 1 << 6,

        // Rendering
        GBufferPreview = 1 << 7,
        DepthPreview = 1 << 8,
        Overdraw = 1 << 9,

        // Physics & Collision
        CollisionGeometry = 1 << 10,
        PhysicsContacts = 1 << 11,
        Raycasts = 1 << 12,

        // Navigation & AI
        NavigationMesh = 1 << 13,
        Pathfinding = 1 << 14,
        AIState = 1 << 15,

        // Performance
        PerformanceStats = 1 << 16,
        FrameTiming = 1 << 17,
        MemoryUsage = 1 << 18,

        // Custom user layers
        User1 = 1 << 24,
        User2 = 1 << 25,
        User3 = 1 << 26,
        User4 = 1 << 27,

        // Groups
        AllLighting = CascadeVisualization | ShadowMapPreview | LightIcons | LightInfluence,
        AllRendering = GBufferPreview | DepthPreview | Overdraw,
        AllPhysics = CollisionGeometry | PhysicsContacts | Raycasts,
        AllNavigation = NavigationMesh | Pathfinding | AIState,
        AllPerformance = PerformanceStats | FrameTiming | MemoryUsage,
        All = 0xFFFFFFFF
    }

}
