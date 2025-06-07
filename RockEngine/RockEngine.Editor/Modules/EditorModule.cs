using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Editor.Layers;

using SimpleInjector;

namespace RockEngine.Editor.Modules
{
    public class EditorModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            container.Collection.Append<ILayer, EditorLayer>();
            //container.Collection.Append<ILayer, TitleBarLayer>();

        }
    }
}
