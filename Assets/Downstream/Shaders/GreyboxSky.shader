// A painted sky for Downstream: a three-band gradient (zenith, horizon haze, ground haze) with a soft sun
// and two layers of drifting noise clouds, so the sky reads as one piece with the linear fog that the
// far hills dissolve into. No 3D cloud props, no texture downloads: the clouds come from the same tileable
// noise the ground uses.
Shader "Downstream/Greybox Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.24, 0.42, 0.80, 1)
        _HorizonColor ("Horizon haze", Color) = (0.76, 0.84, 0.91, 1)
        _GroundColor ("Below horizon", Color) = (0.70, 0.78, 0.86, 1)
        _HorizonPower ("Horizon band falloff", Range(1, 8)) = 3.2
        _SunColor ("Sun", Color) = (1, 0.95, 0.85, 1)
        _SunDirection ("Sun direction (to the sun)", Vector) = (0.3, 0.7, 0.5, 0)
        _SunSize ("Sun disc", Range(100, 4000)) = 1400
        _SunHalo ("Sun halo", Range(0, 1)) = 0.35
        [NoScaleOffset] _CloudMap ("Cloud noise", 2D) = "gray" {}
        _CloudCut ("Cloud coverage cut", Range(0, 1)) = 0.52
        _CloudSoft ("Cloud edge softness", Range(0.02, 0.5)) = 0.18
        _CloudScale ("Cloud scale", Range(0.05, 2)) = 0.42
        _CloudHeight ("Cloud layer flatness", Range(0.02, 0.6)) = 0.14
        _CloudShade ("Cloud underside", Color) = (0.72, 0.78, 0.88, 1)
        _CloudSpeed ("Cloud drift", Range(0, 0.1)) = 0.008
        _CloudAmount ("Cloud opacity", Range(0, 1)) = 0.85
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

            TEXTURE2D(_CloudMap); SAMPLER(sampler_CloudMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _SunDirection, _CloudShade;
                float _HorizonPower, _SunSize, _SunHalo, _CloudCut, _CloudSoft, _CloudScale, _CloudHeight, _CloudSpeed, _CloudAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            float Clouds(float3 d)
            {
                // Project the view ray onto a flat cloud deck; lower rays stretch toward the horizon.
                float2 uv = d.xz / (d.y + _CloudHeight) * _CloudScale * 0.1;
                float2 drift = float2(1.0, 0.35) * _Time.y * _CloudSpeed;
                float n = SAMPLE_TEXTURE2D(_CloudMap, sampler_CloudMap, uv + drift).r * 0.62
                        + SAMPLE_TEXTURE2D(_CloudMap, sampler_CloudMap, uv * 2.7 + drift * 1.6 + 0.31).r * 0.26
                        + SAMPLE_TEXTURE2D(_CloudMap, sampler_CloudMap, uv * 7.1 - drift * 0.8 + 0.77).r * 0.12;
                return smoothstep(_CloudCut, _CloudCut + _CloudSoft, n);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                float up = saturate(d.y);
                float band = pow(1.0 - up, _HorizonPower);
                float3 sky = lerp(_ZenithColor.rgb, _HorizonColor.rgb, band);
                // Under the horizon the haze darkens a touch so the far water and ground do not glow.
                float below = smoothstep(0.0, -0.08, d.y);
                sky = lerp(sky, _GroundColor.rgb, below);

                float3 sunDir = normalize(_SunDirection.xyz);
                float cosSun = saturate(dot(d, sunDir));
                float disc = pow(cosSun, _SunSize);
                float halo = pow(cosSun, 6.0) * _SunHalo;
                sky += _SunColor.rgb * (disc * 1.6 + halo * 0.5);

                if (d.y > 0.0)
                {
                    float c = Clouds(d) * _CloudAmount;
                    // Fade clouds out at the horizon so they melt into the haze, like the fog does.
                    c *= smoothstep(0.0, 0.22, d.y);
                    // A second, sharper sample a little toward the sun gives the puffs a lit top edge.
                    float lit = Clouds(normalize(d + sunDir * 0.03));
                    float3 cloud = lerp(_CloudShade.rgb, float3(1.0, 1.0, 1.0), saturate(0.55 + 0.6 * (lit - Clouds(d)) + 0.2));
                    cloud += _SunColor.rgb * halo * 0.3;
                    sky = lerp(sky, cloud, c);
                }
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
