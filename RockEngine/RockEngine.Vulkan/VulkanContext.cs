using NLog;

using RockEngine.Vulkan.Builders;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan
{
    public class VulkanContext : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static Vk Vk = Vk.GetApi();
        public VkLogicalDevice Device { get; }
        public VkInstance Instance { get; }
        public ISurfaceHandler Surface { get; }
        public int MaxFramesPerFlight { get; internal set; }
        public SamplerCache SamplerCache { get; }
        public SubmitContext GraphicsSubmitContext { get; }
        public SubmitContext ComputeSubmitContext { get; }
        public SubmitContext TransferSubmitContext { get; }
        public SubmitContext PresentSubmitContext { get; }

        public DebugUtilsFunctions DebugUtils => _debugUtilsFunctions;

        private static VulkanContext? _renderingContext;

        public static VulkanContext GetCurrent() => _renderingContext ?? throw new InvalidOperationException("Rendering context was not created yet");

        public static ref AllocationCallbacks CustomAllocator<T>() => ref Vulkan.CustomAllocator.CreateCallbacks<T>();

        private static readonly string[] _validationLayers = ["VK_LAYER_KHRONOS_validation"];
        private static DebugUtilsMessengerCallbackFunctionEXT _debugCallback;
        private readonly Stack<IDisposable> _pendingDisposals = new Stack<IDisposable>();
        private readonly DebugUtilsFunctions _debugUtilsFunctions;
        private readonly AppSettings _settings;
        public readonly Vk Api = Vk;

        public VulkanContext(IWindow window, AppSettings settings)
        {
            _settings = settings;
            if (_renderingContext is not null)
            {
                // Have to think about supporting multiple windows with different contexts
                throw new NotSupportedException("For now it is unsupported to have multiple contexts");
            }
            Instance = CreateInstance(window, _settings);
            Surface = CreateSurface(window);
            Device = CreateDevice(Surface, Instance, this);
            _debugUtilsFunctions = new DebugUtilsFunctions(Vk, Device, _settings);
            
            Device.NameQueues();

            MaxFramesPerFlight = _settings.MaxFramesPerFlight;
            _renderingContext = this;
            SamplerCache = new SamplerCache(this);

            // Create SubmitContexts with thread managers
            GraphicsSubmitContext =  new SubmitContext(this, Device.GraphicsQueue);

            ComputeSubmitContext = new SubmitContext(this, Device.ComputeQueue);
            TransferSubmitContext = new SubmitContext(this, Device.TransferQueue);
            PresentSubmitContext = new SubmitContext(this, Device.PresentQueue);

            _settings = settings;
        }

      

        private SDLSurfaceHandler CreateSurface(IWindow window)
        {
            return SDLSurfaceHandler.CreateSurface(window, this);
        }

        private static unsafe VkInstance CreateInstance(IWindow surface, AppSettings appSettings)
        {
            var appname = (byte*)Marshal.StringToHGlobalAnsi(appSettings.Name);
            var appInfo = new ApplicationInfo()
            {
                ApiVersion = Vk.Version13,
                ApplicationVersion = Vk.MakeVersion(1, 0, 0),
                EngineVersion = Vk.MakeVersion(1, 0, 0),
                PApplicationName = appname,
                PEngineName = appname,
                SType = StructureType.ApplicationInfo,
            };

            var ci = new InstanceCreateInfo()
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
            };

            _debugCallback = DebugUtilsMessengerCallbackFunctionEXT;
            IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_debugCallback);


            PfnDebugUtilsMessengerCallbackEXT dbcallback = new PfnDebugUtilsMessengerCallbackEXT(
                (delegate* unmanaged[Cdecl]<DebugUtilsMessageSeverityFlagsEXT, DebugUtilsMessageTypeFlagsEXT, DebugUtilsMessengerCallbackDataEXT*, void*, Bool32>)callbackPtr);


            var extensions = surface.VkSurface.GetRequiredExtensions(out uint countExtensions);
            ci.PpEnabledExtensionNames = extensions;
            ci.EnabledExtensionCount = countExtensions;

            var instanceBuilder = new VkInstanceBuilder();
                
            if (appSettings.EnableValidationLayers)
            {
                instanceBuilder.UseValidationLayers(_validationLayers)
                    .UseDebugUtilsMessenger(DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt | DebugUtilsMessageSeverityFlagsEXT.InfoBitExt,
                                        DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt,
                                        dbcallback, (void*)nint.Zero);
            }

            var instance = instanceBuilder.Build(ref ci);

            Marshal.FreeHGlobal((nint)appname);
            return instance;
        }
        private static unsafe uint DebugUtilsMessengerCallbackFunctionEXT(
       DebugUtilsMessageSeverityFlagsEXT messageSeverity,
       DebugUtilsMessageTypeFlagsEXT messageTypes,
       DebugUtilsMessengerCallbackDataEXT* pCallbackData,
       void* pUserData)
        {
            var message = Marshal.PtrToStringUTF8((nint)pCallbackData->PMessage);
            var logLevel = LogLevel.Trace;

            switch (messageSeverity)
            {
                case DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt:
                    logLevel = LogLevel.Error;
                    _logger.Log(logLevel, $"{message}\n{Environment.StackTrace}");
                    throw new VulkanException(messageSeverity, message);

                case DebugUtilsMessageSeverityFlagsEXT.WarningBitExt:
                    logLevel = LogLevel.Warn;
                    _logger.Log(logLevel, message);
                    break;

                case DebugUtilsMessageSeverityFlagsEXT.InfoBitExt:
                    logLevel = LogLevel.Info;
                    _logger.Log(logLevel, message);
                    break;

                case DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt:
                    // Optionally skip verbose messages entirely
                    logLevel = LogLevel.Trace;
                    _logger.Log(logLevel, message);
                    break;
            }

            return Vk.False;
        }


        private static VkLogicalDevice CreateDevice(ISurfaceHandler surface, VkInstance instance, VulkanContext vulkanContext)
        {
            var device = VkPhysicalDevice.Create(instance);
            return VkLogicalDevice.Create(vulkanContext, device, surface, KhrSwapchain.ExtensionName);
        }

        public void Dispose()
        {
            Surface.Dispose();
            Device.Dispose();
            Instance.Dispose();
        }
    }
}
