#version 450

layout(location = 0) in vec4 fragColor; // Input color from the vertex shader

layout(location = 0) out vec4 outColor; // Output color

void main()
{
    outColor = fragColor; // Set the output color to the input color
}