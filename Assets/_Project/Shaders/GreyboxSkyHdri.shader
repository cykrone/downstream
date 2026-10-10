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
        _HazeHeight ("Haze band height", Range(0.01, 0.4)) = 0.1
        _HazeStrength ("Haze strength", Range(0, 1)) = 0.85
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
                float3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb * _Exposure * _Tint.rgb;
                float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                if (lum > _MaxBrightness) c *= _MaxBrightness / lum;
                // The horizon band: the panorama is stretched and soft there; the fog's own haze reads cleaner.
                float haze = (1.0 - smoothstep(0.0, _HazeHeight, abs(d.y))) * _HazeStrength;
                c = lerp(c, _HorizonHaze.rgb, haze);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
