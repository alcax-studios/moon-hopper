// Soft additive light shaft: fades along its length (uv.y) and toward its edges (uv.x),
// and fades out when seen edge-on or from very close, so it reads as dusty sunlight, not a solid plane.
Shader "Alcax/LightShaft"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.72, 0.47, 1)
        _Intensity ("Intensity", Float) = 0.12
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.5)) = 0.35
        _NearFade ("Near Fade Distance", Float) = 3
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _EdgeSoftness, _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 posWS : TEXCOORD1; float3 normalWS : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float edge = smoothstep(0, _EdgeSoftness, i.uv.x) * smoothstep(0, _EdgeSoftness, 1 - i.uv.x);
                float along = smoothstep(0, 0.15, i.uv.y) * (1 - smoothstep(0.55, 1, i.uv.y));
                float3 v = _WorldSpaceCameraPos - i.posWS;
                float dist = length(v);
                float facing = abs(dot(normalize(i.normalWS), v / max(dist, 1e-4)));
                float near = saturate((dist - 0.5) / _NearFade);
                float a = edge * along * smoothstep(0.05, 0.5, facing) * near * _Intensity;
                return half4(_Color.rgb * a, 1);
            }
            ENDHLSL
        }
    }
}
