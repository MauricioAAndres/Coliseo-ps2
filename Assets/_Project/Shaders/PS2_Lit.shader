// PS2_Lit — Gouraud / vertex-lit shader for URP (Forward renderer).
// * Lighting is computed per VERTEX (main light + additional lights), then interpolated.
// * Ambient light is NOT computed here: it is baked into the mesh vertex colors
//   (see LowPolyMesh.BakeAmbient), exactly like many PS2-era engines did.
// * Optional vertex snapping to a low-res grid (global _PS2_SnapRes, driven by PS2RenderSetup).
// * Linear/exp fog via URP's standard fog keywords.
// * PS3 look (global _PS2_Quality = 1): per-pixel light, real sun shadows and a soft highlight instead.
Shader "Gladiator/PS2_Lit"
{
    Properties
    {
        _BaseMap ("Texture (max 256px)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _LightingAmount ("Vertex Lighting Amount (0 = unlit)", Range(0,1)) = 1
        _FogAmount ("Fog Amount", Range(0,1)) = 1
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        _FlashColor ("Hit Flash (rgb, a = amount)", Color) = (1,1,1,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _LightingAmount;
            half _FogAmount;
            half4 _EmissionColor;
            half4 _FlashColor;
            float _Cull;
        CBUFFER_END

        // Global, set from C#: xy = snap grid resolution, z = enabled (0/1)
        float4 _PS2_SnapRes;
        // Global: 0 = PS2 (per-vertex light), 1 = PS3 (per-pixel light, shadows, highlight)
        float _PS2_Quality;

        float4 PS2Snap(float4 positionCS)
        {
            if (_PS2_SnapRes.z < 0.5 || positionCS.w <= 0.0001)
                return positionCS;
            float2 grid = _PS2_SnapRes.xy * 0.5;
            float2 ndc = positionCS.xy / positionCS.w;
            ndc = round(ndc * grid) / grid;
            positionCS.xy = ndc * positionCS.w;
            return positionCS;
        }
        ENDHLSL

        Pass
        {
            Name "PS2VertexLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            // Shadow sampling is always compiled in (no keyword variants to be stripped): with the PS2 look the
            // sun casts no shadows, the shadow map is empty and the per-pixel path is not taken anyway.
            #define _MAIN_LIGHT_SHADOWS_CASCADE 1
            #define _SHADOWS_SOFT 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  light      : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                half3  normalWS   : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                float4 positionCS = TransformWorldToHClip(positionWS);

                o.fogFactor = ComputeFogFactor(positionCS.z);
                o.positionCS = PS2Snap(positionCS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.positionWS = positionWS;
                o.normalWS = normalWS;

                // Ambient: baked into vertex colors.
                half3 ambient = input.color.rgb;
                if (_PS2_Quality > 0.5)
                {
                    o.light = input.color; // PS3: direct light is done per pixel
                    return o;
                }

                // Direct light, evaluated per vertex (Gouraud).
                Light mainLight = GetMainLight();
                half3 direct = mainLight.color * (mainLight.distanceAttenuation * saturate(dot(normalWS, mainLight.direction)));

                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; ++li)
                {
                    Light l = GetAdditionalLight(li, positionWS);
                    direct += l.color * (l.distanceAttenuation * saturate(dot(normalWS, l.direction)));
                }
                #endif

                o.light = half4(lerp(ambient, ambient + direct, _LightingAmount), input.color.a);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 lit = i.light.rgb;
                half3 spec = 0;
                if (_PS2_Quality > 0.5)
                {
                    half3 n = normalize(i.normalWS);
                    half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                    Light ml = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                    half atten = ml.distanceAttenuation * lerp(1.0, ml.shadowAttenuation, step(0.01, _LightingAmount));
                    half ndl = saturate(dot(n, ml.direction));
                    half3 direct = ml.color * (atten * ndl);
                    half3 h = normalize(ml.direction + v);
                    spec = ml.color * (atten * pow(saturate(dot(n, h)), 32.0) * 0.18 * ndl);
                    #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    uint lightCount = GetAdditionalLightsCount();
                    for (uint li = 0u; li < lightCount; ++li)
                    {
                        Light l = GetAdditionalLight(li, i.positionWS);
                        direct += l.color * (l.distanceAttenuation * saturate(dot(n, l.direction)));
                    }
                    #endif
                    // Soft sky/ground fill so the shadowed side keeps some shape.
                    half3 fill = i.light.rgb * (0.9 + 0.15 * n.y);
                    lit = lerp(i.light.rgb, fill + direct, _LightingAmount);
                    spec *= _LightingAmount;
                }
                half3 col = tex.rgb * lit + spec + _EmissionColor.rgb;
                col = lerp(col, _FlashColor.rgb, _FlashColor.a);
                half3 fogged = MixFog(col, i.fogFactor);
                col = lerp(col, fogged, _FogAmount);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            struct AttributesD { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct VaryingsD { float4 positionCS : SV_POSITION; };

            VaryingsD vertDepth(AttributesD input)
            {
                VaryingsD o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = PS2Snap(TransformObjectToHClip(input.positionOS.xyz));
                return o;
            }

            half fragDepth(VaryingsD i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct AttributesS { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct VaryingsS { float4 positionCS : SV_POSITION; };

            VaryingsS vertShadow(AttributesS input)
            {
                VaryingsS o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            // Unlit things (flames, sparks, blob discs) do not cast shadows.
            half4 fragShadow(VaryingsS i) : SV_Target { clip(_LightingAmount - 0.01); return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
