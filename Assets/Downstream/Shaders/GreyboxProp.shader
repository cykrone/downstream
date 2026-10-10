// Props of the greybox kit (canopies, pines, rocks, trunks): the design's form shading rule, two or
// three tones per form with the tops brightest and the shadow sides darkest, a touch of wrap lighting
// so rounded forms stay soft, screen-space occlusion, shadows and fog. One warm key, cool sky fill.
Shader "Downstream/Greybox Prop"
{
    Properties
    {
        _BaseColor ("Side colour", Color) = (0.36, 0.56, 0.26, 1)
        _TopColor ("Top colour", Color) = (0.52, 0.70, 0.32, 1)
        _ShadeColor ("Underside colour", Color) = (0.22, 0.36, 0.20, 1)
        _Wrap ("Light wrap", Range(0, 1)) = 0.35
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "PropForward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopColor;
                float4 _ShadeColor;
                float _Wrap;
                float _Smoothness;
            CBUFFER_END

            #include "DownstreamSkyFog.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                // Three tones by facing: tops, sides, undersides.
                float up = N.y;
                float3 albedo = up >= 0.0 ? lerp(_BaseColor.rgb, _TopColor.rgb, smoothstep(0.1, 0.8, up))
                                          : lerp(_BaseColor.rgb, _ShadeColor.rgb, smoothstep(0.0, 0.7, -up));

                Light light = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float shadow = light.shadowAttenuation * light.distanceAttenuation;
                float2 screenUv = GetNormalizedScreenSpaceUV(IN.positionCS);
                float aoDirect = 1.0, aoIndirect = 1.0;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUv);
                    aoDirect = ao.directAmbientOcclusion;
                    aoIndirect = ao.indirectAmbientOcclusion;
                #endif
                float ndl = saturate((dot(N, light.direction) + _Wrap) / (1.0 + _Wrap));
                float3 ambient = SampleSHPixel(half3(0, 0, 0), N) * aoIndirect;
                float3 colour = albedo * (ambient + light.color * shadow * ndl * aoDirect);

                // A small soft highlight so leaves and wet rock are not dead matte.
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                float3 H = normalize(light.direction + V);
                float spec = pow(saturate(dot(N, H)), exp2(6.0 * _Smoothness + 2.0)) * _Smoothness * 0.5;
                colour += light.color * shadow * spec;

                colour = ApplySkyFog(colour, IN.fogFactor, IN.positionWS);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

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
            Varyings DepthVert(Attributes IN) { Varyings OUT; OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz); return OUT; }
            half DepthFrag(Varyings IN) : SV_Target { return IN.positionCS.z; }
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
