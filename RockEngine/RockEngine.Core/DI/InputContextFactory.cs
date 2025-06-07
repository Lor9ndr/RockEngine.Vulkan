using Silk.NET.Input;
using Silk.NET.Windowing;

using SimpleInjector;

namespace RockEngine.Core.DI
{
    internal class InputContextFactory
    {
        private readonly Container _container;

        public InputContextFactory(Container container)
        {
            _container = container;
        }
        public IInputContext GetInputContext()=> _container.GetInstance<IWindow>().CreateInput();
    }
}
