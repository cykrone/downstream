// Unlit, alpha-blended sprite for wakes, bow spray, drift spray and waterfall mist.
// Vertex colour (particle colour or trail gradient) times a soft texture; fogged so distant mist sits in the haze.
Shader "Downstream/Greybox Spray"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Sprite", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _SkyTint ("Shadow-side sky tint", Color) = (0.78, 0.88, 1.0, 1)
        _SoftDistance ("Soft edge against opaque geometry", Range(0.01, 2)) = 0.35
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SprayForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always // depth is tested by hand against the opaque depth texture, so the water (which writes depth) never cuts a wake
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
                float4 _SkyTint;
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
                // Hidden behind hulls, rocks and banks (the opaque depth), never behind the water surface.
                float2 screenUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float ahead = sceneEye - IN.eyeDepth;
                clip(ahead + 0.02);
                float soft = saturate(ahead / _SoftDistance);
                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                // Mist is lit mostly by the sky: a cool tint keeps it from reading as flat paper white.
                float3 colour = IN.color.rgb * tex.rgb * lerp(_SkyTint.rgb, 1.0, 0.6);
                float alpha = IN.color.a * tex.a * soft;
                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
