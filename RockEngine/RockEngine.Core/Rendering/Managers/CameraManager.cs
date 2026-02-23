using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;

using System.Runtime.InteropServices;
using System.Numerics;

namespace RockEngine.Core.Rendering.Managers
{
    public class CameraManager
    {
        private readonly List<Camera> _activeCameras = new List<Camera>();
        
        public IReadOnlyList<Camera> RegisteredCameras => _activeCameras;

        public CameraManager()
        {
           
        }

        public int Register(Camera camera, WorldRenderer renderer)
        {
            _activeCameras.Add(camera);
            return _activeCameras.Count - 1;
        }


        public void Unregister(Camera camera)
        {
            _activeCameras.Remove(camera);
            camera.RenderTarget?.Dispose();
        }

        [GLSLStruct(GLSLMemoryLayout.Std140)]

        public struct IBLParams
        {
            public float Exposure;      // [0.1 - 4.0] Typical HDR exposure range
            public float EnvIntensity;  // [0.0 - 2.0] Environment map multiplier  
            public float AoStrength;    // [0.0 - 2.0] Ambient occlusion effect strength
            public float Gamma;         // [1.8 - 2.4] Gamma correction
            public float EnvRotation;   // [0.0 - 2*PI] Environment map rotation
            private float _padding1;
            private float _padding2;
            private float _padding3;

            public IBLParams()
            {
                Exposure = 1.0f;
                EnvIntensity = 1.0f;
                AoStrength = 1.0f;
                Gamma = 2.2f;
                EnvRotation = 0.0f;
            }
        }
    }

}
