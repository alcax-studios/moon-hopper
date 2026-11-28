// Kelxor terrain: four layers from the Terrain system (control map, splats, normals, masks) with
//  - height-based blending (sand settles into rock crevices instead of cross-fading),
//  - triplanar projection on steep slopes (cliffs aren't stretched),
//  - anti-tiling: each layer sampled at two scales, blended by distance, plus a large-scale color variation,
//  - per-layer color matching (_LayerTint) so every scan sits in the art bible's sand palette.
// URP PBR lighting with shadows. Used by KelxorBuilder (not instanced terrain).
Shader "Alcax/Kelxor Terrain"
{
    Properties
    {
        [HideInInspector] _Control ("Control", 2D) = "red" {}
        [HideInInspector] _Splat0 ("Layer 0", 2D) = "grey" {}
        [HideInInspector] _Splat1 ("Layer 1", 2D) = "grey" {}
        [HideInInspector] _Splat2 ("Layer 2", 2D) = "grey" {}
        [HideInInspector] _Splat3 ("Layer 3", 2D) = "grey" {}
        [HideInInspector] _Normal0 ("Normal 0", 2D) = "bump" {}
        [HideInInspector] _Normal1 ("Normal 1", 2D) = "bump" {}
        [HideInInspector] _Normal2 ("Normal 2", 2D) = "bump" {}
        [HideInInspector] _Normal3 ("Normal 3", 2D) = "bump" {}
        [HideInInspector] _Mask0 ("Mask 0", 2D) = "grey" {}
        [HideInInspector] _Mask1 ("Mask 1", 2D) = "grey" {}
        [HideInInspector] _Mask2 ("Mask 2", 2D) = "grey" {}
        [HideInInspector] _Mask3 ("Mask 3", 2D) = "grey" {}
        _Tint0 ("Layer 0 Tint", Color) = (1, 1, 1, 1)
        _Tint1 ("Layer 1 Tint", Color) = (1, 1, 1, 1)
        _Tint2 ("Layer 2 Tint", Color) = (1, 1, 1, 1)
        _Tint3 ("Layer 3 Tint", Color) = (1, 1, 1, 1)
        _Tile ("Layer Tile Sizes (m)", Vector) = (2.4, 9, 2.6, 3.2)
        _HeightBlend ("Height Blend Sharpness", Range(0.01, 1)) = 0.15
        _TriplanarSlope ("Triplanar From (normal.y)", Range(0, 1)) = 0.8
        _FarScale ("Far UV Scale", Float) = 4.2
        _FarStart ("Far Blend Start (m)", Float) = 12
        _FarEnd ("Far Blend End (m)", Float) = 70
        _MacroScale ("Macro Variation Scale (1/m)", Float) = 0.012
        _MacroStrength ("Macro Variation", Range(0, 1)) = 0.22
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-100" "RenderPipeline"="UniversalPipeline" "TerrainCompatible"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_Control); SAMPLER(sampler_Control);
        TEXTURE2D(_Splat0); SAMPLER(sampler_Splat0); TEXTURE2D(_Splat1); TEXTURE2D(_Splat2); TEXTURE2D(_Splat3);
        TEXTURE2D(_Normal0); SAMPLER(sampler_Normal0); TEXTURE2D(_Normal1); TEXTURE2D(_Normal2); TEXTURE2D(_Normal3);
        TEXTURE2D(_Mask0); TEXTURE2D(_Mask1); TEXTURE2D(_Mask2); TEXTURE2D(_Mask3);
        CBUFFER_START(UnityPerMaterial)
            float4 _Control_TexelSize;
            half4 _Tint0, _Tint1, _Tint2, _Tint3;
            float4 _Tile;
            half _HeightBlend, _TriplanarSlope, _FarScale, _FarStart, _FarEnd, _MacroScale, _MacroStrength;
        CBUFFER_END
        float4 _TerrainHeightmapRecipSize;
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; half fog : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            // One layer: flat (top) projection, or triplanar on steep ground; near and far scales blended by distance.
            // Normals come back as a world-space detail perturbation (UDN blend) added to the geometry normal.
            struct LayerSample { half3 albedo; half3 pert; half ao; half height; half smooth; };

            #define SAMPLE_LAYER(T, N, M, uv, c, n, m)                 c = SAMPLE_TEXTURE2D(T, sampler_Splat0, uv);                 n = UnpackNormal(SAMPLE_TEXTURE2D(N, sampler_Normal0, uv));                 m = SAMPLE_TEXTURE2D(M, sampler_Splat0, uv);

            LayerSample Layer(int idx, float3 pws, float3 nGeo, float tri, float farK)
            {
                float tile = idx == 0 ? _Tile.x : idx == 1 ? _Tile.y : idx == 2 ? _Tile.z : _Tile.w;
                float3 w = pow(abs(nGeo), 4); w /= (w.x + w.y + w.z);
                w = lerp(float3(0, 1, 0), w, tri);
                half4 cOut = 0, mOut = 0; half3 pOut = 0;
                [unroll] for (int s = 0; s < 2; s++)
                {
                    float sc = (s == 0 ? 1.0 : 1.0 / _FarScale) / tile;
                    float2 uvY = pws.xz * sc, uvX = pws.zy * sc, uvZ = pws.xy * sc;
                    half4 cY, cX, cZ, mY, mX, mZ; half3 nY, nX, nZ;
                    if (idx == 0) { SAMPLE_LAYER(_Splat0, _Normal0, _Mask0, uvY, cY, nY, mY) SAMPLE_LAYER(_Splat0, _Normal0, _Mask0, uvX, cX, nX, mX) SAMPLE_LAYER(_Splat0, _Normal0, _Mask0, uvZ, cZ, nZ, mZ) }
                    else if (idx == 1) { SAMPLE_LAYER(_Splat1, _Normal1, _Mask1, uvY, cY, nY, mY) SAMPLE_LAYER(_Splat1, _Normal1, _Mask1, uvX, cX, nX, mX) SAMPLE_LAYER(_Splat1, _Normal1, _Mask1, uvZ, cZ, nZ, mZ) }
                    else if (idx == 2) { SAMPLE_LAYER(_Splat2, _Normal2, _Mask2, uvY, cY, nY, mY) SAMPLE_LAYER(_Splat2, _Normal2, _Mask2, uvX, cX, nX, mX) SAMPLE_LAYER(_Splat2, _Normal2, _Mask2, uvZ, cZ, nZ, mZ) }
                    else { SAMPLE_LAYER(_Splat3, _Normal3, _Mask3, uvY, cY, nY, mY) SAMPLE_LAYER(_Splat3, _Normal3, _Mask3, uvX, cX, nX, mX) SAMPLE_LAYER(_Splat3, _Normal3, _Mask3, uvZ, cZ, nZ, mZ) }
                    half4 c = cX * w.x + cY * w.y + cZ * w.z;
                    half4 m = mX * w.x + mY * w.y + mZ * w.z;
                    // Tangent detail of each projection, expressed along the world axes it lies in
                    half3 p = half3(0, nX.y, nX.x) * w.x + half3(nY.x, 0, nY.y) * w.y + half3(nZ.x, nZ.y, 0) * w.z;
                    half k = s == 0 ? 1 - farK : farK;
                    cOut += c * k; mOut += m * k; pOut += p * k;
                }
                LayerSample o;
                o.albedo = cOut.rgb;
                o.pert = pOut;
                o.ao = mOut.g;
                o.height = mOut.b;
                o.smooth = mOut.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 nGeo = normalize(i.normalWS);
                float2 cuv = i.uv * (_Control_TexelSize.zw - 1) * _Control_TexelSize.xy + 0.5 * _Control_TexelSize.xy;
                half4 ctrl = SAMPLE_TEXTURE2D(_Control, sampler_Control, cuv);
                float tri = saturate((_TriplanarSlope - nGeo.y) / 0.15);
                float dist = distance(GetCameraPositionWS(), i.positionWS);
                float farK = saturate((dist - _FarStart) / max(_FarEnd - _FarStart, 1));

                LayerSample L0 = Layer(0, i.positionWS, nGeo, tri, farK);
                LayerSample L1 = Layer(1, i.positionWS, nGeo, tri, farK);
                LayerSample L2 = Layer(2, i.positionWS, nGeo, tri, farK);
                LayerSample L3 = Layer(3, i.positionWS, nGeo, 1, farK);   // rock is always projected from the sides on slopes

                // Height blend: the layer whose height (plus weight) is highest wins, with a soft transition
                half4 hgt = half4(L0.height, L1.height, L2.height, L3.height) + ctrl;
                half top = max(max(hgt.x, hgt.y), max(hgt.z, hgt.w));
                half4 wgt = max(hgt - top + _HeightBlend, 0) * step(0.001, ctrl);
                wgt /= max(dot(wgt, 1), 1e-4);

                half3 albedo = L0.albedo * _Tint0.rgb * wgt.x + L1.albedo * _Tint1.rgb * wgt.y + L2.albedo * _Tint2.rgb * wgt.z + L3.albedo * _Tint3.rgb * wgt.w;
                half3 nWS = normalize(nGeo + L0.pert * wgt.x + L1.pert * wgt.y + L2.pert * wgt.z + L3.pert * wgt.w);
                half ao = dot(half4(L0.ao, L1.ao, L2.ao, L3.ao), wgt);
                half sm = dot(half4(L0.smooth, L1.smooth, L2.smooth, L3.smooth), wgt);

                // Large-scale color variation breaks up repetition
                half macro = Noise(i.positionWS.xz * _MacroScale) * 0.6 + Noise(i.positionWS.xz * _MacroScale * 3.7 + 11) * 0.4;
                albedo *= lerp(1 - _MacroStrength, 1 + _MacroStrength * 0.6, macro);

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalize(nWS);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fog;
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.smoothness = sm * 0.6;
                s.occlusion = ao;
                half4 c = UniversalFragmentPBR(input, s);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(p, n, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
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
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
