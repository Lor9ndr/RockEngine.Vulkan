using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public class MaterialPipelineAnalyzer
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public void AnalyzeAndBindMaterial(MeshRenderer renderer, MaterialAsset materialAsset)
        {
            try
            {
                var material = materialAsset.GetAsync().Result;
                if (material == null)
                {
                    return;
                }

                // Analyze pipeline dependencies
                foreach (var pass in material.Passes.Values)
                {
                    AnalyzePipelineDependencies(pass.Pipeline);
                }

                // Auto-bind common resources
                AutoBindCommonResources(material, renderer);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to analyze and bind material dependencies");
            }
        }

        private void AnalyzePipelineDependencies(RckPipeline pipeline)
        {
            var layout = pipeline.Layout;

            _logger.Info($"Analyzing pipeline: {pipeline.Name}");
            _logger.Info($"Push constants: {layout.PushConstantRanges.Length}");

            foreach (var pushConstant in layout.PushConstantRanges)
            {
                _logger.Info($"  PushConstant: {pushConstant.Name}, Size: {pushConstant.Size}, Stage: {pushConstant.StageFlags}");
            }

            foreach (var setLayout in layout.DescriptorSetLayouts)
            {
                _logger.Info($"Descriptor Set {setLayout.Key}:");
                foreach (var binding in setLayout.Value.Bindings)
                {
                    _logger.Info($"  Binding {binding.Binding}: {binding.DescriptorType} ({binding.StageFlags}) - {binding.Name}");
                }
            }
        }

        public void AutoBindResource(Material material, string resourceName, object resourceValue)
        {
            if (material == null || resourceValue == null)
            {
                return;
            }

            foreach (var pass in material.Passes.Values)
            {
                AutoBindResourceToPass(pass, resourceName, resourceValue);
            }
        }

        private void AutoBindResourceToPass(MaterialPass pass, string resourceName, object resourceValue)
        {
            var layout = pass.Pipeline.Layout;

            // Try to find matching binding by name
            foreach (var setLayout in layout.DescriptorSetLayouts)
            {
                foreach (var binding in setLayout.Value.Bindings)
                {
                    if (IsNameMatch(binding.Name, resourceName))
                    {
                        BindResourceToSlot(pass, setLayout.Key, binding.Binding, resourceValue);
                        return;
                    }
                }
            }

            // Fallback: try to bind by type
            AutoBindByType(pass, resourceName, resourceValue);
        }

        private bool IsNameMatch(string bindingName, string resourceName)
        {
            // Simple name matching - can be enhanced with patterns
            return bindingName.Contains(resourceName, StringComparison.OrdinalIgnoreCase) ||
                   resourceName.Contains(bindingName, StringComparison.OrdinalIgnoreCase);
        }

        private void BindResourceToSlot(MaterialPass pass, uint set, uint binding, object resource)
        {
            try
            {
                switch (resource)
                {
                    case Texture2D texture:
                        pass.BindResource(new TextureBinding(set, binding, 0, 1, texture));
                        _logger.Info($"Bound texture to set {set}, binding {binding}");
                        break;
                    case Texture texture:
                        pass.BindResource(new TextureBinding(set, binding, 0, 1, texture));
                        _logger.Info($"Bound texture to set {set}, binding {binding}");
                        break;
                        // Add more resource types as needed
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to bind resource to set {set}, binding {binding}");
            }
        }

        private void AutoBindByType(MaterialPass pass, string resourceName, object resourceValue)
        {
            // Implement type-based auto-binding logic
            // This can use naming conventions or configuration
        }

        private void AutoBindCommonResources(Material material, MeshRenderer renderer)
        {
            // Auto-bind common resources like transform matrices, cameras, etc.
            // This would depend on your engine's common binding conventions
        }
    }
}