// Additive glow for energy effects (plasma, sparks, flashes, arcs): texture x vertex color x HDR intensity.
// Soft near opaque surfaces (depth fade) so sprites don't cut into walls.
Shader "Alcax/FX Additive"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _SoftFade ("Soft Fade Distance", Float) = 0.15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Color;
                half _Intensity, _SoftFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float4 screenPos : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 c = t.rgb * t.a * i.color.rgb * i.color.a * _Color.rgb * _Intensity;
                // Depth fade against the scene
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float self = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                c *= saturate((scene - self) / max(_SoftFade, 1e-4));
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
