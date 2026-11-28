// Hair cards (hair, eyebrows, eyelashes): alpha cutout, two-sided, and an anisotropic strand highlight
// (Kajiya-Kay, two lobes: a sharp white one and a softer tinted one shifted along the strand), lit by the main light
// with ambient from probes. Strands run along the card's V (UV) direction, as in the MakeHuman hair.
Shader "Alcax/Hair Lit"
{
    Properties
    {
        _BaseMap ("Albedo (A = coverage)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.45
        _SpecColor1 ("Primary Highlight", Color) = (0.9, 0.85, 0.8, 1)
        _SpecColor2 ("Secondary Highlight (tinted)", Color) = (0.55, 0.42, 0.3, 1)
        _Shift1 ("Primary Shift", Range(-1, 1)) = 0.08
        _Shift2 ("Secondary Shift", Range(-1, 1)) = -0.12
        _Exp1 ("Primary Sharpness", Float) = 180
        _Exp2 ("Secondary Sharpness", Float) = 40
        _SpecStrength ("Highlight Strength", Range(0, 2)) = 0.6
        _Translucency ("Light Through Hair", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _SpecColor1, _SpecColor2;
            half _Cutoff, _Shift1, _Shift2, _Exp1, _Exp2, _SpecStrength, _Translucency;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; float4 tangentWS : TEXCOORD3; half fog : TEXCOORD4; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, v.tangentOS.w);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half StrandSpec(half3 T, half3 V, half3 L, half exponent)
            {
                half3 H = normalize(L + V);
                half th = dot(T, H);
                half s = sqrt(max(0, 1 - th * th));
                return pow(s, exponent) * smoothstep(-1, 0, th);
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                float3 N = normalize(i.normalWS) * (front ? 1 : -1);
                float3 t = normalize(i.tangentWS.xyz);
                float3 B = normalize(cross(N, t) * i.tangentWS.w);    // strands run along V: the bitangent
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half atten = L.shadowAttenuation * L.distanceAttenuation;
                half ndl = dot(N, L.direction);
                half diffuse = saturate(ndl * 0.6 + 0.4);   // hair scatters: a soft wrap
                half3 T1 = normalize(B + N * _Shift1), T2 = normalize(B + N * _Shift2);
                half3 spec = (_SpecColor1.rgb * StrandSpec(T1, V, L.direction, _Exp1) + _SpecColor2.rgb * albedo.rgb * StrandSpec(T2, V, L.direction, _Exp2)) * _SpecStrength * saturate(ndl + 0.3);
                half back = pow(saturate(dot(V, -L.direction)), 3) * _Translucency;
                half3 c = albedo.rgb * (L.color * atten * (diffuse + back) + SampleSH(N)) + L.color * atten * spec;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(p, n, _LightDirection));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = TRANSFORM_TEX(v.uv, _BaseMap); return o; }
            half4 frag(Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return 0; }
            ENDHLSL
        }
    }
}
