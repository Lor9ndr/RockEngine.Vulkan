namespace RockEngine.Core.Rendering
{
    [Flags]
    public enum RenderLayerMask : ulong
    {
        None = 0,
        Default = 1 << 0,
        UI = 1 << 1,
        Debug = 1 << 2,
        User0 = 1UL << 3,
        User1 = 1UL << 4,
        User2 = 1UL << 5,
        User3 = 1UL << 6,
        User4 = 1UL << 7,
        User5 = 1UL << 8,
        User6 = 1UL << 9,
        User7 = 1UL << 10,
        User8 = 1UL << 11,
        User9 = 1UL << 12,
        User10 = 1UL << 13,
        User11 = 1UL << 14,
        User12 = 1UL << 15,
        User13 = 1UL << 16,
        User14 = 1UL << 17,
        User15 = 1UL << 18,
        User16 = 1UL << 19,
        User17 = 1UL << 20,
        User18 = 1UL << 21,
        User19 = 1UL << 22,
        User20 = 1UL << 23,
        User21 = 1UL << 24,
        User22 = 1UL << 25,
        User23 = 1UL << 26,
        User24 = 1UL << 27,
        User25 = 1UL << 28,
        User26 = 1UL << 29,
        User27 = 1UL << 30,
        User28 = 1UL << 31,
        User29 = 1UL << 32,
        User30 = 1UL << 33,
        User31 = 1UL << 34,

        // Define All as combination of all known layers, not ulong.MaxValue
        All = Default | UI | Debug | User0 | User1 | User2 | User3 | User4 | User5 | User6 | User7 | User8 | User9 |
               User10 | User11 | User12 | User13 | User14 | User15 | User16 | User17 | User18 | User19 | User20 |
               User21 | User22 | User23 | User24 | User25 | User26 | User27 | User28 | User29 | User30 | User31
    }
}
