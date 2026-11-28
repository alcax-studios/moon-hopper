// Plasma fluid behind glass (the carbine's barrel strips and energy cell): lava-lamp blobs that drift, merge and
// split, bubbles that rise and pop, a flowing undercurrent, and a glassy rim highlight. All procedural, in UV space
// laid out in meters x 20 (so blobs stay round on any part). _Heat (0..1, per renderer from PlasmaRig) speeds it up,
// brightens it, and shifts it white-hot; _Boil (0..1) adds violent bubbling while overheated.
Shader "Alcax/Plasma Fluid"
{
    Properties
    {
        [HDR] _Deep ("Deep Fluid", Color) = (0.02, 0.18, 0.45, 1)
        [HDR] _Glow ("Blob Glow", Color) = (0.6, 3.2, 5.0, 1)
        [HDR] _Hot ("Hot Core", Color) = (4, 7, 8, 1)
        _FlowDir ("Flow Direction (UV)", Vector) = (0, 1, 0, 0)
        _Speed ("Speed", Float) = 0.35
        _BlobSize ("Blob Size", Float) = 0.42
        _Bubbles ("Bubble Amount", Range(0, 1)) = 0.6
        _Heat ("Heat", Range(0, 1)) = 0
        _Boil ("Boil", Range(0, 1)) = 0
        _Glass ("Glass Rim", Float) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Glow, _Hot;
                float4 _FlowDir;
                float _Speed, _BlobSize, _Bubbles, _Heat, _Boil, _Glass;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                float a = hash2(i).x, b = hash2(i + float2(1, 0)).x, c = hash2(i + float2(0, 1)).x, d = hash2(i + float2(1, 1)).x;
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Metaball field: one blob per cell, wandering on its own slow orbit, summed over neighbors
            float blobs(float2 p, float t)
            {
                float2 cell = floor(p);
                float field = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 c = cell + float2(x, y);
                    float2 h = hash2(c);
                    float2 pos = c + 0.5 + 0.38 * float2(sin(t * (0.6 + h.x) + h.y * 6.28), cos(t * (0.5 + h.y) + h.x * 6.28));
                    float r = _BlobSize * (0.6 + 0.6 * h.x);
                    float2 d = p - pos;
                    field += r * r / (dot(d, d) + 1e-3);
                }
                return field;
            }

            // Rising bubbles: a sparse grid moving along the flow, each a small ring that pops
            float bubbles(float2 p, float t, float amount)
            {
                float2 q = p * 3.0;
                float2 cell = floor(q);
                float2 h = hash2(cell);
                if (h.x > amount) return 0;
                float2 f = frac(q) - 0.5 - (h - 0.5) * 0.5;
                float r = 0.08 + 0.12 * h.y;
                float d = length(f);
                float ring = smoothstep(r, r - 0.04, d) - smoothstep(r - 0.05, r - 0.09, d) * 0.6;
                float life = frac(t * 0.5 + h.y);
                return saturate(ring) * smoothstep(0.0, 0.15, life) * smoothstep(1.0, 0.8, life);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float heat = saturate(_Heat);
                float speed = _Speed * (1 + heat * 2.5 + _Boil * 3);
                float t = _Time.y * speed;
                float2 dir = normalize(_FlowDir.xy + 1e-4);
                // Undercurrent: the fluid streams along the flow with a slow swirl
                float2 p = i.uv + dir * t * 0.6;
                float swirl = noise(p * 2.0 + t * 0.3) * 2 - 1;
                p += float2(-dir.y, dir.x) * swirl * 0.25;
                float current = noise(p * 4.0) * 0.6 + noise(p * 9.0 + t) * 0.4;

                // Lava-lamp blobs rising through it
                float f = blobs(i.uv + dir * t * 0.35, t * 1.3);
                float blob = smoothstep(1.0, 1.35, f);
                float core = smoothstep(1.8, 3.2, f);
                // Bubbles (more when hot, a rolling boil when overheated)
                float bub = bubbles(i.uv + dir * t * 1.6, t * 2, saturate(_Bubbles * (0.4 + heat) + _Boil));

                half3 c = _Deep.rgb * (0.6 + current * 0.8);
                c = lerp(c, _Glow.rgb * (0.7 + current * 0.5), blob);
                c += _Hot.rgb * core * (0.4 + heat);
                c += _Glow.rgb * bub * 1.5;
                c *= 1 + heat * 1.4 + _Boil * 0.6 * (0.5 + 0.5 * sin(_Time.y * 40));
                c = lerp(c, _Hot.rgb * dot(c, half3(0.2, 0.4, 0.4)) * 0.4, heat * heat * 0.35);

                // Glass: a cool rim and a soft reflection streak
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fres = pow(1 - saturate(dot(normalize(i.normalWS), V)), 3);
                c += _Glass * fres * half3(0.6, 0.85, 1.0);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
