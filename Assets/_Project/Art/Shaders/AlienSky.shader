// Desert-planet sky for Meridian Station: a dusty haze horizon, a sun disk with a glow,
// and a banded gas giant with a tilted ring system, all computed per pixel.
Shader "Alcax/AlienSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.24, 0.37, 0.51, 1)
        _Haze ("Horizon Haze", Color) = (0.91, 0.72, 0.56, 1)
        _Ground ("Below Horizon", Color) = (0.55, 0.36, 0.24, 1)
        _SunColor ("Sun", Color) = (1, 0.72, 0.47, 1)
        _SunDir ("Sun Direction (toward sun)", Vector) = (0.3, 0.3, 0.9, 0)
        _SunSize ("Sun Size", Range(0.995, 0.99995)) = 0.9993
        _PlanetDir ("Planet Direction", Vector) = (0.5, 0.25, 0.83, 0)
        _PlanetRadius ("Planet Angular Radius (rad)", Float) = 0.2
        _PlanetA ("Planet Band A", Color) = (0.85, 0.64, 0.49, 1)
        _PlanetB ("Planet Band B", Color) = (0.61, 0.42, 0.33, 1)
        _RingColor ("Ring", Color) = (0.85, 0.76, 0.65, 1)
        _RingTilt ("Ring Normal", Vector) = (0.25, 0.92, -0.3, 0)
        _Exposure ("Exposure", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Haze, _Ground, _SunColor, _PlanetA, _PlanetB, _RingColor;
                float4 _SunDir, _PlanetDir, _RingTilt;
                float _SunSize, _PlanetRadius, _Exposure;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float hash(float n) { return frac(sin(n) * 43758.5453); }
            float noise1(float x) { float i = floor(x); float f = frac(x); f = f * f * (3 - 2 * f); return lerp(hash(i), hash(i + 1), f); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sunDir = normalize(_SunDir.xyz);

                // Atmosphere: dusty haze near the horizon, thinning to a cool zenith, warmer toward the sun.
                float h = d.y;
                float haze = exp(-max(h, 0) * 4.0);
                float sunAmt = saturate(dot(d, sunDir));
                half3 sky = lerp(_Zenith.rgb, _Haze.rgb, haze);
                sky += _SunColor.rgb * pow(sunAmt, 8) * 0.35 * haze;
                sky += _SunColor.rgb * pow(sunAmt, 64) * 0.6;
                half3 below = lerp(_Haze.rgb, _Ground.rgb, saturate(-h * 5));
                half3 col = h >= 0 ? sky : below;

                // Gas giant: ray-sphere test against a planet far along _PlanetDir.
                float3 pDir = normalize(_PlanetDir.xyz);
                float D = 1000.0;
                float R = D * sin(_PlanetRadius);
                float3 C = pDir * D;
                float b = dot(d, C);
                float c = dot(C, C) - R * R;
                float disc = b * b - c;
                float tPlanet = 1e9;
                if (disc > 0)
                {
                    float t = b - sqrt(disc);
                    if (t > 0)
                    {
                        tPlanet = t;
                        float3 hit = d * t;
                        float3 n = normalize(hit - C);
                        float3 axis = normalize(_RingTilt.xyz);
                        float lat = dot(n, axis);
                        float bands = sin(lat * 26.0 + noise1(lat * 40.0) * 2.5) * 0.5 + 0.5;
                        bands = lerp(bands, noise1(lat * 90.0), 0.25);
                        half3 surf = lerp(_PlanetB.rgb, _PlanetA.rgb, bands);
                        float lit = saturate(dot(n, sunDir) * 0.9 + 0.12);
                        float rim = pow(1 - saturate(dot(n, -d)), 3);
                        half3 planet = surf * lit + _Haze.rgb * rim * 0.35;
                        // Haze in front of the planet (it's seen through the atmosphere)
                        planet = lerp(planet, sky, 0.28 + haze * 0.35);
                        col = planet;
                    }
                }

                // Rings: a plane through the planet center.
                float3 rn = normalize(_RingTilt.xyz);
                float denom = dot(d, rn);
                if (abs(denom) > 1e-4)
                {
                    float tr = dot(C, rn) / denom;
                    if (tr > 0)
                    {
                        float r = length(d * tr - C) / R;
                        if (r > 1.35 && r < 2.35)
                        {
                            float k = (r - 1.35) / 1.0;
                            float bandsR = noise1(r * 38.0) * 0.6 + noise1(r * 9.0) * 0.4;
                            float gap = smoothstep(0.52, 0.55, k) * (1 - smoothstep(0.58, 0.61, k));
                            float a = bandsR * (1 - gap) * smoothstep(0.0, 0.08, k) * (1 - smoothstep(0.9, 1.0, k)) * 0.75;
                            half3 ringCol = _RingColor.rgb * (0.55 + 0.45 * saturate(dot(rn, sunDir) * 0.5 + 0.6));
                            ringCol = lerp(ringCol, sky, 0.3 + haze * 0.3);
                            bool inFront = tr < tPlanet;
                            if (inFront || tPlanet > 1e8) col = lerp(col, ringCol, a);
                        }
                    }
                }

                // Sun disk on top
                float disk = smoothstep(_SunSize, _SunSize + 0.00015, dot(d, sunDir));
                col = lerp(col, _SunColor.rgb * 6.0, disk);

                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
