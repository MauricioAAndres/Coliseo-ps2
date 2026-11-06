using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Character
{
    public enum ArmorSlot { Helmet, Chest, Shoulders, Arms, Gloves, Legs, Ankles, Boots }
    public enum ArmorZone { Head, Torso, Arms, Legs }
    /// <summary>Limb pieces can be worn on both sides or only the left / right one.</summary>
    public enum ArmorSide { Both, Left, Right }

    /// <summary>One armour piece: where it goes, how much it weighs, and how much damage it stops per zone.</summary>
    public class ArmorPiece
    {
        public string Id, Name, Description;
        public ArmorSlot Slot;
        public bool Heavy;
        /// <summary>Helmets: 0 leather cap, 1 bronze galea, 2 closed murmillo helm.</summary>
        public int Model;
        public float Weight;
        /// <summary>Fraction of the damage stopped for Head, Torso, Arms, Legs.</summary>
        public float[] Protect = new float[4];
        public int Price;
    }

    /// <summary>
    /// The armoury: two pieces (light leather / heavy iron) for each body slot, and three helmets.
    /// Weights in kg; a full heavy set is about 32 kg.
    /// </summary>
    public static class ArmorLibrary
    {
        public const float MaxWeight = 32f;

        static ArmorPiece P(string id, string name, ArmorSlot slot, bool heavy, float kg, float head, float torso, float arms, float legs,
                            int price, string desc, int model = 0) => new ArmorPiece
        {
            Id = id, Name = name, Slot = slot, Heavy = heavy, Weight = kg, Model = model, Price = price, Description = desc,
            Protect = new[] { head, torso, arms, legs },
        };

        public static readonly ArmorPiece[] All =
        {
            P("galea_cuero", "Galea de cuero", ArmorSlot.Helmet, false, 1.5f, 0.22f, 0, 0, 0, 35, "Gorro de cuero endurecido. Cabeza -22%.", 0),
            P("galea_bronce", "Galea de bronce", ArmorSlot.Helmet, false, 2.5f, 0.32f, 0, 0, 0, 70, "Casco abierto de bronce con penacho. Cabeza -32%.", 1),
            P("yelmo", "Yelmo de mirmillon", ArmorSlot.Helmet, true, 5f, 0.6f, 0, 0, 0, 160, "Casco cerrado con rejilla. Cabeza -60%.", 2),
            P("pectoral_cuero", "Pectoral de cuero", ArmorSlot.Chest, false, 4f, 0, 0.2f, 0, 0, 80, "Coraza de cuero. Torso -20%."),
            P("lorica", "Lorica segmentata", ArmorSlot.Chest, true, 9f, 0, 0.45f, 0, 0, 220, "Placas de hierro. Torso -45%."),
            P("hombreras_cuero", "Hombreras de cuero", ArmorSlot.Shoulders, false, 1.5f, 0, 0.05f, 0.06f, 0, 40, "Tiras de cuero (pteruges). Torso y brazos -5%."),
            P("hombreras_hierro", "Hombreras de hierro", ArmorSlot.Shoulders, true, 3.5f, 0.05f, 0.1f, 0.12f, 0, 90, "Placas sobre los hombros. Torso -10%, brazos -12%."),
            P("manica_lino", "Manica acolchada", ArmorSlot.Arms, false, 1.5f, 0, 0, 0.15f, 0, 40, "Brazales de lino y cuero. Brazos -15%."),
            P("manica_hierro", "Manica de hierro", ArmorSlot.Arms, true, 4f, 0, 0, 0.32f, 0, 110, "Brazos segmentados de hierro. Brazos -32%."),
            P("guantes_cuero", "Guantes de cuero", ArmorSlot.Gloves, false, 0.5f, 0, 0, 0.05f, 0, 25, "Brazos -5%."),
            P("guanteletes", "Guanteletes", ArmorSlot.Gloves, true, 1.5f, 0, 0, 0.1f, 0, 60, "Guantes con placas. Brazos -10%."),
            P("musleras_cuero", "Musleras de cuero", ArmorSlot.Legs, false, 2f, 0, 0, 0, 0.15f, 50, "Protegen muslos y rodillas. Piernas -15%."),
            P("musleras_hierro", "Musleras de hierro", ArmorSlot.Legs, true, 4.5f, 0, 0, 0, 0.3f, 120, "Placas en muslos y rodilleras. Piernas -30%."),
            P("tobilleras_cuero", "Tobilleras de cuero", ArmorSlot.Ankles, false, 0.5f, 0, 0, 0, 0.05f, 30, "Vendas de cuero. Piernas -5%."),
            P("grebas", "Grebas", ArmorSlot.Ankles, true, 2f, 0, 0, 0, 0.15f, 80, "Grebas de hierro en las espinillas. Piernas -15%."),
            P("botas_cuero", "Botas de cuero", ArmorSlot.Boots, false, 1f, 0, 0, 0, 0.05f, 35, "Sustituyen a las sandalias. Piernas -5%."),
            P("botas_hierro", "Botas de hierro", ArmorSlot.Boots, true, 2.5f, 0, 0, 0, 0.1f, 90, "Botas con placas. Piernas -10%."),
        };

        public static ArmorPiece ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }

        /// <summary>Slots whose piece can go on one side only (shoulders, arms, gloves, thighs, shins).</summary>
        public static bool Sided(ArmorSlot s) => s == ArmorSlot.Shoulders || s == ArmorSlot.Arms || s == ArmorSlot.Gloves ||
                                                 s == ArmorSlot.Legs || s == ArmorSlot.Ankles;

        public static string SideName(ArmorSide s) => s == ArmorSide.Left ? "IZQ." : s == ArmorSide.Right ? "DER." : "AMBOS";

        public static string SlotName(ArmorSlot s)
        {
            switch (s)
            {
                case ArmorSlot.Helmet: return "CASCO";
                case ArmorSlot.Chest: return "PECHERA";
                case ArmorSlot.Shoulders: return "HOMBRERAS";
                case ArmorSlot.Arms: return "BRAZOS";
                case ArmorSlot.Gloves: return "GUANTES";
                case ArmorSlot.Legs: return "PIERNAS";
                case ArmorSlot.Ankles: return "TOBILLERAS";
                default: return "BOTAS";
            }
        }
    }

    /// <summary>What a fighter wears: one piece (or none) per slot.</summary>
    public class ArmorSet
    {
        public const int SlotCount = 8;
        public readonly ArmorPiece[] Pieces = new ArmorPiece[SlotCount];
        public readonly ArmorSide[] Sides = new ArmorSide[SlotCount];

        /// <summary>Does the piece in this slot cover the given side (-1 left, +1 right)?</summary>
        public bool Covers(ArmorSlot s, int side)
        {
            if (Pieces[(int)s] == null) return false;
            var sd = Sides[(int)s];
            return sd == ArmorSide.Both || (sd == ArmorSide.Left && side < 0) || (sd == ArmorSide.Right && side > 0);
        }

        float Share(int slot) => Sides[slot] == ArmorSide.Both ? 1f : 0.5f;

        public static ArmorSet Empty => new ArmorSet();

        public ArmorPiece this[ArmorSlot s] => Pieces[(int)s];

        public static ArmorSet FromIds(IEnumerable<string> ids) => FromIds(ids, null);

        /// <summary>Pieces by id, with an optional side per slot (0 both, 1 left, 2 right).</summary>
        public static ArmorSet FromIds(IEnumerable<string> ids, int[] sides)
        {
            var set = new ArmorSet();
            if (ids != null)
                foreach (var id in ids)
                {
                    var p = ArmorLibrary.ById(id);
                    if (p != null) set.Pieces[(int)p.Slot] = p;
                }
            if (sides != null)
                for (int i = 0; i < SlotCount && i < sides.Length; i++)
                    set.Sides[i] = ArmorLibrary.Sided((ArmorSlot)i) ? (ArmorSide)Mathf.Clamp(sides[i], 0, 2) : ArmorSide.Both;
            return set;
        }

        public float Weight
        {
            get { float w = 0f; for (int i = 0; i < SlotCount; i++) if (Pieces[i] != null) w += Pieces[i].Weight * Share(i); return w; }
        }

        /// <summary>0..1: share of the heaviest possible set.</summary>
        public float Load => Mathf.Clamp01(Weight / ArmorLibrary.MaxWeight);

        /// <summary>Damage stopped in a zone (pieces add up, at most 75%).</summary>
        public float Protection(ArmorZone zone)
        {
            float v = 0f;
            for (int i = 0; i < SlotCount; i++) if (Pieces[i] != null) v += Pieces[i].Protect[(int)zone] * Share(i);
            return Mathf.Min(0.75f, v);
        }

        public bool Has(ArmorSlot s) => Pieces[(int)s] != null;
        public int Count { get { int n = 0; foreach (var p in Pieces) if (p != null) n++; return n; } }

        /// <summary>
        /// Opponents' armour by day: days 1-6 none; days 7-12 some wear light (leather) pieces; from day 13 some wear the
        /// best (full iron) set and others light pieces. The champion has its own set (see Champion).
        /// </summary>
        public static ArmorSet RandomForDay(int day, System.Random rng)
        {
            var set = new ArmorSet();
            if (day <= 6) return set;
            bool armoured = rng.NextDouble() < (day <= 12 ? 0.5 : 0.85);
            if (!armoured) return set;
            if (day >= 13 && rng.NextDouble() < 0.45)
            {
                foreach (var p in ArmorLibrary.All) if (p.Heavy) set.Pieces[(int)p.Slot] = p;
                return set;
            }
            int pieces = rng.Next(day <= 12 ? 2 : 4, day <= 12 ? 5 : 9);
            var slots = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            for (int k = 0; k < pieces && slots.Count > 0; k++)
            {
                int pick = rng.Next(slots.Count);
                int slot = slots[pick];
                slots.RemoveAt(pick);
                var options = new List<ArmorPiece>();
                foreach (var p in ArmorLibrary.All) if ((int)p.Slot == slot && !p.Heavy) options.Add(p);
                set.Pieces[slot] = options[rng.Next(options.Count)];
            }
            return set;
        }

        /// <summary>The champion: every heavy piece (shown in gold through the appearance's metal colour).</summary>
        public static ArmorSet Champion()
        {
            var set = new ArmorSet();
            foreach (var p in ArmorLibrary.All) if (p.Heavy) set.Pieces[(int)p.Slot] = p;
            return set;
        }

        /// <summary>Old progression (kept for reference): a helmet first, iron from day 3.</summary>
        public static ArmorSet RandomByDayOld(int day, System.Random rng)
        {
            var set = new ArmorSet();
            int pieces = Mathf.Clamp(1 + (day - 1) * 2 + rng.Next(0, 2), 1, SlotCount);
            float heavyChance = Mathf.Clamp01((day - 2) * 0.3f);
            var slots = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            for (int k = 0; k < pieces && slots.Count > 0; k++)
            {
                int pick = k == 0 ? 0 : rng.Next(slots.Count);   // the first piece is always a helmet
                int slot = slots[pick];
                slots.RemoveAt(pick);
                var options = new List<ArmorPiece>();
                bool heavy = rng.NextDouble() < heavyChance;
                foreach (var p in ArmorLibrary.All) if ((int)p.Slot == slot && p.Heavy == heavy) options.Add(p);
                if (options.Count == 0) foreach (var p in ArmorLibrary.All) if ((int)p.Slot == slot) options.Add(p);
                set.Pieces[slot] = options[rng.Next(options.Count)];
            }
            return set;
        }

        /// <summary>Old appearance fields (NPCs of the ludus): helmet / manica / greaves.</summary>
        public static ArmorSet FromLegacy(CharacterAppearance a)
        {
            var set = new ArmorSet();
            if (a.Helmet) set.Pieces[(int)ArmorSlot.Helmet] = ArmorLibrary.ById("galea_bronce");
            if (a.Armor == 1 || a.Armor == 3) set.Pieces[(int)ArmorSlot.Arms] = ArmorLibrary.ById("manica_hierro");
            if (a.Armor >= 2) set.Pieces[(int)ArmorSlot.Ankles] = ArmorLibrary.ById("grebas");
            return set;
        }
    }
}
