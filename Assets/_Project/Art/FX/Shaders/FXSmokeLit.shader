// Lit smoke and steam: alpha-blended flipbook particles lit by the main light (wrapped, with a back-light glow
// through thin smoke) and ambient light probes, with depth fade. Particle color tints, alpha fades.
Shader "Alcax/FX Smoke Lit"
{
    Properties
    {
        _BaseMap ("Flipbook", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Density ("Density", Float) = 1
        _Wrap ("Light Wrap", Range(0, 1)) = 0.5
        _Ambient ("Ambient Strength", Float) = 1
        _SoftFade ("Soft Fade Distance", Float) = 0.3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Color;
                half _Density, _Wrap, _Ambient, _SoftFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float3 positionWS : TEXCOORD1; float4 screenPos : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half a = saturate(t.a * _Density * i.color.a * _Color.a);
                // A billboard faces the camera: light it as a soft volume (wrapped toward the viewer, plus a
                // back-light glow where it is thin)
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(V, L.direction);
                half front = saturate((ndl + _Wrap) / (1 + _Wrap));
                half back = pow(saturate(-ndl), 4) * (1 - a) * 1.5;
                half3 lit = L.color * L.shadowAttenuation * (front * 0.6 + back) * t.rgb;
                half3 amb = SampleSH(half3(0, 1, 0)) * _Ambient * t.rgb;
                half3 c = (lit + amb) * i.color.rgb * _Color.rgb;
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float self = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                a *= saturate((scene - self) / max(_SoftFade, 1e-4));
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
