// Scorch marks on a quad laid flat on the surface: darkens what is under it (alpha blend) and glows where hot.
// Per instance (MaterialPropertyBlock): _Heat 0..1 (the glowing core cools), _Fade 0..1 (the mark fades out).
Shader "Alcax/FX Decal"
{
    Properties
    {
        _BaseMap ("Scorch (RGB color, A coverage)", 2D) = "white" {}
        [HDR] _HotColor ("Hot Color", Color) = (2.5, 6, 8, 1)
        _Heat ("Heat", Range(0, 1)) = 1
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Offset -2, -2

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _HotColor;
                half _Heat, _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                // The core (where the scorch is darkest) glows while hot
                float2 d = i.uv - 0.5;
                half core = saturate(1 - length(d) * 4.5);
                half3 c = t.rgb + _HotColor.rgb * core * core * _Heat * _Heat;
                return half4(c, t.a * _Fade);
            }
            ENDHLSL
        }
    }
}
