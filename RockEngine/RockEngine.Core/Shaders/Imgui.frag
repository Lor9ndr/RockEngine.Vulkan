#version 450 core
#extension GL_KHR_vulkan_glsl : enable

layout(location = 0) out vec4 fColor;

layout(set=0, binding=0) uniform sampler2D sTexture;

layout(location = 0) in struct { vec4 Color; vec2 UV; } In;

vec3 srgb_to_linear(vec3 srgb) {
    return mix(
        srgb / 12.92,
        pow((srgb + 0.055) / 1.055, vec3(2.4)),
        step(0.04045, srgb)
    );
}
void main()
{
    fColor = In.Color * texture(sTexture, In.UV.st);
     if (fColor.a <= 0.0)
        discard;
    // Convert from sRGB to linear
    fColor.rgb = srgb_to_linear(fColor.rgb);
}