using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.VulkanInitilizers
{
    public unsafe class VulkanContext : IDisposable
    {
        public Vk Api { get; private set; }
        public InstanceWrapper Instance { get; private set; }
        public LogicalDeviceWrapper Device { get; private set; }
        public ISurfaceHandler Surface { get; private set; }
        public CommandPoolManager CommandPoolManager { get; private set;}
        public DescriptorPoolFactory DescriptorPoolFactory { get; }
        public PipelineManager PipelineManager { get; }

        public readonly Mutex QueueMutex = new Mutex();

        private readonly IWindow _window;
        private readonly string[] _validationLayers = ["VK_LAYER_KHRONOS_validation"];

        public const int MAX_FRAMES_IN_FLIGHT = 2;
        public int CurrentFrame { get; private set;}

        public VulkanContext(IWindow window, string appName)
        {
            _window = window;
            Api = Vk.GetApi();
            CreateInstance(appName);
            CreateSurface();
            CreateDevice();
            CommandPoolManager = new CommandPoolManager(this);
            PipelineManager = new PipelineManager(this);
            DescriptorPoolFactory = new DescriptorPoolFactory(this);
        }


        public void SwapFrame()
        {
            CurrentFrame = (CurrentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        public CommandPoolWrapper GetOrCreateCommandPool()
        {
            return CommandPoolManager.GetCommandPool();
        }

        private void CreateInstance(string appName)
        {
            var appname = (byte*)Marshal.StringToHGlobalAnsi(appName);
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


            IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(DebugCallback);


            PfnDebugUtilsMessengerCallbackEXT dbcallback = new PfnDebugUtilsMessengerCallbackEXT(
                (delegate* unmanaged[Cdecl]<DebugUtilsMessageSeverityFlagsEXT, DebugUtilsMessageTypeFlagsEXT, DebugUtilsMessengerCallbackDataEXT*, void*, Bool32>)callbackPtr);

            var extensions = _window.VkSurface.GetRequiredExtensions(out uint countExtensions);
            ci.PpEnabledExtensionNames = extensions;
            ci.EnabledExtensionCount = countExtensions;

            Instance = new VulkanInstanceBuilder(Api)
                .UseValidationLayers(_validationLayers)
                .UseDebugUtilsMessenger(DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
                                        DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt,
                                        dbcallback, (void*)nint.Zero)
                .Build(ref ci);

            Marshal.FreeHGlobal((nint)appname);
        }

        private void CreateSurface()
        {
            Surface = GlfwSurfaceHandler.CreateSurface(_window, this);
        }

        private void CreateDevice()
        {
            var device = PhysicalDeviceWrapper.Create(this);
            Device = LogicalDeviceWrapper.Create(Api, device, Surface, KhrSwapchain.ExtensionName);
        }

        unsafe Bool32 DebugCallback(DebugUtilsMessageSeverityFlagsEXT severity,
               DebugUtilsMessageTypeFlagsEXT messageType,
               DebugUtilsMessengerCallbackDataEXT* callbackData,
               void* userData)
        {
            var message = Marshal.PtrToStringUTF8((nint)callbackData->PMessage);

            // Change console color based on severity
            switch (severity)
            {
                case DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.WarningBitExt:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.InfoBitExt:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
                default:
                    Console.ResetColor();
                    break;
            }

            Console.WriteLine($"{severity} ||| {message}");

            // Reset console color to default
            Console.ResetColor();

            // Throw an exception if severity is ErrorBitEXT
            if (severity == DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt)
            {
               throw new Exception($"Vulkan Error: {message}");
            }

            return new Bool32(true);
        }

        public void Dispose()
        {
            DescriptorPoolFactory.Dispose();
            CommandPoolManager.Dispose();
            PipelineManager.Dispose();
            Surface.Dispose();
            Device.Dispose();
            Instance.Dispose();
        }
    }
}
