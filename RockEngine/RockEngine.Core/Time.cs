namespace RockEngine.Core
{
    public static class Time
    {
        private static float _lastFrameTime;
        private static float _deltaTime;
        private static float _totalTime;
        private static float _lastFpsUpdateTime;
        private static int _currentFps;
        private static int _frameCount;
        public static float DeltaTime => _deltaTime;
        public static float TotalTime => _totalTime;
        public static int FPS => _currentFps;
        public static float TimeScale { get; set; } = 1.0f;
        public static float ScaledDeltaTime => _deltaTime * TimeScale;

        public static void Update(double currentTime, double deltaTime)
        {
            float currentTimeFloat = (float)currentTime;
            _deltaTime = (float)deltaTime;
            _lastFrameTime = currentTimeFloat;

            _totalTime = currentTimeFloat;
            _frameCount++;

            // Update FPS every second
            if (_totalTime - _lastFpsUpdateTime >= 1.0f)
            {
                _currentFps = _frameCount;
                _frameCount = 0;
                _lastFpsUpdateTime = _totalTime;
            }
        }
    }
}
