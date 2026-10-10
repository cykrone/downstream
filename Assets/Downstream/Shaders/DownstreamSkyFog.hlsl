// Sky-matched fog: distance fog toward the sky panorama sampled in the view direction, so land and water
// dissolve into exactly the sky behind them, in every direction and above or below the horizon. One global
// fog colour cannot do that: the HDRI horizon is paler toward the sun and darker away from it. Falls back
// to unity_FogColor when no panorama is registered (GreyboxDressing sets the globals at runtime).
#ifndef DOWNSTREAM_SKY_FOG_INCLUDED
#define DOWNSTREAM_SKY_FOG_INCLUDED

TEXTURE2D(_DownstreamSkyTex); SAMPLER(sampler_DownstreamSkyTex);
float4 _DownstreamSkyParams; // x: rotation (degrees), y: exposure, z: brightness clamp, w: 1 when set

float3 DownstreamSkyColour(float3 dirWS)
{
    if (_DownstreamSkyParams.w < 0.5) return unity_FogColor.rgb;
    float a = _DownstreamSkyParams.x * PI / 180.0;
    float s, c; sincos(a, s, c);
    float3 v = normalize(dirWS);
    float3 d = float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
    float lon = atan2(d.z, d.x);
    float lat = acos(clamp(d.y, -1.0, 1.0));
    float2 uv = float2(0.5 - lon / (2.0 * PI), 1.0 - lat / PI);
    // A slightly blurred level: fog is a soft thing, and it hides the seam column of an explicit lod.
    float3 col = SAMPLE_TEXTURE2D_LOD(_DownstreamSkyTex, sampler_DownstreamSkyTex, uv, 2.5).rgb * _DownstreamSkyParams.y;
    float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
    if (lum > _DownstreamSkyParams.z) col *= _DownstreamSkyParams.z / lum;
    return col;
}

float3 ApplySkyFog(float3 colour, float fogFactor, float3 positionWS)
{
    #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
        float intensity = ComputeFogIntensity(fogFactor);
        return lerp(DownstreamSkyColour(positionWS - _WorldSpaceCameraPos), colour, intensity);
    #else
        return colour;
    #endif
}
#endif
