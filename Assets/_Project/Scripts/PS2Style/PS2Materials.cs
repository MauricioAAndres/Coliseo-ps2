using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.PS2Style
{
    /// <summary>Creates and caches PS2_Lit materials. Every material in the prototype comes from here.</summary>
    public static class PS2Materials
    {
        public const string LitShaderName = "Gladiator/PS2_Lit";
        static Shader litShader;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int LightingId = Shader.PropertyToID("_LightingAmount");
        static readonly int FogId = Shader.PropertyToID("_FogAmount");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int CullId = Shader.PropertyToID("_Cull");

        public static Shader LitShader
        {
            get
            {
                if (litShader == null) litShader = Shader.Find(LitShaderName);
                return litShader;
            }
        }

        public static void Init(Shader shader)
        {
            if (shader != null) litShader = shader;
            if (LitShader == null) Debug.LogError("[PS2] Shader '" + LitShaderName + "' not found.");
        }

        public static Material Get(string key, Texture2D tex, Color color, float lighting = 1f, float fog = 1f,
                                   Color? emission = null, UnityEngine.Rendering.CullMode cull = UnityEngine.Rendering.CullMode.Back)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(LitShader) { name = "PS2_" + key };
            m.SetTexture(BaseMapId, tex != null ? tex : PS2Textures.White);
            m.SetColor(BaseColorId, color);
            m.SetFloat(LightingId, lighting);
            m.SetFloat(FogId, fog);
            m.SetColor(EmissionId, emission ?? Color.clear);
            m.SetFloat(CullId, (float)cull);
            cache[key] = m;
            return m;
        }

        /// <summary>Convenience: unlit flat colour (sparks, flames, blob shadows).</summary>
        public static Material Flat(Color color, float fog = 1f)
        {
            return Get("flat_" + ColorUtility.ToHtmlStringRGB(color), PS2Textures.White, color, 0f, fog);
        }
    }
}
