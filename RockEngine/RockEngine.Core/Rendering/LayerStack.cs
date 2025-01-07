using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering
{
    public interface ILayer
    {
        Task OnAttach();
        void OnDetach();
        void OnUpdate();
        void OnRender(VkCommandBuffer vkCommandBuffer);
        void OnImGuiRender(VkCommandBuffer vkCommandBuffer); // For editor UI rendering
    }

    public class LayerStack
    {
        public int Count => _layers.Count;
        private readonly List<ILayer> _layers = new List<ILayer>();

        public Task PushLayer(ILayer layer)
        {
            _layers.Add(layer);
            return layer.OnAttach();
        }

        public void PopLayer(ILayer layer)
        {
            if (_layers.Remove(layer))
            {
                layer.OnDetach();
            }
        }

        public void Update()
        {
            foreach (var layer in _layers)
            {
                layer.OnUpdate();
            }
        }

        public void Render(VkCommandBuffer vkCommandBuffer)
        {
            foreach (var layer in _layers)
            {
                layer.OnRender(vkCommandBuffer);
            }
        }

        public void RenderImGui(VkCommandBuffer commandBuffer)
        {
            foreach (var layer in _layers)
            {
                layer.OnImGuiRender(commandBuffer);
            }
        }

        /*  public void HandleEvent(Event e)
          {
              for (int i = _layers.Count - 1; i >= 0; i--)
              {
                  _layers[i].OnEvent(e);
                  if (e.Handled)
                      break;
              }
          }*/
    }

}