namespace RockEngine.Core
{
    public static class Time
    {
        private static float _deltaTime;
        private static float _unscaledDeltaTime;
        private static float _totalTime;
        private static float _lastFpsUpdateTime;
        private static int _currentFps;
        private static int _frameCount;
        private static float _timeScale = 1.0f;
        private static double _lastUpdateTime;
        private static readonly object _lock = new();
        private static readonly Queue<float> _deltaTimeHistory = new();
        private const int FRAME_HISTORY_SIZE = 60; // For averaging

        public static float DeltaTime => _deltaTime;
        public static float UnscaledDeltaTime => _unscaledDeltaTime;
        public static float TotalTime => _totalTime;
        public static int FPS => _currentFps;
        public static float TimeScale
        {
            get => _timeScale;
            set => _timeScale = Math.Max(0, value); // Prevent negative time scale
        }

        public static void Update(double currentTime)
        {
            lock (_lock)
            {
                // Calculate delta time based on actual time difference
                if (_lastUpdateTime > 0)
                {
                    _unscaledDeltaTime = (float)(currentTime - _lastUpdateTime);

                    // Clamp delta time to prevent large spikes (e.g., when window is minimized/resized)
                    _unscaledDeltaTime = Math.Min(_unscaledDeltaTime, 0.1f); // Max 100ms

                    // Apply time scale
                    _deltaTime = _unscaledDeltaTime * _timeScale;

                    // Add to history for smoothing
                    _deltaTimeHistory.Enqueue(_unscaledDeltaTime);
                    if (_deltaTimeHistory.Count > FRAME_HISTORY_SIZE)
                    {
                        _deltaTimeHistory.Dequeue();
                    }
                }

                _lastUpdateTime = currentTime;
                _totalTime = (float)currentTime;
                _frameCount++;

                // Update FPS every second
                if (_totalTime - _lastFpsUpdateTime >= 1.0f) 
                {
                    // Calculate FPS based on actual frame count in the last second
                    _currentFps = _frameCount;


                    _frameCount = 0;
                    _lastFpsUpdateTime = _totalTime;
                }
            }
        }

        public static float GetAverageDeltaTime()
        {
            lock (_lock)
            {
                if (_deltaTimeHistory.Count == 0)
                    return _unscaledDeltaTime;

                return _deltaTimeHistory.Average();
            }
        }

        public static float GetAverageFPS()
        {
            var avgDelta = GetAverageDeltaTime();
            return avgDelta > 0 ? 1.0f / avgDelta : 0;
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _deltaTime = 0;
                _unscaledDeltaTime = 0;
                _lastUpdateTime = 0;
                _deltaTimeHistory.Clear();
            }
        }
    }
}