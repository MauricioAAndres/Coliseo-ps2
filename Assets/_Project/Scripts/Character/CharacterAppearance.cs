using System;
using UnityEngine;

namespace Gladiator.Character
{
    /// <summary>
    /// Everything the character creator edits. Sliders are 0..1 (0.5 = average); options are indices into
    /// the tables below. Purely cosmetic: the stats still come from training.
    /// Serialized with JsonUtility into PlayerPrefs so the last gladiator is remembered between sessions.
    /// </summary>
    [Serializable]
    public class CharacterAppearance
    {
        public string Name = "Espartaco";

        // ---- Body
        public float Femininity = 0f;
        /// <summary>Back: 0 = stooped like the raw animations, 1 = very upright. Default fairly upright.</summary>
        public float Posture = 0.65f;
        public float Height = 0.5f;
        public float Muscle = 0.5f;
        public float Fat = 0.25f;
        public float Shoulders = 0.5f;
        public float LegLength = 0.5f;
        public float ArmLength = 0.5f;

        // ---- Head
        public float HeadSize = 0.5f;
        public float Jaw = 0.5f;
        public float Nose = 0.5f;
        public float Neck = 0.5f;
        public int HairStyle = 1;
        public int Beard = 0;
        public int HairColor = 0;

        // ---- Style
        public int SkinTone = 2;
        /// <summary>Old single outfit (saves before the clothes update); see Shirt / Pants / Shoes.</summary>
        public int Outfit = 1;
        // ---- Clothes (milestone C6)
        public int ClothesVersion = 0;
        public int Shirt = 1;
        public int Pants = 0;
        public int Shoes = 0;
        public int PantsColor = 0;
        /// <summary>Shield paint: 0 = like the shirt, then red, blue, green, brown, black, white, orange.</summary>
        public int ShieldColor = 0;
        public int ClothColor = 0;
        public int Armor = 0;
        public int Metal = 0;
        public bool Helmet;
        public int HelmetCrest = 0;

        // ------------------------------------------------------------------ tables
        public static readonly string[] Names =
        {
            "Espartaco", "Crixo", "Flamma", "Priscus", "Verus", "Carpophorus", "Tetraites", "Spiculus",
            "Amazonia", "Achillia", "Valeria", "Livia", "Aurelia", "Marcus", "Draco", "Brutus"
        };

        public static readonly Color[] SkinTones =
        {
            new Color(1.00f, 0.86f, 0.74f), new Color(0.98f, 0.78f, 0.64f), new Color(0.90f, 0.70f, 0.55f),
            new Color(0.80f, 0.60f, 0.45f), new Color(0.68f, 0.48f, 0.34f), new Color(0.55f, 0.37f, 0.25f),
            new Color(0.42f, 0.28f, 0.19f), new Color(0.32f, 0.21f, 0.15f)
        };

        public static readonly string[] HairStyles = { "RAPADO", "CORTO", "RIZOS", "COLETA", "CRESTA", "MELENA" };
        public static readonly string[] Beards = { "NINGUNA", "PERILLA", "CORTA", "LARGA" };
        public static readonly string[] HairColorNames = { "CASTAÑO", "NEGRO", "RUBIO", "PELIRROJO", "CANOSO", "BLANCO" };
        public static readonly Color[] HairColors =
        {
            new Color(0.30f, 0.19f, 0.11f), new Color(0.10f, 0.08f, 0.07f), new Color(0.78f, 0.62f, 0.34f),
            new Color(0.62f, 0.25f, 0.10f), new Color(0.50f, 0.48f, 0.46f), new Color(0.88f, 0.86f, 0.82f)
        };

        public static readonly string[] Outfits = { "SUBLIGACULUM", "TUNICA CORTA", "TUNICA LARGA" };
        public static readonly string[] Shirts = { "SIN CAMISA", "TUNICA CORTA", "EXOMIS", "TUNICA LARGA", "MUSCULOSA" };
        public static readonly string[] PantsNames = { "SUBLIGACULUM", "BRACAE CORTAS", "BRACAE LARGAS", "FALDA CORTA", "FALDA LARGA" };
        public static readonly string[] ShoesNames = { "SANDALIAS", "CALIGAE" };
        public static readonly string[] ClothColorNames = { "LINO", "ROJO", "AZUL", "VERDE", "NEGRO", "PURPURA", "OCRE", "BLANCO" };
        public static readonly Color[] ClothColors =
        {
            new Color(0.62f, 0.50f, 0.36f), new Color(0.70f, 0.12f, 0.09f), new Color(0.20f, 0.30f, 0.60f),
            new Color(0.24f, 0.45f, 0.20f), new Color(0.16f, 0.14f, 0.13f), new Color(0.42f, 0.20f, 0.48f),
            new Color(0.78f, 0.55f, 0.18f), new Color(0.88f, 0.86f, 0.80f)
        };

        public static readonly string[] Armors = { "NINGUNA", "MANICA", "GREBAS", "MANICA + GREBAS" };
        public static readonly string[] MetalNames = { "BRONCE", "HIERRO", "ORO" };
        public static readonly Color[] Metals =
        {
            new Color(0.95f, 0.72f, 0.40f), new Color(0.62f, 0.62f, 0.64f), new Color(1.00f, 0.86f, 0.35f)
        };

        /// <summary>NPC team colours (not saved): override the palette entries.</summary>
        [NonSerialized] public Color? ClothOverride, CrestOverride;

        public Color SkinColor => SkinTones[Mathf.Clamp(SkinTone, 0, SkinTones.Length - 1)];
        public Color HairTint => HairColors[Mathf.Clamp(HairColor, 0, HairColors.Length - 1)];
        public Color ClothTint => ClothOverride ?? ClothColors[Mathf.Clamp(ClothColor, 0, ClothColors.Length - 1)];
        public static readonly string[] ShieldColorNames = { "COMO LA CAMISA", "ROJO", "AZUL", "VERDE", "MARRON", "NEGRO", "BLANCO", "NARANJA" };
        public static readonly Color[] ShieldColors =
        {
            Color.clear, new Color(0.7f, 0.12f, 0.08f), new Color(0.18f, 0.3f, 0.62f), new Color(0.2f, 0.45f, 0.18f),
            new Color(0.42f, 0.27f, 0.14f), new Color(0.12f, 0.11f, 0.1f), new Color(0.88f, 0.86f, 0.8f), new Color(0.9f, 0.45f, 0.1f)
        };
        public Color ShieldTint => ShieldColor <= 0 || ShieldColor >= ShieldColors.Length ? ClothTint : ShieldColors[ShieldColor];
        public Color PantsTint => ClothColors[Mathf.Clamp(PantsColor, 0, ClothColors.Length - 1)] * 0.85f + Color.black * 0.15f;

        /// <summary>Saves from before the clothes update: turn the old outfit into shirt + pants.</summary>
        public void MigrateClothes()
        {
            if (ClothesVersion >= 1) return;
            ClothesVersion = 1;
            Shirt = Outfit == 0 ? 0 : Outfit == 1 ? 1 : 3;
            Pants = 0;
            Shoes = 0;
            PantsColor = ClothColor;
        }
        public Color MetalTint => Metals[Mathf.Clamp(Metal, 0, Metals.Length - 1)];
        public Color CrestTint => CrestOverride ?? ClothColors[Mathf.Clamp(HelmetCrest, 0, ClothColors.Length - 1)];

        /// <summary>Standing height in metres (top of the head).</summary>
        public float HeightMeters => Mathf.Lerp(1.60f, 1.98f, Height) - 0.08f * Femininity;

        public CharacterAppearance Clone() => (CharacterAppearance)MemberwiseClone();

        // ------------------------------------------------------------------ presets
        public static CharacterAppearance Random(System.Random rng, bool allowFemale = true)
        {
            float R() => (float)rng.NextDouble();
            float Mid(float spread) => Mathf.Clamp01(0.5f + (R() + R() - 1f) * spread);
            var a = new CharacterAppearance();
            a.Femininity = allowFemale && R() < 0.3f ? Mathf.Lerp(0.75f, 1f, R()) : Mathf.Lerp(0f, 0.2f, R());
            bool fem = a.Femininity > 0.5f;
            a.Name = fem ? Names[8 + rng.Next(5)] : Names[rng.Next(8)];
            a.Height = Mid(0.9f);
            a.Muscle = Mid(1f);
            a.Fat = Mathf.Clamp01(R() * R() * 0.9f);
            a.Shoulders = Mid(0.8f);
            a.LegLength = Mid(0.6f);
            a.ArmLength = Mid(0.5f);
            a.HeadSize = Mid(0.5f);
            a.Jaw = Mid(1f);
            a.Nose = Mid(1f);
            a.Neck = Mid(0.8f);
            a.HairStyle = fem ? new[] { 1, 2, 3, 5 }[rng.Next(4)] : rng.Next(HairStyles.Length);
            a.Beard = fem ? 0 : rng.Next(Beards.Length);
            a.HairColor = rng.Next(HairColors.Length - 1);
            a.SkinTone = rng.Next(SkinTones.Length);
            a.Outfit = rng.Next(Outfits.Length);
            a.ClothesVersion = 1;
            a.Shirt = rng.Next(Shirts.Length);
            a.Pants = rng.Next(PantsNames.Length);
            a.Shoes = rng.Next(ShoesNames.Length);
            a.PantsColor = rng.Next(ClothColors.Length);
            a.ClothColor = rng.Next(ClothColors.Length);
            a.Armor = rng.Next(Armors.Length);
            a.Metal = rng.Next(Metals.Length);
            a.Helmet = R() < 0.25f;
            a.HelmetCrest = rng.Next(ClothColors.Length);
            return a;
        }

        // ------------------------------------------------------------------ persistence
        const string PrefsKey = "Gladiator.Appearance.v1";

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        /// <summary>The last gladiator saved by the creator, or the default one.</summary>
        public static CharacterAppearance LoadSaved()
        {
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { var a = JsonUtility.FromJson<CharacterAppearance>(json); a.MigrateClothes(); return a; }
                catch (Exception) { /* corrupt prefs: fall through to the default */ }
            }
            return new CharacterAppearance();
        }
    }
}
