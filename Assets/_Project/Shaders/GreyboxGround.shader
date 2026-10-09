// Greybox river bed and floodable meadow in one material: stones under the water blending into grass
// up the bank by vertex colour (R = grass), so the shoreline is not cut along the mesh grid.
Shader "Downstream/Greybox Ground"
{
    Properties
    {
        _StoneColor ("Stone tint", Color) = (0.55, 0.53, 0.47, 1)
        _GrassColor ("Grass colour", Color) = (0.40, 0.50, 0.27, 1)
        _SandColor ("Waterline sand", Color) = (0.80, 0.74, 0.58, 1)
        [NoScaleOffset] _BaseMap ("Stones", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "GroundForward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _StoneColor;
                float4 _GrassColor;
                float4 _SandColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float grass : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.grass = IN.color.r;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 stones = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _StoneColor.rgb;
                float g = saturate(IN.grass);
                float3 albedo = lerp(stones, _GrassColor.rgb, g);
                // A pale strip of sand where the stones give way to grass: the waterline of the reference banks.
                float rim = 1.0 - abs(g * 2.0 - 1.0);
                albedo = lerp(albedo, _SandColor.rgb, rim * rim * 0.7);
                float3 N = normalize(IN.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float shadow = light.shadowAttenuation * light.distanceAttenuation;
                float3 ambient = SampleSHPixel(half3(0, 0, 0), N);
                float3 colour = albedo * (ambient + light.color * shadow * saturate(dot(N, light.direction)));
                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        // So the camera depth texture (used by the water's shore fade and refraction) includes the bed.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half DepthFrag(Varyings IN) : SV_Target
            {
                return IN.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
