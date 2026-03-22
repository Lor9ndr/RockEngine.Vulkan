using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NLog;
using RockEngine.Vulkan.Builders;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace RockEngine.Vulkan
{
    public class VulkanContext : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static readonly Vk Vk = Vk.GetApi();
        public VkLogicalDevice Device { get; }
        public VkInstance Instance { get; }
        public ISurfaceHandler Surface { get; }
        public int MaxFramesPerFlight { get; internal set; }
        public SamplerCache SamplerCache { get; }
        public SubmitContext GraphicsSubmitContext { get; }
        public SubmitContext ComputeSubmitContext { get; }
        public SubmitContext TransferSubmitContext { get; }
        public SubmitContext PresentSubmitContext { get; }
        public FeatureRegistry FeatureRegistry { get; }

        public DebugUtilsFunctions DebugUtils => _debugUtilsFunctions;

        private static VulkanContext? _singleton;

        public static VulkanContext GetCurrent() => _singleton ?? throw new InvalidOperationException("Rendering context was not created yet");

        public static ref AllocationCallbacks CustomAllocator<T>() => ref VulkanAllocator.CreateCallbacks<T>();

        private static readonly string[] _validationLayers = ["VK_LAYER_KHRONOS_validation"];
        private static DebugUtilsMessengerCallbackFunctionEXT _debugCallback;
        private readonly Stack<IDisposable> _pendingDisposals = new Stack<IDisposable>();
        private readonly DebugUtilsFunctions _debugUtilsFunctions;
        private readonly AppSettings _settings;
        public readonly Vk Api = Vk;

        public VulkanContext(IWindow? window, AppSettings settings, FeatureRegistry featureRegistry)
        {
            _settings = settings;
            FeatureRegistry = featureRegistry;
            Instance = CreateInstance(window, _settings);
            Surface = window != null ? CreateSurface(window) : null!; // Surface may be null
            Device = CreateDevice(Surface, Instance, this);
            _debugUtilsFunctions = new DebugUtilsFunctions(Vk, Device, _settings);

            Device.NameQueues();

            MaxFramesPerFlight = _settings.MaxFramesPerFlight;
            _singleton = this;
            SamplerCache = new SamplerCache(this);

            GraphicsSubmitContext = new SubmitContext(this, Device.GraphicsQueue);
            ComputeSubmitContext = new SubmitContext(this, Device.ComputeQueue);
            TransferSubmitContext = new SubmitContext(this, Device.TransferQueue);

            // Present queue is only created if a surface exists
            if (Device.PresentQueue != null)
            {
                PresentSubmitContext = new SubmitContext(this, Device.PresentQueue);
            }

            _settings = settings;
        }


        private SurfaceHandler CreateSurface(IWindow window)
        {
            return SurfaceHandler.CreateSurface(window, this);
        }

        private static unsafe VkInstance CreateInstance(IWindow? window, AppSettings appSettings)
        {
            var appname = (byte*)Marshal.StringToHGlobalAnsi(appSettings.Name);
            var appInfo = new ApplicationInfo()
            {
                ApiVersion = Vk.MakeVersion(1, 4, 312),
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
            var dbcallback = new PfnDebugUtilsMessengerCallbackEXT(
                (delegate* unmanaged[Cdecl]<DebugUtilsMessageSeverityFlagsEXT, DebugUtilsMessageTypeFlagsEXT, DebugUtilsMessengerCallbackDataEXT*, void*, Bool32>)callbackPtr);

            // Determine instance extensions
            List<string> extensionsList = new List<string>();

            if (window != null && window.VkSurface is not null)
            {
                // Use window's required surface extensions
                var extPtr = window.VkSurface.GetRequiredExtensions(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    extensionsList.Add(Marshal.PtrToStringAnsi((nint)extPtr[i])!);
                }
            }
            else
            {
                // Headless: enable only essential extensions (e.g., for debug utils and device properties)
                extensionsList.Add("VK_KHR_get_physical_device_properties2");
                // If validation layers are enabled, we also need debug utils extension
                if (appSettings.EnableValidationLayers)
                {
                    extensionsList.Add("VK_EXT_debug_utils");
                }
            }

            var extensionsArray = extensionsList.Distinct().ToArray();
            var ppExtensions = SilkMarshal.StringArrayToPtr(extensionsArray);
            ci.PpEnabledExtensionNames = (byte**)ppExtensions;
            ci.EnabledExtensionCount = (uint)extensionsArray.Length;

            var instanceBuilder = new VkInstanceBuilder();

            if (appSettings.EnableValidationLayers)
            {
                instanceBuilder.UseValidationLayers(_validationLayers)
                    .UseDebugUtilsMessenger(DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt | DebugUtilsMessageSeverityFlagsEXT.InfoBitExt,
                                            DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt,
                                            ref dbcallback, (void*)nint.Zero);
            }

            var instance = instanceBuilder.Build(ref ci);
            SilkMarshal.Free(ppExtensions);
            Marshal.FreeHGlobal((nint)appname);
            return instance;
        }
        private static unsafe uint DebugUtilsMessengerCallbackFunctionEXT(DebugUtilsMessageSeverityFlagsEXT messageSeverity,
            DebugUtilsMessageTypeFlagsEXT messageTypes,
            DebugUtilsMessengerCallbackDataEXT* pCallbackData,
            void* pUserData)
        {
            // Extract main message
            string message = Marshal.PtrToStringUTF8((nint)pCallbackData->PMessage) ?? string.Empty;

            // Extract message ID (if available)
            string messageIdName = pCallbackData->PMessageIdName != null
                ? Marshal.PtrToStringUTF8((nint)pCallbackData->PMessageIdName) ?? "unknown"
                : "unknown";
            int messageIdNumber = pCallbackData->MessageIdNumber;

            // Build detailed log message
            var sb = new StringBuilder();
            sb.AppendLine($"Vulkan Validation [{messageIdName} (0x{messageIdNumber:X8})]: {message}");

            // Add related objects
            if (pCallbackData->ObjectCount > 0 && pCallbackData->PObjects != null)
            {
                sb.AppendLine("  Related objects:");
                for (uint i = 0; i < pCallbackData->ObjectCount; i++)
                {
                    var obj = pCallbackData->PObjects[i];
                    string objType = obj.ObjectType.ToString(); // enum name
                    ulong handle = obj.ObjectHandle;
                    string objName = obj.PObjectName != null
                        ? Marshal.PtrToStringUTF8((nint)obj.PObjectName) ?? ""
                        : "";

                    sb.Append($"    - Type: {objType}, Handle: 0x{handle:X16}");
                    if (!string.IsNullOrEmpty(objName))
                        sb.Append($", Name: \"{objName}\"");
                    sb.AppendLine();
                }
            }

            // Add queue labels
            if (pCallbackData->QueueLabelCount > 0 && pCallbackData->PQueueLabels != null)
            {
                sb.AppendLine("  Queue labels:");
                for (uint i = 0; i < pCallbackData->QueueLabelCount; i++)
                {
                    var label = pCallbackData->PQueueLabels[i];
                    string labelName = label.PLabelName != null
                        ? Marshal.PtrToStringUTF8((nint)label.PLabelName) ?? "unnamed"
                        : "unnamed";
                    sb.AppendLine($"    - \"{labelName}\" (color: [{label.Color[0]}, {label.Color[1]}, {label.Color[2]}, {label.Color[3]}])");
                }
            }

            // Add command buffer labels
            if (pCallbackData->CmdBufLabelCount > 0 && pCallbackData->PCmdBufLabels != null)
            {
                sb.AppendLine("  Command buffer labels:");
                for (uint i = 0; i < pCallbackData->CmdBufLabelCount; i++)
                {
                    var label = pCallbackData->PCmdBufLabels[i];
                    string labelName = label.PLabelName != null
                        ? Marshal.PtrToStringUTF8((nint)label.PLabelName) ?? "unnamed"
                        : "unnamed";
                    sb.AppendLine($"    - \"{labelName}\"");
                }
            }

            // Map severity to log level
            LogLevel logLevel = messageSeverity switch
            {
                DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt => LogLevel.Error,
                DebugUtilsMessageSeverityFlagsEXT.WarningBitExt => LogLevel.Warn,
                DebugUtilsMessageSeverityFlagsEXT.InfoBitExt => LogLevel.Info,
                DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt => LogLevel.Trace,
                _ => LogLevel.Debug
            };

            // Log the detailed message
            _logger.Log(logLevel, sb.ToString());

            if (messageSeverity == DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt)
            {
                Debugger.Break();
                throw new VulkanException(messageSeverity, sb.ToString());
            }

            return Vk.False; // Returning false tells the messenger to continue normal operation
        }


        private static VkLogicalDevice CreateDevice(ISurfaceHandler surface, VkInstance instance, VulkanContext vulkanContext)
        {
            var device = VkPhysicalDevice.Create(instance);
            return VkLogicalDevice.Create(vulkanContext, device, surface, KhrSwapchain.ExtensionName);
        }

        public void Dispose()
        {
            GraphicsSubmitContext.Dispose();
            TransferSubmitContext.Dispose();
            ComputeSubmitContext.Dispose();
            PresentSubmitContext?.Dispose();
            Surface?.Dispose();
            Device.Dispose();
            Instance.Dispose();
        }
    }
}
