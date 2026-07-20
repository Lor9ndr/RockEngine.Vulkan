using ImGuiNET;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering.Texturing;
using Silk.NET.Vulkan;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(SamplerState))]
    public class SamplerStateHandler : BasePropertyHandler<SamplerState>
    {
        private static readonly string[] FilterNames = { "Nearest", "Linear" };
        private static readonly TextureFilter[] FilterValues = { TextureFilter.Nearest, TextureFilter.Linear };

        private static readonly string[] WrapNames = { "Repeat", "MirroredRepeat", "ClampToEdge", "ClampToBorder" };
        private static readonly TextureWrap[] WrapValues = { TextureWrap.Repeat, TextureWrap.MirroredRepeat, TextureWrap.ClampToEdge, TextureWrap.ClampToBorder };

        private static readonly string[] CompareOpNames = { "Never", "Less", "Equal", "LessOrEqual", "Greater", "NotEqual", "GreaterOrEqual", "Always" };
        private static readonly CompareOp[] _compareOpValues = { CompareOp.Never, CompareOp.Less, CompareOp.Equal, CompareOp.LessOrEqual, CompareOp.Greater, CompareOp.NotEqual, CompareOp.GreaterOrEqual, CompareOp.Always };

        private static readonly string[] BorderColorNames = { "FloatTransparentBlack", "IntTransparentBlack", "FloatOpaqueBlack", "IntOpaqueBlack", "FloatOpaqueWhite", "IntOpaqueWhite" };
        private static readonly BorderColor[] BorderColorValues = { BorderColor.FloatTransparentBlack, BorderColor.IntTransparentBlack, BorderColor.FloatOpaqueBlack, BorderColor.IntOpaqueBlack, BorderColor.FloatOpaqueWhite, BorderColor.IntOpaqueWhite };

        protected override void DrawProperty(object owner, UIPropertyAccessor accessor, SamplerState value, PropertyDrawer drawer)
        {
            bool changed = false;

            changed |= DrawEnumCombo("Min Filter", value.MinFilter, FilterNames, FilterValues, f => value.MinFilter = f);
            changed |= DrawEnumCombo("Mag Filter", value.MagFilter, FilterNames, FilterValues, f => value.MagFilter = f);
            changed |= DrawEnumCombo("Mip Filter", value.MipFilter, FilterNames, FilterValues, f => value.MipFilter = f);

            ImGui.Separator();

            changed |= DrawEnumCombo("Address U", value.AddressModeU, WrapNames, WrapValues, w => value.AddressModeU = w);
            changed |= DrawEnumCombo("Address V", value.AddressModeV, WrapNames, WrapValues, w => value.AddressModeV = w);
            changed |= DrawEnumCombo("Address W", value.AddressModeW, WrapNames, WrapValues, w => value.AddressModeW = w);

            ImGui.Separator();

            float mipLodBias = value.MipLodBias;
            if (ImGui.DragFloat("Mip LOD Bias", ref mipLodBias, 0.01f))
            {
                value.MipLodBias = mipLodBias;
                changed = true;
            }

            bool anisotropy = value.AnisotropyEnable;
            if (ImGui.Checkbox("Anisotropy Enable", ref anisotropy))
            {
                value.AnisotropyEnable = anisotropy;
                changed = true;
            }

            if (anisotropy)
            {
                float maxAniso = value.MaxAnisotropy;
                if (ImGui.SliderFloat("Max Anisotropy", ref maxAniso, 1.0f, 16.0f))
                {
                    value.MaxAnisotropy = maxAniso;
                    changed = true;
                }
            }

            bool compareEnable = value.CompareEnable;
            if (ImGui.Checkbox("Compare Enable", ref compareEnable))
            {
                value.CompareEnable = compareEnable;
                changed = true;
            }

            if (compareEnable)
            {
                changed |= DrawEnumCombo("Compare Op", value.CompareOp, CompareOpNames, _compareOpValues, op => value.CompareOp = op);
            }

            float minLod = value.MinLod;
            if (ImGui.DragFloat("Min LOD", ref minLod, 0.1f))
            {
                value.MinLod = minLod;
                changed = true;
            }

            float maxLod = value.MaxLod;
            if (ImGui.DragFloat("Max LOD", ref maxLod, 0.1f))
            {
                value.MaxLod = maxLod;
                changed = true;
            }

            changed |= DrawEnumCombo("Border Color", value.BorderColor, BorderColorNames, BorderColorValues, bc => value.BorderColor = bc);

            bool unnormalized = value.UnnormalizedCoordinates;
            if (ImGui.Checkbox("Unnormalized Coordinates", ref unnormalized))
            {
                value.UnnormalizedCoordinates = unnormalized;
                changed = true;
            }

            if (changed)
            {
                drawer.OnSamplerStateChanged?.Invoke(value);
            }
        }

        private static bool DrawEnumCombo<T>(string label, T current, string[] names, T[] values, System.Action<T> setter)
            where T : struct, System.Enum
        {
            int index = Array.IndexOf(values, current);
            if (index < 0)
            {
                index = 0;
            }

            if (ImGui.Combo(label, ref index, names, names.Length))
            {
                if (index >= 0 && index < values.Length)
                {
                    setter(values[index]);
                    return true;
                }
            }
            return false;
        }
    }
}