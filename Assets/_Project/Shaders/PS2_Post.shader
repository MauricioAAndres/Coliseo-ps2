// PS2_Post — full-screen "output" pass. The game is rendered into a low-res
// RenderTexture (e.g. 640x448-ish) which a RawImage stretches to the window with
// point filtering. This shader adds:
//  * 16-bit-framebuffer style colour quantisation with 4x4 ordered (Bayer) dithering
//  * very subtle interlace scanlines (per source row)
//  * light chromatic aberration growing towards the edges
Shader "Gladiator/PS2_Post"
{
    Properties
    {
        [PerRendererData] _MainTex ("Source", 2D) = "white" {}
        _SrcRes ("Source Resolution (w, h, 1/w, 1/h)", Vector) = (640, 448, 0.0015625, 0.00223)
        _Scanline ("Scanline Strength", Range(0, 0.5)) = 0.12
        _Chroma ("Chromatic Aberration (px)", Range(0, 3)) = 0.8
        _ColorLevels ("Colour Levels Per Channel", Float) = 32
        _Dither ("Dither Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Opaque" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _SrcRes;
            float _Scanline;
            float _Chroma;
            float _ColorLevels;
            float _Dither;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            static const float bayer4x4[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            fixed4 frag(v2f i) : SV_Target
            {
                float2 px = i.uv * _SrcRes.xy;
                float2 fromCenter = i.uv - 0.5;
                float2 off = fromCenter * 2.0 * _Chroma * _SrcRes.zw;

                float3 col;
                col.r = tex2D(_MainTex, i.uv + off).r;
                col.g = tex2D(_MainTex, i.uv).g;
                col.b = tex2D(_MainTex, i.uv - off).b;

                #ifndef UNITY_COLORSPACE_GAMMA
                col = LinearToGammaSpace(col);
                #endif

                int2 ip = int2(fmod(floor(px), 4.0));
                float threshold = bayer4x4[ip.y * 4 + ip.x] / 16.0 - 0.5;
                col = floor(col * _ColorLevels + 0.5 + threshold * _Dither) / _ColorLevels;

                float scan = fmod(floor(px.y), 2.0);
                col *= 1.0 - _Scanline * scan;
                col = saturate(col);

                #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(col);
                #endif
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
