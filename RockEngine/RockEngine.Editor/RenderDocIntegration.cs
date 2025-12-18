using Silk.NET.Windowing;

using System.Reflection;
using System.Runtime.InteropServices;

namespace RockEngine.Editor
{
    public class RenderDocIntegration : IDisposable
    {
        private IntPtr _renderDocDll;
        private RENDERDOC_API_1_4_2 _api;
        private bool _initialized;

        #pragma warning disable
        // Define RenderDoc API structure (based on renderdoc_app.h)
        [StructLayout(LayoutKind.Sequential)]
        private struct RENDERDOC_API_1_4_2
        {
            public IntPtr GetAPIVersion;
            public IntPtr SetCaptureOptionU32;
            public IntPtr SetCaptureOptionF32;
            public IntPtr GetCaptureOptionU32;
            public IntPtr GetCaptureOptionF32;
            public IntPtr SetFocusToggleKeys;
            public IntPtr SetCaptureKeys;
            public IntPtr GetOverlayBits;
            public IntPtr MaskOverlayBits;
            public IntPtr Shutdown;
            public IntPtr UnloadCrashHandler;
            public IntPtr SetCaptureFilePathTemplate;
            public IntPtr GetCaptureFilePathTemplate;
            public IntPtr GetNumCaptures;
            public IntPtr GetCapture;
            public IntPtr TriggerCapture;
            public IntPtr IsTargetControlConnected;
            public IntPtr LaunchReplayUI;
            public IntPtr SetActiveWindow;
            public IntPtr StartFrameCapture;
            public IntPtr IsFrameCapturing;
            public IntPtr EndFrameCapture;
            public IntPtr TriggerMultiFrameCapture;
            public IntPtr SetCaptureFileComments;
            public IntPtr DiscardFrameCapture;
        }
        #pragma warning enable

        // Delegate definitions for RenderDoc functions
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RENDERDOC_SetCaptureFilePathTemplate([MarshalAs(UnmanagedType.LPStr)] string pathTemplate);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RENDERDOC_SetCaptureOptionU32(uint option, uint value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RENDERDOC_StartFrameCapture(IntPtr device, IntPtr wndHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint RENDERDOC_EndFrameCapture(IntPtr device, IntPtr wndHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint RENDERDOC_TriggerCapture();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RENDERDOC_LaunchReplayUI(int connectTarget,
            [MarshalAs(UnmanagedType.LPStr)] string cmdline,
            [MarshalAs(UnmanagedType.LPStr)] string logfile,
            [MarshalAs(UnmanagedType.LPStr)] string comment);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RENDERDOC_Shutdown();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RENDERDOC_GetAPI(uint version, out IntPtr api);

        private readonly IntPtr _windowHandle;
        private readonly Lock _syncLock = new Lock();

        public bool IsInitialized => _initialized;
        public bool IsFrameCapturing { get; private set; }

        public RenderDocIntegration(IWindow window)
        {
            _windowHandle = window.Handle;
            Initialize();
        }

        private void Initialize()
        {
            lock (_syncLock)
            {
                if (_initialized) return;

                try
                {
                    // Try to load RenderDoc from common locations
                    string[] possiblePaths =
                    {
                        Environment.GetEnvironmentVariable("RENDERDOC_PATH") + "\\renderdoc.dll",
                        @"C:\Program Files\RenderDoc\renderdoc.dll",
                        @"C:\Program Files (x86)\RenderDoc\renderdoc.dll",
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "renderdoc.dll"),
                        Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? ".", "renderdoc.dll")
                    };

                    string renderDocPath = null;
                    foreach (var path in possiblePaths)
                    {
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            renderDocPath = path;
                            break;
                        }
                    }

                    if (renderDocPath == null)
                    {
                        Console.WriteLine("[RenderDoc] Library not found. Install RenderDoc to enable captures.");
                        return;
                    }

                    Console.WriteLine($"[RenderDoc] Attempting to load from: {renderDocPath}");

                    // Load RenderDoc DLL
                    _renderDocDll = NativeLibrary.Load(renderDocPath);
                    if (_renderDocDll == IntPtr.Zero)
                    {
                        Console.WriteLine($"[RenderDoc] Failed to load library: {renderDocPath}");
                        return;
                    }

                    // Get RENDERDOC_GetAPI function
                    var getApiFuncPtr = NativeLibrary.GetExport(_renderDocDll, "RENDERDOC_GetAPI");
                    if (getApiFuncPtr == IntPtr.Zero)
                    {
                        Console.WriteLine("[RenderDoc] Failed to find RENDERDOC_GetAPI export");
                        return;
                    }

                    var getApi = Marshal.GetDelegateForFunctionPointer<RENDERDOC_GetAPI>(getApiFuncPtr);

                    // Get API version 1.4.2
                    int result = getApi(10402, out nint apiPtr); // 1.4.2 = 10402
                    if (result != 1 || apiPtr == IntPtr.Zero)
                    {
                        Console.WriteLine("[RenderDoc] Failed to get API interface");
                        return;
                    }

                    // Marshal the API structure
                    _api = Marshal.PtrToStructure<RENDERDOC_API_1_4_2>(apiPtr);

                    // Set up capture options
                    SetCaptureOptions();

                    _initialized = true;
                    Console.WriteLine($"[RenderDoc] Initialized successfully");

                    // Set active window
                    SetActiveWindow();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderDoc] Initialization failed: {ex.Message}");
                    Console.WriteLine($"[RenderDoc] Stack trace: {ex.StackTrace}");
                }
            }
        }

        private void SetActiveWindow()
        {
            if (!_initialized || _api.SetActiveWindow == IntPtr.Zero) return;

            try
            {
                var setActiveWindow = Marshal.GetDelegateForFunctionPointer<
                    RENDERDOC_StartFrameCapture>(_api.SetActiveWindow); // Same signature as StartFrameCapture

                setActiveWindow(IntPtr.Zero, _windowHandle);
                Console.WriteLine("[RenderDoc] Active window set");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderDoc] Failed to set active window: {ex.Message}");
            }
        }

        private void SetCaptureOptions()
        {
            if (!_initialized) return;

            try
            {
                // Set capture directory to Documents/RenderDoc
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string capturePath = Path.Combine(documents, "RenderDoc");
                Directory.CreateDirectory(capturePath);

                // Set capture file path template
                if (_api.SetCaptureFilePathTemplate != IntPtr.Zero)
                {
                    var setPathTemplate = Marshal.GetDelegateForFunctionPointer<
                        RENDERDOC_SetCaptureFilePathTemplate>(_api.SetCaptureFilePathTemplate);
                    setPathTemplate(capturePath + @"\capture");
                    Console.WriteLine($"[RenderDoc] Capture path set to: {capturePath}");
                }

                // Set capture options
                if (_api.SetCaptureOptionU32 != IntPtr.Zero)
                {
                    var setOptionU32 = Marshal.GetDelegateForFunctionPointer<
                        RENDERDOC_SetCaptureOptionU32>(_api.SetCaptureOptionU32);

                    // Allow fullscreen capture
                    setOptionU32(0, 1); // RENDERDOC_CaptureOption::AllowFullscreen
                    // Capture all callstacks
                    setOptionU32(1, 1); // RENDERDOC_CaptureOption::CaptureCallstacks
                    // Verify buffer access
                    setOptionU32(2, 1); // RENDERDOC_CaptureOption::VerifyBufferAccess
                    // Hook into children
                    setOptionU32(3, 1); // RENDERDOC_CaptureOption::HookIntoChildren
                    // Capture all command lists
                    setOptionU32(6, 1); // RENDERDOC_CaptureOption::CaptureAllCmdLists

                    Console.WriteLine("[RenderDoc] Capture options set");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderDoc] Failed to set capture options: {ex.Message}");
            }
        }

        public void StartFrameCapture()
        {
            if (!_initialized || _api.StartFrameCapture == IntPtr.Zero) return;

            lock (_syncLock)
            {
                try
                {
                    var startCapture = Marshal.GetDelegateForFunctionPointer<
                        RENDERDOC_StartFrameCapture>(_api.StartFrameCapture);

                    int result = startCapture(IntPtr.Zero, _windowHandle);
                    if (result == 1)
                    {
                        IsFrameCapturing = true;
                        Console.WriteLine("[RenderDoc] Started frame capture");
                    }
                    else
                    {
                        Console.WriteLine($"[RenderDoc] StartFrameCapture returned: {result}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderDoc] Failed to start capture: {ex.Message}");
                }
            }
        }

        public void EndFrameCapture()
        {
            if (!_initialized || !IsFrameCapturing || _api.EndFrameCapture == IntPtr.Zero) return;

            lock (_syncLock)
            {
                try
                {
                    var endCapture = Marshal.GetDelegateForFunctionPointer<
                        RENDERDOC_EndFrameCapture>(_api.EndFrameCapture);

                    uint captureId = endCapture(IntPtr.Zero, _windowHandle);
                    if (captureId != 0)
                    {
                        Console.WriteLine($"[RenderDoc] Capture saved with ID: {captureId}");

                        // Optionally launch RenderDoc UI
                        // LaunchReplayUI();
                    }
                    else
                    {
                        Console.WriteLine("[RenderDoc] Capture ID is 0 (might indicate failure)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderDoc] Failed to end capture: {ex.Message}");
                }
                finally
                {
                    IsFrameCapturing = false;
                }
            }
        }

        public void TriggerCapture()
        {
            if (!_initialized || _api.TriggerCapture == IntPtr.Zero) return;

            try
            {
                var triggerCapture = Marshal.GetDelegateForFunctionPointer<
                    RENDERDOC_TriggerCapture>(_api.TriggerCapture);

                triggerCapture();
                Console.WriteLine("[RenderDoc] Triggered capture");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderDoc] Failed to trigger capture: {ex.Message}");
            }
        }

        private void LaunchReplayUI()
        {
            if (!_initialized || _api.LaunchReplayUI == IntPtr.Zero) return;

            try
            {
                var launchUI = Marshal.GetDelegateForFunctionPointer<
                    RENDERDOC_LaunchReplayUI>(_api.LaunchReplayUI);

                // Launch RenderDoc UI without connecting to a target
                launchUI(0, null, null, null);
                Console.WriteLine("[RenderDoc] Launched RenderDoc UI");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderDoc] Failed to launch UI: {ex.Message}");
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_renderDocDll != IntPtr.Zero)
                {
                    if (_initialized && _api.Shutdown != IntPtr.Zero)
                    {
                        try
                        {
                            var shutdown = Marshal.GetDelegateForFunctionPointer<
                                RENDERDOC_Shutdown>(_api.Shutdown);
                            shutdown();
                            Console.WriteLine("[RenderDoc] Shutdown completed");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RenderDoc] Error during shutdown: {ex.Message}");
                        }
                    }

                    NativeLibrary.Free(_renderDocDll);
                    _renderDocDll = IntPtr.Zero;
                }
                _initialized = false;
            }
        }

        // Helper method to check if RenderDoc is attached
        public static bool IsRenderDocAttached()
        {
            try
            {
                // Try to load renderdoc.dll without actually loading it
                return NativeLibrary.TryLoad("renderdoc.dll", out var handle);
            }
            catch
            {
                return false;
            }
        }

        // Alternative simpler integration for hotkey-based capture
        public static void CaptureFrameViaHotkey()
        {
            // RenderDoc can capture via hotkeys when injected
            // This method just logs that hotkey capture should work
            Console.WriteLine("[RenderDoc] Press F11 to capture a frame (if RenderDoc is injected)");
        }
    }
}