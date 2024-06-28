#version 450

layout(location = 0) in vec2 inPosition; // Vertex position
layout(location = 1) in vec4 inColor;    // Vertex color

layout(location = 0) out vec4 fragColor; // Output color to the fragment shader

void main()
{
    gl_Position = vec4(inPosition, 0.0, 1.0); // Convert 2D position to 4D homogeneous coordinates
    fragColor = inColor; // Pass the color to the fragment shader
}