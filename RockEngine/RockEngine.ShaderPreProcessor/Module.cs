using RockEngine.DI;
using RockEngine.ShaderPreprocessor;
using RockEngine.ShaderPreProcessor.Steps;
using SimpleInjector;


namespace RockEngine.ShaderPreProcessor
{

    public class ShaderPreprocessorModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            // Register annotation handlers – add any new handlers here
            container.Collection.Register<IAnnotationHandler>(
             typeof(MaterialAnnotationHandler)
         );

            // Register preprocessor steps in order
            container.Collection.Register<IShaderPreprocessorStep>(
                typeof(AnnotationProcessorStep),
                typeof(ExtensionInserterStep)
            );

            // AnnotationProcessorStep depends on all IAnnotationHandler,
            // so register it after handlers and add it as the last step
            container.Register<IShaderPreprocessorStep, AnnotationProcessorStep>();

            // Register the preprocessor itself – it receives IEnumerable<IShaderPreprocessorStep>
            container.Register<IShaderPreprocessor, MainShaderPreprocessor>(Lifestyle.Singleton);
        }
    }
}
