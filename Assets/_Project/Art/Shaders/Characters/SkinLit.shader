// Skin for URP: PBR lighting (every light, shadows) plus what makes skin read as skin:
//  - subsurface scattering approximation: light wraps past the shadow edge and comes back tinted red (strongest
//    where the scatter mask says skin is thin: ears, nose, lips),
//  - a second, broad specular lobe (the oily sheen on top of the sharp highlight),
//  - tiled pore-level detail normal, plus the character's normal map,
//  - a soft rim for backlit silhouettes.
Shader "Alcax/Skin Lit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _ScatterMask ("Scatter Mask (thin areas, R)", 2D) = "white" {}
        _ScatterColor ("Scatter Color", Color) = (0.9, 0.25, 0.15, 1)
        _ScatterWrap ("Scatter Wrap", Range(0, 1)) = 0.55
        _ScatterStrength ("Scatter Strength", Range(0, 2)) = 0.8
        [Normal] _PoreMap ("Pore Detail Normal", 2D) = "bump" {}
        _PoreTiling ("Pore Tiling", Float) = 40
        _PoreStrength ("Pore Strength", Range(0, 2)) = 0.35
        _Smoothness ("Smoothness", Range(0, 1)) = 0.42
        _SheenSmoothness ("Second Lobe Smoothness", Range(0, 1)) = 0.2
        _SheenStrength ("Second Lobe Strength", Range(0, 1)) = 0.25
        _RimStrength ("Rim", Range(0, 1)) = 0.15
        // Removable clothing (CharacterOutfit): UV1 = (slots covering the vertex, underwear); skin under worn slots is cut out
        _HideMask ("Worn Outfit Slots (bits)", Float) = 0
        [Toggle] _UnderwearOn ("Draw Underwear", Float) = 0
        _UnderwearColor ("Underwear", Color) = (0.13, 0.13, 0.15, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); TEXTURE2D(_ScatterMask); TEXTURE2D(_PoreMap); SAMPLER(sampler_PoreMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ScatterColor;
            half _BumpScale, _ScatterWrap, _ScatterStrength, _PoreTiling, _PoreStrength, _Smoothness, _SheenSmoothness, _SheenStrength, _RimStrength;
            float _HideMask, _UnderwearOn;
            half4 _UnderwearColor;
        CBUFFER_END
        // 1 where a worn garment covers this vertex (the skin under it is cut out: no poke-through)
        float OutfitHidden(float2 m) { uint bits = (uint)(m.x + 0.5); return (bits & (uint)(_HideMask + 0.5)) != 0u ? 1.0 : 0.0; }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; float2 outfit : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; float4 tangentWS : TEXCOORD3; half fog : TEXCOORD4; float2 outfit : TEXCOORD5; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.outfit = float2(OutfitHidden(v.outfit), v.outfit.y);
                return o;
            }

            // Wrapped, tinted diffuse minus plain Lambert: the extra light that scattered under the surface
            half3 Scatter(Light L, half3 n, half mask)
            {
                half ndl = dot(n, L.direction);
                half wrap = saturate((ndl + _ScatterWrap) / (1 + _ScatterWrap));
                half extra = max(wrap - saturate(ndl), 0) * (0.5 + 0.5 * mask);
                return L.color * L.distanceAttenuation * lerp(1, L.shadowAttenuation, 0.6) * extra * _ScatterColor.rgb * _ScatterStrength;
            }

            half Sheen(Light L, half3 n, half3 v)
            {
                half3 h = SafeNormalize(L.direction + v);
                half r = 1 - _SheenSmoothness; half a2 = max(r * r * r * r, 1e-4);
                half nh = saturate(dot(n, h));
                half d = nh * nh * (a2 - 1) + 1;
                return a2 / (PI * d * d) * saturate(dot(n, L.direction)) * L.shadowAttenuation * L.distanceAttenuation;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(0.5 - i.outfit.x);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half underwear = step(0.5, i.outfit.y) * _UnderwearOn;   // fabric, not skin: its colour, matte, no scatter (off for now)
                albedo.rgb = lerp(albedo.rgb, _UnderwearColor.rgb, underwear);
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _BumpScale);
                half3 pore = UnpackNormalScale(SAMPLE_TEXTURE2D(_PoreMap, sampler_PoreMap, i.uv * _PoreTiling), _PoreStrength);
                nTS = normalize(half3(nTS.xy + pore.xy, nTS.z * pore.z));
                float3 nGeo = normalize(i.normalWS);
                float3 t = normalize(i.tangentWS.xyz), b = cross(nGeo, t) * i.tangentWS.w;
                half3 nWS = normalize(mul(nTS, float3x3(t, b, nGeo)));
                half mask = SAMPLE_TEXTURE2D(_ScatterMask, sampler_BaseMap, i.uv).r * (1 - underwear);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fog;
                inputData.bakedGI = SampleSH(nWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo.rgb;
                s.alpha = 1;
                s.metallic = 0;
                s.specular = half3(0.028, 0.028, 0.028);   // skin reflects about 2.8% at normal incidence
                s.smoothness = lerp(_Smoothness, 0.18, underwear);
                s.occlusion = 1;
                half4 c = UniversalFragmentPBR(inputData, s);

                // Scattering and the second lobe for the main light and the nearby lights
                Light main = GetMainLight(inputData.shadowCoord);
                half3 extra = Scatter(main, nGeo, mask) * albedo.rgb + main.color * Sheen(main, nWS, inputData.viewDirectionWS) * _SheenStrength * 0.04;
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    extra += Scatter(l, nGeo, mask) * albedo.rgb + l.color * Sheen(l, nWS, inputData.viewDirectionWS) * _SheenStrength * 0.04;
                LIGHT_LOOP_END
                #endif
                // Rim: thin backlit edge
                half rim = pow(1 - saturate(dot(nWS, inputData.viewDirectionWS)), 4) * _RimStrength;
                extra += rim * SampleSH(nWS) * albedo.rgb;
                c.rgb += extra;
                c.rgb = MixFog(c.rgb, i.fog);
                return half4(c.rgb, 1);
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection, _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 outfit : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float hidden : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - p);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(p, n, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.hidden = OutfitHidden(v.outfit);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { clip(0.5 - i.hidden); return 0; }
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
            struct Attributes { float4 positionOS : POSITION; float2 outfit : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float hidden : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.hidden = OutfitHidden(v.outfit); return o; }
            half4 frag(Varyings i) : SV_Target { clip(0.5 - i.hidden); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 outfit : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float hidden : TEXCOORD1; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); o.hidden = OutfitHidden(v.outfit); return o; }
            half4 frag(Varyings i) : SV_Target { clip(0.5 - i.hidden); return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
