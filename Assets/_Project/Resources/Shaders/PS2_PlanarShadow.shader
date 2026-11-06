// PS2_PlanarShadow — the classic PS2-era character shadow: the character's own (skinned) mesh squashed onto the
// ground plane along the light direction, drawn as a translucent dark shape (no stencil: the low-res render target
// has none, so overlaps darken a little more, like many PS2 games). The ground height comes per renderer (_GroundY, set from C# by a raycast) and the
// shadow fades the higher the vertex is above it (jumps, climbing).
Shader "Gladiator/PS2_PlanarShadow"
{
    Properties
    {
        _ShadowColor ("Shadow colour (a = opacity)", Color) = (0.1, 0.07, 0.04, 0.3)
        _LightDir ("Light direction (world, pointing down)", Vector) = (0.35, -1, 0.25, 0)
        _GroundY ("Ground height", Float) = 0
        _FadeHeight ("Fade with height above ground", Float) = 3.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" }

        Pass
        {
            Name "PlanarShadow"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                float4 _LightDir;
                float _GroundY;
                float _FadeHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; half fade : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(v.positionOS.xyz);
                float3 L = normalize(_LightDir.xyz);
                L.y = min(L.y, -0.2);
                float h = max(0.0, w.y - _GroundY);
                // Slide the vertex down along the light until it meets the ground.
                float3 p = w + L * (h / -L.y);
                p.y = _GroundY + 0.03;
                o.positionCS = TransformWorldToHClip(p);
                o.fade = saturate(1.0 - h / max(0.01, _FadeHeight) * 0.35);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(_ShadowColor.rgb, _ShadowColor.a * i.fade);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
