/// <class>Desaturate</class>
/// <description>An effect that turns the input into shades of a single color.</description>

sampler2D inputSampler : register(S0);

//-----------------------------------------------------------------------------------------
// Shader constant register mappings (scalars - float, double, Point, Color, Point3D, etc.)
//-----------------------------------------------------------------------------------------

/// <summary>Desaturates an image. the value can be betwen 0 and 1. 0 means the origianl image is returned. 1 means a monochrome image is produced.</summary>
/// <minValue>0</minValue>
/// <maxValue>1</maxValue>
/// <defaultValue>1</defaultValue>
float Strength : register(C0);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 srcColor = tex2D(inputSampler, uv);
    float3 rgb = srcColor.rgb;
    float3 luminance = lerp(rgb, dot(rgb, float3(0.30, 0.59, 0.11)), Strength);
    return float4(luminance, srcColor.a);
}