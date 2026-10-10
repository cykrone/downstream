// River bed, floodable meadow and hills in one material. Vertex colour R blends stones (in the channel)
// through the meadow to grass, so the shoreline is never cut along the mesh grid; steep faces show earth
// under grass and rock under stones. With the CC0 texture set wired (_UseTextures) the layers carry real
// albedo and normal detail, tinted toward the art-directed palette; without it the flat colours remain.
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
        [Toggle] _UseTextures ("Use texture layers", Float) = 0
        [NoScaleOffset] _GrassMap ("Grass albedo", 2D) = "gray" {}
        [NoScaleOffset] _GrassNormal ("Grass normal", 2D) = "bump" {}
        [NoScaleOffset] _MeadowMap ("Meadow albedo (near the water)", 2D) = "gray" {}
        [NoScaleOffset] _MeadowNormal ("Meadow normal", 2D) = "bump" {}
        [NoScaleOffset] _EarthMap ("Earth albedo", 2D) = "gray" {}
        [NoScaleOffset] _EarthNormal ("Earth normal", 2D) = "bump" {}
        [NoScaleOffset] _RockMap ("Rock albedo", 2D) = "gray" {}
        [NoScaleOffset] _RockNormal ("Rock normal", 2D) = "bump" {}
        _TexScale ("Metres per texture tile", Range(1, 20)) = 6
        _TintStrength ("Palette tint on textures", Range(0, 1)) = 0.65
        _NormalStrength ("Normal map strength", Range(0, 2)) = 0.9
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
            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassNormal); SAMPLER(sampler_GrassNormal);
            TEXTURE2D(_MeadowMap); SAMPLER(sampler_MeadowMap);
            TEXTURE2D(_MeadowNormal); SAMPLER(sampler_MeadowNormal);
            TEXTURE2D(_EarthMap); SAMPLER(sampler_EarthMap);
            TEXTURE2D(_EarthNormal); SAMPLER(sampler_EarthNormal);
            TEXTURE2D(_RockMap); SAMPLER(sampler_RockMap);
            TEXTURE2D(_RockNormal); SAMPLER(sampler_RockNormal);

            CBUFFER_START(UnityPerMaterial)
                float4 _StoneColor;
                float4 _GrassColor;
                float4 _SandColor;
                float4 _EarthColor;
                float4 _CliffColor;
                float _DetailStrength;
                float _UseTextures;
                float _TexScale;
                float _TintStrength;
                float _NormalStrength;
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

            // A tint that keeps the texture's own contrast but pulls its average toward the palette colour.
            float3 Tinted(float3 tex, float3 palette)
            {
                float3 toned = tex * (palette / 0.42);
                return lerp(tex, toned, _TintStrength);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float g = saturate(IN.grass);
                float m = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, IN.positionWS.xz * 0.045).r * 0.6
                        + SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, IN.positionWS.xz * 0.21 + 0.37).r * 0.4;
                float steep = smoothstep(0.32, 0.58, 1.0 - N.y);
                float rim = 1.0 - abs(g * 2.0 - 1.0);
                float3 albedo;

                if (_UseTextures > 0.5)
                {
                    // World-planar tiling, two scales so the repeat never reads at race height or from the hills.
                    float2 uv = IN.positionWS.xz / _TexScale;
                    // The second sample is turned a quarter turn and weighted equally, so no single feature of the
                    // tile repeats on a grid.
                    float2 uv2 = IN.positionWS.zx / (_TexScale * 2.9) + 0.31;
                    float3 grassTex = lerp(SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, uv).rgb, SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, uv2).rgb, 0.5);
                    float3 meadowTex = SAMPLE_TEXTURE2D(_MeadowMap, sampler_MeadowMap, uv * 1.4).rgb;
                    float3 earthTex = SAMPLE_TEXTURE2D(_EarthMap, sampler_EarthMap, uv * 1.2).rgb;
                    float3 rockTex = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uv * 0.8).rgb;
                    float3 stonesTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;

                    float meadowW = smoothstep(0.2, 0.55, g) * (1.0 - smoothstep(0.65, 0.95, g));
                    float grassW = smoothstep(0.65, 0.95, g);
                    float stoneW = 1.0 - smoothstep(0.2, 0.55, g);
                    float3 flat = stonesTex * _StoneColor.rgb * stoneW
                                + Tinted(meadowTex, _GrassColor.rgb * 0.9) * meadowW
                                + Tinted(grassTex, _GrassColor.rgb) * grassW;
                    flat = lerp(flat, _SandColor.rgb * (0.85 + 0.3 * m), pow(rim, 1.4) * 0.0);
                    float3 steepColour = lerp(Tinted(rockTex, _CliffColor.rgb), Tinted(earthTex, _EarthColor.rgb), g);
                    albedo = lerp(flat, steepColour, steep);
                    albedo *= 1.0 + (IN.tint - 0.5) * 0.10;
                    // Far off, the tiling reads as a dot grid however it is scaled: the mottling goes first
                    // (60-300 m), then the texture and its normal detail fade to the palette's flat colour
                    // (150-600 m); the fog takes over after.
                    float camDist = distance(IN.positionWS, _WorldSpaceCameraPos);
                    float farFade = smoothstep(80.0, 350.0, camDist);
                    albedo *= 1.0 + (m - 0.5) * 2.0 * _DetailStrength * 0.5 * (1.0 - smoothstep(60.0, 300.0, camDist));
                    float3 flatFar = lerp(_GrassColor.rgb * 0.78, lerp(_CliffColor.rgb, _EarthColor.rgb, g) * 0.9, steep) * (1.0 + (IN.tint - 0.5) * 0.10);
                    albedo = lerp(albedo, flatFar, farFade);

                    // Normal detail: the maps are OpenGL (green up), applied in a world-planar frame.
                    float3 nGrass = UnpackNormal(SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassNormal, uv));
                    float3 nMeadow = UnpackNormal(SAMPLE_TEXTURE2D(_MeadowNormal, sampler_MeadowNormal, uv * 1.4));
                    float3 nEarth = UnpackNormal(SAMPLE_TEXTURE2D(_EarthNormal, sampler_EarthNormal, uv * 1.2));
                    float3 nRock = UnpackNormal(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uv * 0.8));
                    float3 nFlat = nGrass * grassW + nMeadow * meadowW + float3(0, 0, 1) * stoneW;
                    float3 nTS = normalize(lerp(nFlat, lerp(nRock, nEarth, g), steep));
                    // Tangent along world x, bitangent along world z: exact on flat ground, close enough on the slopes.
                    float3 T = normalize(cross(float3(0, 0, 1), N));
                    float3 B = cross(N, T);
                    N = normalize(N + (nTS.x * T + nTS.y * B) * _NormalStrength * (1.0 - farFade));
                }
                else
                {
                    float3 stones = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _StoneColor.rgb;
                    albedo = lerp(stones, _GrassColor.rgb, g);
                    albedo = lerp(albedo, _SandColor.rgb, rim * rim * 0.7);
                    albedo *= 1.0 + (IN.tint - 0.5) * 0.14;
                    albedo *= 1.0 + (m - 0.5) * 2.0 * _DetailStrength;
                    float3 steepColour = lerp(_CliffColor.rgb, _EarthColor.rgb, g) * (0.85 + 0.3 * m);
                    albedo = lerp(albedo, steepColour, steep);
                }

                Light light = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float shadow = light.shadowAttenuation * light.distanceAttenuation;
                float aoDirect = 1.0, aoIndirect = 1.0;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(IN.positionCS));
                    aoDirect = ao.directAmbientOcclusion;
                    aoIndirect = ao.indirectAmbientOcclusion;
                #endif
                float3 ambient = SampleSHPixel(half3(0, 0, 0), N) * aoIndirect;
                // Half-Lambert wrap keeps the shadow side of the hills readable; the key still carries the form.
                float ndl = saturate(dot(N, light.direction));
                float wrap = saturate((dot(N, light.direction) + 0.25) / 1.25);
                float3 colour = albedo * (ambient + light.color * shadow * lerp(wrap, ndl, 0.6) * aoDirect);
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
