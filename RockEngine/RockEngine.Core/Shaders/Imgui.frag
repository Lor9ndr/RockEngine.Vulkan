#version 450 core
#include "include/common.glsl"
#extension GL_EXT_nonuniform_qualifier : require
layout(location = 0) out vec4 fColor;

layout(set = MATERIAL_SET , binding = 0) uniform sampler2D uTextures[];

layout(push_constant, std430) uniform uPushConstantFrag
{ 
    layout(offset = 16) uint textureIndex;   // place after vertex push constants
} pc;

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
    fColor = In.Color * texture(uTextures[nonuniformEXT(pc.textureIndex)], In.UV.st);
     if (fColor.a <= 0.0)
     {
        discard;
     }
    // Convert from sRGB to linear
    fColor.rgb = srgb_to_linear(fColor.rgb);
}

