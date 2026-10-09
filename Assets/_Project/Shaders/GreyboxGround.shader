// Greybox river bed and floodable meadow in one material: stones under the water blending into grass
// up the bank by vertex colour (R = grass), so the shoreline is not cut along the mesh grid.
Shader "Downstream/Greybox Ground"
{
    Properties
    {
        _StoneColor ("Stone tint", Color) = (0.55, 0.53, 0.47, 1)
        _GrassColor ("Grass colour", Color) = (0.40, 0.50, 0.27, 1)
        _SandColor ("Waterline sand", Color) = (0.80, 0.74, 0.58, 1)
        _EarthColor ("Earth on steep faces", Color) = (0.52, 0.40, 0.28, 1)
        _CliffColor ("Rock on steep stone faces", Color) = (0.38, 0.38, 0.40, 1)
        [NoScaleOffset] _BaseMap ("Stones", 2D) = "white" {}
        [NoScaleOffset] _DetailMap ("Soft mottling", 2D) = "gray" {}
        _DetailStrength ("Mottling strength", Range(0, 0.6)) = 0.22
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _StoneColor;
                float4 _GrassColor;
                float4 _SandColor;
                float4 _EarthColor;
                float4 _CliffColor;
                float _DetailStrength;
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
                float tint : TEXCOORD5;
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
                OUT.tint = IN.color.g;
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
                // Vertex G varies the grass a few percent so a field never reads as one flat tile.
                albedo *= 1.0 + (IN.tint - 0.5) * 0.14;
                // Two scales of soft mottling, in world space so it never stretches with the mesh.
                float m = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, IN.positionWS.xz * 0.045).r * 0.6
                        + SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, IN.positionWS.xz * 0.21 + 0.37).r * 0.4;
                albedo *= 1.0 + (m - 0.5) * 2.0 * _DetailStrength;
                float3 N = normalize(IN.normalWS);
                // Cliff risers and terrace fronts show earth; tops stay grass (the reference's layered banks).
                float steep = smoothstep(0.32, 0.58, 1.0 - N.y);
                float3 steepColour = lerp(_CliffColor.rgb, _EarthColor.rgb, g) * (0.85 + 0.3 * m);
                albedo = lerp(albedo, steepColour, steep);
                Light light = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float shadow = light.shadowAttenuation * light.distanceAttenuation;
                float aoDirect = 1.0, aoIndirect = 1.0;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(IN.positionCS));
                    aoDirect = ao.directAmbientOcclusion;
                    aoIndirect = ao.indirectAmbientOcclusion;
                #endif
                float3 ambient = SampleSHPixel(half3(0, 0, 0), N) * aoIndirect;
                float3 colour = albedo * (ambient + light.color * shadow * saturate(dot(N, light.direction)) * aoDirect);
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
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings DNVert(Attributes IN) { Varyings OUT; OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz); OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS); return OUT; }
            half4 DNFrag(Varyings IN) : SV_Target { return half4(normalize(IN.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
