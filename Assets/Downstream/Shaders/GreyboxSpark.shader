// Additive sprite for sparks, stars, glints and light trails: the hit, pickup and boost accents that
// should glow over the water rather than sit on it like mist. Same hand depth test as the spray shader
// so a spark is hidden by a hull or a rock but never cut by the water surface.
Shader "Downstream/Greybox Spark"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Sprite", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _SoftDistance ("Soft edge against opaque geometry", Range(0.01, 2)) = 0.25
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+30" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SparkForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _SoftDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float fogFactor : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv = IN.uv;
                OUT.color = IN.color * _BaseColor;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                OUT.eyeDepth = -TransformWorldToView(positionWS).z;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 screenUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float ahead = sceneEye - IN.eyeDepth;
                clip(ahead + 0.02);
                float soft = saturate(ahead / _SoftDistance);
                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                // Additive light fades out with the fog rather than turning the fog colour.
                float fogFade = ComputeFogIntensity(IN.fogFactor);
                float3 colour = IN.color.rgb * tex.rgb;
                float alpha = IN.color.a * tex.a * soft * fogFade;
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
