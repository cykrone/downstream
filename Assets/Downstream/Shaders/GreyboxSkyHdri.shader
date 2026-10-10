// Lat-long HDRI skybox with a brightness clamp: the sun in a real sky is thousands of times brighter
// than the clouds, which blows the bloom and the ambient probe out; the clamp keeps the disc and its
// glow but caps them at a value the tonemapper can hold.
Shader "Downstream/Greybox Sky HDRI"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Panorama (lat-long)", 2D) = "grey" {}
        _Exposure ("Exposure", Range(0, 4)) = 1
        _Rotation ("Rotation", Range(0, 360)) = 0
        _MaxBrightness ("Brightness clamp", Range(1, 64)) = 6
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _HorizonHaze ("Horizon haze (fog colour)", Color) = (0.76, 0.84, 0.91, 1)
        _HazeHeight ("Haze band height", Range(0.01, 0.4)) = 0.035
        _HazeStrength ("Haze strength", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Exposure, _Rotation, _MaxBrightness, _HazeHeight, _HazeStrength;
                float4 _Tint, _HorizonHaze;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            float3 RotateAroundY(float3 v, float degrees)
            {
                float a = degrees * PI / 180.0;
                float s, c; sincos(a, s, c);
                return float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = RotateAroundY(IN.positionOS.xyz, _Rotation);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Unity's panoramic mapping: u = 0.5 - atan2(z, x) / 2pi, v = 1 - acos(y) / pi.
                float3 d = normalize(IN.dir);
                float lon = atan2(d.z, d.x);
                float lat = acos(clamp(d.y, -1.0, 1.0));
                float2 uv = float2(0.5 - lon / (2.0 * PI), 1.0 - lat / PI);
                // u wraps at the seam, so its screen derivative jumps by a whole texture there and the
                // sampler would drop to the smallest mip along one pixel column: wrap the derivatives too.
                float2 dx = ddx(uv), dy = ddy(uv);
                dx.x = frac(dx.x + 0.5) - 0.5; dy.x = frac(dy.x + 0.5) - 0.5;
                float3 c = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, dx, dy).rgb * _Exposure * _Tint.rgb;
                float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                if (lum > _MaxBrightness) c *= _MaxBrightness / lum;
                // A thin haze line where the fogged land meets the sky, so the two join without a seam; below
                // the horizon the panorama's own ground never shows (the far ground covers it, and anything
                // that slipped through would be a stretched smear), only the fog colour.
                float haze = (1.0 - smoothstep(0.0, _HazeHeight, d.y)) * _HazeStrength;
                haze = max(haze, saturate(-d.y * 60.0));
                c = lerp(c, _HorizonHaze.rgb, haze);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
