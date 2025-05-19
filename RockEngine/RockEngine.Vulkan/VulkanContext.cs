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
        public static Vk Vk = Vk.GetApi();
        public VkLogicalDevice Device { get; }
        public VkInstance Instance { get; }
        public ISurfaceHandler Surface { get; }
        public int MaxFramesPerFlight { get; internal set; }
        public SamplerCache SamplerCache { get; }
        public SubmitContext SubmitContext { get; }
        public SubmitContext SubmitComputeContext { get; }

        public DebugUtilsFunctions DebugUtils => _debugUtilsFunctions;

        private static VulkanContext? _renderingContext;

        public static VulkanContext GetCurrent() => _renderingContext ?? throw new InvalidOperationException("Rendering context was not created yet");

        public static ref AllocationCallbacks CustomAllocator<T>() => ref Vulkan.CustomAllocator.CreateCallbacks<T>();

        private static readonly string[] _validationLayers = ["VK_LAYER_KHRONOS_validation"];
        private static DebugUtilsMessengerCallbackFunctionEXT _debugCallback;
        private readonly Stack<IDisposable> _pendingDisposals = new Stack<IDisposable>();
        private readonly ThreadLocal<VkCommandPool> _threadCommandPools;
        private readonly DebugUtilsFunctions _debugUtilsFunctions;

        public VulkanContext(IWindow window, string appName, int maxFramesPerFlight = 3)
        {
            if (_renderingContext is not null)
            {
                // Have to think about supporting multiple windows with different contexts
                throw new NotSupportedException("For now it is unsupported to have multiple contexts");
            }
            Instance = CreateInstance(window, appName);
            Surface = CreateSurface(window);
            Device = CreateDevice(Surface, Instance, this);
            _debugUtilsFunctions = new DebugUtilsFunctions(Vk, Device);
            
            Device.NameQueues();

            MaxFramesPerFlight = maxFramesPerFlight;
            _renderingContext = this;
            SamplerCache = new SamplerCache(this);
            SubmitContext = new SubmitContext(this, Device.GraphicsQueue);
            SubmitComputeContext = new SubmitContext(this, Device.ComputeQueue);

            _threadCommandPools = new ThreadLocal<VkCommandPool>(() => CreateCommandPool(CommandPoolCreateFlags.TransientBit));
        }
        public VkCommandPool GetThreadLocalCommandPool() => _threadCommandPools!.Value!;

        private VkCommandPool CreateCommandPool(CommandPoolCreateFlags createFlags)
        {
            return VkCommandPool.Create(this, createFlags, Device.QueueFamilyIndices.GraphicsFamily.Value);
        }

        private SDLSurfaceHandler CreateSurface(IWindow window)
        {
            return SDLSurfaceHandler.CreateSurface(window, this);
        }

        private static unsafe VkInstance CreateInstance(IWindow surface, string appName)
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


            var extensions = surface.VkSurface.GetRequiredExtensions(out uint countExtensions);
            ci.PpEnabledExtensionNames = extensions;
            ci.EnabledExtensionCount = countExtensions;

            var instance = new VkInstanceBuilder()
                .UseValidationLayers(_validationLayers)
                .UseDebugUtilsMessenger(DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt | DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt | DebugUtilsMessageSeverityFlagsEXT.InfoBitExt,
                                        DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt,
                                        dbcallback, (void*)nint.Zero)
                .Build(ref ci);

            Marshal.FreeHGlobal((nint)appname);
            return instance;
        }
        private static unsafe uint DebugUtilsMessengerCallbackFunctionEXT(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
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

            Console.WriteLine($"{messageSeverity} ||| {message}\n{Environment.StackTrace}");

            // Reset console color to default
            Console.ResetColor();

            // Throw an exception if severity is ErrorBitEXT
            if (messageSeverity == DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt)
            {
                throw new Exception(message: $"Vulkan Error: {message}");
            }

            return new Bool32(false);
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
