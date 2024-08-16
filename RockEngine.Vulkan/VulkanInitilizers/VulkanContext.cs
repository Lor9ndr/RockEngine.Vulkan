using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.VulkanInitilizers
{
    public unsafe class VulkanContext : IVulkanContext
    {
        public Vk Api { get; private set; }
        public InstanceWrapper Instance { get; private set; }
        public LogicalDeviceWrapper Device { get; private set; }
        public CommandPoolManager CommandPoolManager { get; private set;}
        public DescriptorPoolFactory DescriptorPoolFactory { get; }
        public ISurfaceHandler Surface { get; private set;}

        public Mutex QueueMutex = new Mutex();

        private readonly IWindow _window;
        private readonly string[] _validationLayers = ["VK_LAYER_KHRONOS_validation"];
        private DebugUtilsMessengerCallbackFunctionEXT _debugCallback;
        
        /// <summary>
        /// Max frames in flight, for now it has a bug with more than 1 frame in flight
        /// </summary>
        public const int MAX_FRAMES_IN_FLIGHT = 1;

        public VulkanContext(IWindow window, string appName)
        {
            _window = window;
            ArgumentNullException.ThrowIfNull(_window.VkSurface);

            Api = Vk.GetApi();
            CreateInstance(appName);

            Surface = SDLSurfaceHandler.CreateSurface(_window, this);
            CreateDevice();
            CommandPoolManager = new CommandPoolManager(this);
            DescriptorPoolFactory = new DescriptorPoolFactory(this);

            IoC.Container.RegisterInstance(this);
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

            _debugCallback = DebugUtilsMessengerCallbackFunctionEXT;
            IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_debugCallback);


            PfnDebugUtilsMessengerCallbackEXT dbcallback = new PfnDebugUtilsMessengerCallbackEXT(
                (delegate* unmanaged[Cdecl]<DebugUtilsMessageSeverityFlagsEXT, DebugUtilsMessageTypeFlagsEXT, DebugUtilsMessengerCallbackDataEXT*, void*, Bool32>)callbackPtr);


            var extensions = _window.VkSurface!.GetRequiredExtensions(out uint countExtensions);
            ci.PpEnabledExtensionNames = extensions;
            ci.EnabledExtensionCount = countExtensions;

            Instance = new VulkanInstanceBuilder(Api)
                .UseValidationLayers(_validationLayers)
                .UseDebugUtilsMessenger(DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt | DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt | DebugUtilsMessageSeverityFlagsEXT.InfoBitExt,
                                        DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt,
                                        dbcallback, (void*)nint.Zero)
                .Build(ref ci);

            Marshal.FreeHGlobal((nint)appname);
            Api.CurrentInstance = Instance;
        }

       

        private void CreateDevice()
        {
            var device = PhysicalDeviceWrapper.Create(this);
            Device = LogicalDeviceWrapper.Create(Api, device, Surface, KhrSwapchain.ExtensionName);
            Api.CurrentDevice = Device;
        }

        private unsafe uint DebugUtilsMessengerCallbackFunctionEXT(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
        {
            var message = Marshal.PtrToStringUTF8((nint)pCallbackData->PMessage);

            // Change console color based on severity
            switch (messageSeverity)
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

            Console.WriteLine($"{messageSeverity} ||| {message}");

            // Reset console color to default
            Console.ResetColor();

            // Throw an exception if severity is ErrorBitEXT
            if (messageSeverity == DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt)
            {
               throw new Exception(message: $"Vulkan Error: {message}");
            }

            return new Bool32(true);
        }

        public void Dispose()
        {
            DescriptorPoolFactory.Dispose();
            CommandPoolManager.Dispose();
            Surface.Dispose();
            Device.Dispose();
            Instance.Dispose();
        }
    }
}
