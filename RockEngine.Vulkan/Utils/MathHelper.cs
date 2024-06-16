namespace RockEngine.Vulkan.Utils
{
    public static class MathHelper
    {
        public const float Pi = (float)Math.PI;

        public const float PiOver2 = (float)Math.PI / 2f;

        public const float PiOver3 = (float)Math.PI / 3f;

        public const float PiOver4 = (float)Math.PI / 4f;

        public const float PiOver6 = (float)Math.PI / 6f;

        public const float TwoPi = (float)Math.PI * 2f;

        public const float ThreePiOver2 = 4.712389f;

        public const float E = (float)Math.E;

        public const float Log10E = 0.4342945f;

        public const float Log2E = 1.442695f;
        public static float DegreesToRadians(float degrees)
        {
            return degrees * ((float)Math.PI / 180f);
        }
        public static float RadiansToDegrees(float radians)
        {
            return radians * (180f / (float)Math.PI);
        }

    }
}
