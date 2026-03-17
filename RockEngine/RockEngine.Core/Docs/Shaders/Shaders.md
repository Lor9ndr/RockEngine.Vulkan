## Overview

Writing
 texture bindings manually is error‑prone and becomes repetitive when
switching between bindless and legacy pipelines. The `[MATERIAL]` annotation allows you to **declare the textures used by a material** in a simple block. During shader compilation, the annotation is replaced with the appropriate GLSL code:

* **Bindless mode**  generates a push‑constant block with index variables and a global `sampler2D` array, plus helper sampling functions. (if gpu supports that)
* **Legacy mode** : generates individual descriptor bindings (one per texture) and corresponding helper functions.

This keeps your shader code clean and portable.

## Syntax

Place the annotation anywhere in your GLSL shader (typically near the top, after `#version` and includes). The block must be enclosed in curly braces and contain a list of texture declarations, each on its own line:

**glsl**

```
[MATERIAL]
{
    Texture2D Albedo;
    Texture2D Normal;
    Texture2D MRA;
}
```

* Each line ends with a semicolon (`,` also works but semicolon is recommended for GLSL familiarity).
* Supported texture types: `Texture2D`, `Texture3D`, `TextureCube`, `Texture2DArray` (extend as needed).
* The names (`Albedo`, `Normal`, …) become the base for generated identifiers.

## Generated Code

The preprocessor transforms the annotation into real GLSL code. Below are examples for both modes.

### Bindless Mode (with `-DBINDLESS_SUPPORTED`)

**glsl**

```sha
// Automatically generated from [MATERIAL] annotation
#ifdef BINDLESS_SUPPORTED
layout(set = MATERIAL_SET, binding = 0) uniform sampler2D uBindlessTextures[];
layout(push_constant) uniform MaterialIndices
{
    uint AlbedoIndex;
    uint NormalIndex;
    uint MRAIndex;
} material;

vec4 sampleAlbedo(vec2 uv) { return texture(uBindlessTextures[nonuniformEXT(material.AlbedoIndex)], uv); }
vec4 sampleNormal(vec2 uv) { return texture(uBindlessTextures[nonuniformEXT(material.NormalIndex)], uv); }
vec4 sampleMRA(vec2 uv)   { return texture(uBindlessTextures[nonuniformEXT(material.MRAIndex)], uv); }
#else
...
#endif
```

* The global bindless array is bound at set = `MATERIAL_SET`, binding = 0.
* Push constants hold the index for each texture.
* Helper functions use `nonuniformEXT` to handle non‑uniform indices (required by Vulkan).

### Legacy Mode (without `BINDLESS_SUPPORTED`)

**glsl**

```
// Automatically generated from [MATERIAL] annotation
layout(set = MATERIAL_SET, binding = 0) uniform sampler2D uAlbedo;
layout(set = MATERIAL_SET, binding = 1) uniform sampler2D uNormal;
layout(set = MATERIAL_SET, binding = 2) uniform sampler2D uMRA;

vec4 sampleAlbedo(vec2 uv) { return texture(uAlbedo, uv); }
vec4 sampleNormal(vec2 uv) { return texture(uNormal, uv); }
vec4 sampleMRA(vec2 uv)   { return texture(uMRA, uv); }
```

* Each texture gets a dedicated binding slot, starting at 0.
* Helper functions directly sample the respective sampler.

## Using the Helpers in Your Shader

After
 the annotation, you can call the generated functions instead of writing
 explicit texture lookups. This makes the shader independent of the
underlying binding mechanism.

**Example (fragment shader snippet):**

**glsl**

```c
void main()
{
    vec4 albedo = sampleAlbedo(vTexCoord);
    vec3 normal = sampleNormal(vTexCoord).xyz * 2.0 - 1.0;
    float metallic = sampleMRA(vTexCoord).r;
    // ...
}
```

The helper functions are named `sample<TextureName>` with the exact casing you used in the annotation.
