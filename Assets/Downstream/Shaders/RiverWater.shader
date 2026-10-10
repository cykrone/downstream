// Downstream river water: driven by the River Field (RiverField.hlsl), so the surface drawn is the
// surface the boats ride. Flow-mapped ripple normals (two-phase, Valve's method) advected by the
// field's flow, depth tint from the field's own depth, refraction through the opaque texture, sun
// specular and a stylized sky fresnel, and foam that follows the design's readability grammar:
// current lanes draw as long glossy streaks, eddy lines as foam seams, holes as a white boil,
// crests as a breaking face, the waterfall lip as spray.
Shader "Downstream/River Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow tint (over the bed)", Color) = (0.55, 0.85, 0.72, 1)
        _DeepColor ("Deep colour", Color) = (0.08, 0.22, 0.2, 1)
        _Absorption ("Absorption per metre (RGB)", Vector) = (0.9, 0.45, 0.35, 0)
        _Refraction ("Refraction", Range(0, 0.2)) = 0.045
        _EdgeFade ("Shore contact fade, metres", Range(0.02, 2)) = 0.35

        [Header(Surface)]
        [NoScaleOffset] _RippleNormal ("Ripple normals", 2D) = "bump" {}
        _RippleScale ("Ripple tiles per metre", Range(0.05, 2)) = 0.35
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.55
        _FlowSpeed ("Flow map cycles per second", Range(0.05, 2)) = 0.35
        _LaneStretch ("Current lane streak stretch", Range(1, 6)) = 3.5
        _Smoothness ("Smoothness", Range(0.5, 1)) = 0.93
        _SpecularStrength ("Sun glint", Range(0, 4)) = 1.6
        _ZenithColor ("Sky reflection, zenith", Color) = (0.42, 0.62, 0.9, 1)
        _HorizonColor ("Sky reflection, horizon", Color) = (0.8, 0.88, 0.95, 1)
        _Fresnel ("Reflectivity at grazing", Range(0, 1)) = 0.9
        _ProbeMix ("Reflection probe share", Range(0, 1)) = 0.6

        [Header(Foam)]
        [NoScaleOffset] _FoamTex ("Foam strokes", 2D) = "white" {}
        _FoamColor ("Foam colour", Color) = (0.96, 0.98, 1.0, 1)
        _FoamScale ("Foam tiles per metre", Range(0.05, 2)) = 0.25
        _FoamFlowStart ("Whitewater starts at m/s", Range(0, 10)) = 3.5
        _FoamFlowFull ("Whitewater full at m/s", Range(0, 12)) = 6.5
        _FoamSlope ("Whitewater on slopes steeper than", Range(0.01, 1)) = 0.12
        [Header(Debug)]
        _SlowSpeed ("Slack water speed (m/s)", Float) = 1.5
        _FastSpeed ("Fast water speed (m/s)", Float) = 7
        _SlowTint ("Slack water tint", Color) = (0.72, 0.86, 0.92, 1)
        _FastTint ("Fast water tint", Color) = (1.12, 1.22, 1.16, 1)
        _EddyTint ("Eddy tint", Color) = (0.62, 0.78, 0.82, 1)
        _LaneMarkSpacing ("Lane marker spacing (m)", Float) = 9
        _LaneMarkSpeed ("Lane marker speed (m/s)", Float) = 7
        _LaneMarkColor ("Lane marker colour", Color) = (0.75, 1.0, 1.0, 1)
        _Debug ("Debug view (0 off, 1 body, 2 fresnel, 3 ambient+sun, 4 sky, 5 foam, 6 depth, 7 normal, 8 fog)", Range(0, 10)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "RiverWaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // URP 17 declares the fog keywords (dynamic branch or multi_compile) in this include.
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            // Forward+ (clustered) lighting: without this keyword URP leaves the main light off for the material.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "RiverField.hlsl"

            TEXTURE2D(_RippleNormal); SAMPLER(sampler_RippleNormal);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _Absorption;
                float _Refraction;
                float _EdgeFade;
                float _RippleScale;
                float _RippleStrength;
                float _FlowSpeed;
                float _LaneStretch;
                float _Smoothness;
                float _SpecularStrength;
                float4 _ZenithColor;
                float4 _HorizonColor;
                float _Fresnel;
                float _ProbeMix;
                float4 _FoamColor;
                float _FoamScale;
                float _FoamFlowStart;
                float _FoamFlowFull;
                float _FoamSlope;
                float _Debug;
                float _SlowSpeed, _FastSpeed, _LaneMarkSpacing, _LaneMarkSpeed;
                float4 _SlowTint, _FastTint, _EddyTint, _LaneMarkColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;   // xy/w = screen uv, w = eye depth
                float3 staticSlopeAndWet : TEXCOORD2; // baked surface slope (x, z) and 1 when wet at the vertex
                float fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                RiverWaterSample s = SampleRiverWater(positionWS.xz, _RiverRaceTime);
                // The mesh is a flat grid; the field places it. Dry vertices sit at the level the
                // water would have, under the bank blocks.
                positionWS.y = s.surfaceHeight;
                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.staticSlopeAndWet = float3(s.staticSlope, s.isWet ? 1.0 : 0.0);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            // Rotates a world-space xz offset into a frame aligned with the flow, squashed along it.
            float2 FlowFrameUv(float2 p, float2 flowDir, float stretch)
            {
                float2 perp = float2(-flowDir.y, flowDir.x);
                return float2(dot(p, flowDir) / stretch, dot(p, perp));
            }

            float3 SkyReflection(float3 r)
            {
                return lerp(_HorizonColor.rgb, _ZenithColor.rgb, saturate(r.y * 1.4));
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float t = _RiverRaceTime;
                float2 xz = IN.positionWS.xz;

                RiverStaticSample st = SampleRiverStatic(xz);
                clip(st.hasData ? 1.0 : -1.0);
                float rise, flowScale;
                RiverLayersAt(st.riverDistance, t, rise, flowScale);
                float waveH;
                float2 waveSlope;
                RiverWavesAt(xz, t, waveH, waveSlope);
                float depth = (st.surfaceHeight + rise) - st.bedHeight + waveH;
                // Near the waterline, blur the bed over a metre so the 0.5 m texel steps of the channel
                // edge do not read as a sawtooth at grazing angles. Physics still uses the sharp field.
                if (depth < 1.2)
                {
                    float bed = st.bedHeight;
                    float n = 1.0;
                    [unroll]
                    for (int k = 0; k < 4; k++)
                    {
                        float2 o = k == 0 ? float2(0.7, 0.7) : k == 1 ? float2(-0.7, 0.7) : k == 2 ? float2(0.7, -0.7) : float2(-0.7, -0.7);
                        RiverStaticSample sk = SampleRiverStatic(xz + o);
                        if (!sk.hasData) continue;
                        bed += sk.bedHeight;
                        n += 1.0;
                    }
                    depth = (st.surfaceHeight + rise) - bed / n + waveH;
                }
                clip(depth);
                // The waterline follows the smooth ground mesh in the camera depth, not the 0.5 m field texels:
                // where the field says shallow, a ground pixel in front of the surface ends the water.
                if (depth < 0.7)
                {
                    float2 shoreUv = IN.screenPos.xy / IN.screenPos.w;
                    float shoreEye = LinearEyeDepth(SampleSceneDepth(shoreUv), _ZBufferParams);
                    float viewDiff = shoreEye - IN.screenPos.w;
                    clip(viewDiff + 0.004);
                    // Vertical gap to the ground, not the distance along the view ray: at a grazing angle a
                    // centimetre of water sits metres along the ray, and that sliver used to draw as a bright line.
                    float3 toCam = normalize(GetCameraPositionWS() - IN.positionWS);
                    float vertical = max(viewDiff, 0.0) * saturate(abs(toCam.y) + 0.05);
                    depth = min(depth, vertical * 0.8 + 0.01);
                }

                float2 flow = st.flow * flowScale;
                float speed = length(flow);
                float2 flowDir = speed > 1e-3 ? flow / speed : float2(0.0, 1.0);
                int feat = st.features;
                bool lane = (feat & RIVER_FEATURE_CURRENT_LANE) != 0;
                bool eddy = (feat & RIVER_FEATURE_EDDY) != 0;
                bool hole = (feat & RIVER_FEATURE_HOLE) != 0;
                bool crest = (feat & RIVER_FEATURE_CREST) != 0;
                bool lip = (feat & RIVER_FEATURE_WATERFALL_LIP) != 0;
                // The eddy line: strongest where the bilinear eddy share crosses one half, so it runs smooth along the
                // boundary instead of stepping texel by texel.
                float eddySeam = smoothstep(0.25, 0.75, 1.0 - abs(2.0 * st.eddyWeight - 1.0));

                // Gameplay normal: baked slope plus the wave slope, exactly as the sim sees it.
                float2 slope = IN.staticSlopeAndWet.xy + waveSlope;
                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));

                // Two-phase flow map: each layer's pattern drifts with the water for one cycle and
                // fades out while it is most distorted; the other layer covers the gap.
                float period = 1.0 / _FlowSpeed;
                float phase0 = frac(t * _FlowSpeed);
                float phase1 = frac(t * _FlowSpeed + 0.5);
                float w0 = 1.0 - abs(2.0 * phase0 - 1.0);
                float w1 = 1.0 - w0;
                float2 drift0 = flow * ((phase0 - 0.5) * period);
                float2 drift1 = flow * ((phase1 - 0.5) * period);
                float stretch = lane ? _LaneStretch : 1.0;
                float2 perp0 = float2(-flowDir.y, flowDir.x);
                float2 uv0 = FlowFrameUv((xz - drift0) * _RippleScale, flowDir, stretch);
                float2 uv1 = FlowFrameUv((xz - drift1) * _RippleScale + 0.37, flowDir, stretch);
                float2 n0 = SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, uv0).xy * 2.0 - 1.0;
                float2 n1 = SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, uv1).xy * 2.0 - 1.0;
                // A second, larger octave turned across the flow breaks up the regular banding.
                float2 uvB0 = FlowFrameUv((xz - drift0) * _RippleScale * 0.37, perp0, 1.0);
                float2 uvB1 = FlowFrameUv((xz - drift1) * _RippleScale * 0.37 + 0.19, perp0, 1.0);
                float2 m0 = SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, uvB0).xy * 2.0 - 1.0;
                float2 m1 = SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, uvB1).xy * 2.0 - 1.0;
                float2 mf = m0 * w0 + m1 * w1;
                float2 nf = (n0 * w0 + n1 * w1) * 0.7 + float2(-mf.y, mf.x) * 0.5; // in the flow frame
                float2 perp = float2(-flowDir.y, flowDir.x);
                float2 ripple = nf.x * flowDir + nf.y * perp;
                float viewDist = distance(GetCameraPositionWS(), IN.positionWS);
                float detail = saturate(1.3 - viewDist / 160.0); // ripples and strokes calm into the distance
                float rippleAmount = _RippleStrength * (0.35 + 0.65 * saturate(speed / 5.0)) * (hole ? 1.8 : 1.0) * detail;
                float3 Nd = normalize(N + float3(ripple.x, 0.0, ripple.y) * rippleAmount);

                // Foam strokes carried by the same flow map.
                float2 fuv0 = FlowFrameUv((xz - drift0) * _FoamScale, flowDir, stretch);
                float2 fuv1 = FlowFrameUv((xz - drift1) * _FoamScale + 0.61, flowDir, stretch);
                float strokes = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv0).r * w0
                              + SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv1).r * w1;
                // A boil churns in place rather than drifting: counter-scrolling strokes.
                float2 buv = xz * _FoamScale * 1.7;
                // Two stroke fields crossed at right angles churn into cells rather than a grid.
                float boilA = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, buv + float2(0.0, t * 0.9)).r;
                float boilB = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, float2(buv.y, -buv.x) * 1.27 + float2(t * 0.7, 0.0)).r;
                float boil = saturate(boilA * 0.5 + boilB * 0.5 + boilA * boilB);

                // Rapids and falls are steeper than the river's own grade; the grade itself is not whitewater.
                float slopeMag = max(0.0, length(IN.staticSlopeAndWet.xy) - _RiverBaseSlope * 1.1);
                float cover = 0.0;
                cover += 0.75 * smoothstep(_FoamFlowStart, _FoamFlowFull, speed);
                cover += 0.8 * smoothstep(_FoamSlope, _FoamSlope * 2.5, slopeMag) * (0.55 + 0.45 * strokes);
                cover += lane ? 0.05 : 0.0; // lanes read as glossy streaks, not white water
                cover += eddySeam * 0.4 * (0.6 + 0.4 * strokes) + (eddy ? 0.18 : 0.0);
                cover += crest ? 0.6 : 0.0;
                cover += lip ? 0.9 : 0.0;
                cover += 0.3 * smoothstep(0.6, 0.1, depth);
                cover = saturate(cover);
                float foam = smoothstep(1.0 - cover, 1.0 - cover + 0.3, strokes) * lerp(0.6, 1.0, detail) * saturate(cover * 2.5) * saturate(depth / 0.12);
                // The boil: full inside the hole, half where the blended texels disagree, churning with its own noise.
                float holeMix = (st.featuresAll & RIVER_FEATURE_HOLE) != 0 ? 1.0 : ((st.featuresAny & RIVER_FEATURE_HOLE) != 0 ? 0.5 : 0.0);
                foam = max(foam, holeMix * smoothstep(0.3, 0.8, boil));
                // The falls face: a curtain of vertical streaks pouring down, not a flat white slab.
                float steepFace = smoothstep(1.0, 3.0, slopeMag);
                if (steepFace > 0.0)
                {
                    float2 cuv = float2(dot(xz, perp0) * 0.55, IN.positionWS.y * 0.11 + t * 2.8);
                    float curtain = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, cuv).r * 0.6
                                  + SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, cuv * float2(2.1, 1.3) + float2(0.4, t * 1.7)).r * 0.4;
                    foam = lerp(foam, saturate(0.3 + 0.9 * curtain), steepFace);
                }

                // Underwater colour: the refracted opaque scene absorbed by depth, tinted by the biome.
                float2 screenUv = IN.screenPos.xy / IN.screenPos.w;
                float2 refractUv = screenUv + Nd.xz * _Refraction;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(refractUv), _ZBufferParams);
                if (sceneEye < IN.screenPos.w)
                {
                    // The refracted texel belongs to something in front of the surface (a hull): do not bend it.
                    refractUv = screenUv;
                    sceneEye = LinearEyeDepth(SampleSceneDepth(refractUv), _ZBufferParams);
                }
                float3 scene = SampleSceneColor(refractUv);
                float3 absorb = exp(-depth * _Absorption.rgb);
                float3 body = lerp(_DeepColor.rgb, scene * _ShallowColor.rgb, absorb);
                // Speed grading: fast water runs pale and bright, slack water sits deep and dull, so the fast
                // line and the eddies read at a glance; eddies darken further.
                float speedK = smoothstep(0.0, 1.0, saturate((speed - _SlowSpeed) / max(_FastSpeed - _SlowSpeed, 0.01)));
                float laneSum = st.laneWeight, speedSum = speed;
                [unroll]
                for (int q = 0; q < 8; q++)
                {
                    // A 3 x 3 cross at 1.7 m: the lane's texel-row edge becomes a gradient over a boat length.
                    float2 o = 1.7 * (q == 0 ? float2(1, 0) : q == 1 ? float2(-1, 0) : q == 2 ? float2(0, 1) : q == 3 ? float2(0, -1)
                             : q == 4 ? float2(1, 1) : q == 5 ? float2(-1, 1) : q == 6 ? float2(1, -1) : float2(-1, -1));
                    RiverStaticSample sq = SampleRiverStatic(xz + o);
                    laneSum += sq.hasData ? sq.laneWeight : st.laneWeight;
                    speedSum += sq.hasData ? length(sq.flow * flowScale) : speed;
                }
                float laneW = smoothstep(0.2, 0.8, laneSum / 9.0);
                speedK = smoothstep(0.0, 1.0, saturate((speedSum / 9.0 - _SlowSpeed) / max(_FastSpeed - _SlowSpeed, 0.01)));
                float eddyW = smoothstep(0.15, 0.85, st.eddyWeight);
                body *= lerp(_SlowTint.rgb, _FastTint.rgb, saturate(speedK * 0.6 + laneW * 0.5));
                body *= lerp(1.0, _EddyTint.rgb, eddyW);
                // Current lane: bright bands sweeping downstream every few metres, the boost-pad language.
                float ph = frac((st.riverDistance - t * _LaneMarkSpeed) / _LaneMarkSpacing);
                float laneBand = laneW * smoothstep(0.55, 0.72, ph) * smoothstep(0.98, 0.84, ph);

                // Lighting: one warm key, cool sky fill, glints on the ripples.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light light = GetMainLight(shadowCoord);
                float shadow = light.shadowAttenuation * light.distanceAttenuation;
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                float3 L = light.direction;
                float3 H = normalize(L + V);
                float3 ambient = SampleSHPixel(half3(0, 0, 0), Nd);
                float ndl = saturate(dot(Nd, L));
                float3 lit = body * (ambient * 0.85 + light.color * shadow * (0.2 + 0.45 * ndl));

                float specPower = exp2(10.0 * _Smoothness + 1.0);
                float glint = pow(saturate(dot(Nd, H)), specPower) * (specPower + 8.0) / (8.0 * PI);
                float3 spec = light.color * shadow * glint * _SpecularStrength * (lane ? 1.4 : 1.0);

                float3 R = reflect(-V, Nd);
                float fresnel = lerp(0.04, 1.0, pow(1.0 - saturate(dot(Nd, V)), 4.0));
                fresnel = saturate(fresnel * _Fresnel);
                float3 skyGradient = SkyReflection(R) * (ambient + light.color * shadow) * 0.8;
                float3 probe = GlossyEnvironmentReflection(R, IN.positionWS, 0.12, 1.0, screenUv);
                float3 sky = lerp(skyGradient, probe * 1.15, _ProbeMix);
                float3 colour = lerp(lit, sky, fresnel) + spec;
                // Current lane: the glossy streak itself, a touch brighter where the stretched ripples catch the light.
                colour += lane ? 0.04 * (0.5 + 0.5 * nf.y) * light.color * shadow : 0.0;
                colour += laneBand * _LaneMarkColor.rgb * 0.12 * (ambient + light.color * shadow * 0.6);

                float3 foamLit = _FoamColor.rgb * (ambient + light.color * shadow * (0.5 + 0.5 * saturate(dot(N, L))));
                colour = lerp(colour, foamLit, foam);

                colour = MixFog(colour, IN.fogFactor);

                int dbg = (int)round(_Debug);
                if (dbg == 1) return half4(body, 1);
                if (dbg == 2) return half4(fresnel.xxx, 1);
                if (dbg == 3) return half4(ambient + light.color * shadow, 1);
                if (dbg == 4) return half4(sky, 1);
                if (dbg == 5) return half4(foam.xxx, 1);
                if (dbg == 6) return half4(saturate(depth / 3.0).xxx, 1);
                if (dbg == 7) return half4(Nd * 0.5 + 0.5, 1);
                if (dbg == 8) return half4(IN.fogFactor.xxx, 1);
                if (dbg == 9) return half4(saturate(IN.screenPos.w * unity_FogParams.z + unity_FogParams.w).xxx, 1);
                if (dbg == 10) return half4(saturate(-unity_FogParams.z * 500.0), saturate(unity_FogParams.w * 0.5), 0, 1);

                // Soft edge where the surface meets the bank and the bed, and a fade in very shallow water.
                float contact = saturate((sceneEye - IN.screenPos.w) / _EdgeFade);
                float shallow = saturate(depth / 0.5);
                float alpha = max(contact, foam * 0.6) * shallow;
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
