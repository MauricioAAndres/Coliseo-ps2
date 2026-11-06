using System;
using System.Collections.Generic;

namespace Gladiator.Progression
{
    public enum StatType { Strength, Agility, Resistance }

    /// <summary>
    /// Persistent gladiator data that survives scene changes (owned by GameManager).
    /// Milestone 2: training points. Milestone 3 will read these to scale damage / speed / HP and add denarii.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public string Name = "Espartaco";
        public int Strength;
        public int Agility;
        public int Resistance;

        public int Wins;
        public int Losses;

        // ---- Milestone 3
        public int Denarii;
        // ---- Loadout (milestone C5): weapon ids from WeaponLibrary, shield id from ShieldLibrary ("" = none)
        public string MainHand = "gladius";
        /// <summary>Second one-handed weapon (dual wielding); excludes a shield on the arm.</summary>
        public string OffHand = "";
        /// <summary>With a two-hander or the bow the shield is carried on the back.</summary>
        public string ShieldId = "";
        public readonly HashSet<string> Owned = new HashSet<string> { "gladius" };

        /// <summary>Name of the main weapon (kept for logs and older code).</summary>
        public string ArenaWeapon
        {
            get { var w = Combat.WeaponLibrary.ByName(MainHand); return w != null ? w.Name : "Gladius"; }
            set { var w = Combat.WeaponLibrary.ByName(value); if (w != null) MainHand = w.Id; }
        }
        public bool HasShield => !string.IsNullOrEmpty(ShieldId);

        /// <summary>Armour worn, one piece id per slot (Character.ArmorSlot order; "" = nothing).</summary>
        public readonly string[] Armor = { "", "", "", "", "", "", "", "" };
        /// <summary>Side per slot for limb pieces: 0 both, 1 left, 2 right.</summary>
        public readonly int[] ArmorSides = new int[8];
        public Character.ArmorSet ArmorSet => Character.ArmorSet.FromIds(Armor, ArmorSides);

        // ---- Character creator
        Character.CharacterAppearance appearance;
        /// <summary>The look made in the creator (loaded lazily: PlayerPrefs can't be read during construction).</summary>
        public Character.CharacterAppearance Appearance
        {
            get => appearance ?? (appearance = Character.CharacterAppearance.LoadSaved());
            set { appearance = value; Changed?.Invoke(); }
        }

        public event Action Changed;

        public int Get(StatType s)
        {
            switch (s)
            {
                case StatType.Strength: return Strength;
                case StatType.Agility: return Agility;
                default: return Resistance;
            }
        }

        public void Add(StatType s, int points)
        {
            if (points == 0) return;
            switch (s)
            {
                case StatType.Strength: Strength += points; break;
                case StatType.Agility: Agility += points; break;
                default: Resistance += points; break;
            }
            Changed?.Invoke();
        }

        public void AddDenarii(int amount)
        {
            if (amount == 0) return;
            Denarii = Math.Max(0, Denarii + amount);
            Changed?.Invoke();
        }

        public bool Spend(int amount)
        {
            if (amount > Denarii) return false;
            Denarii -= amount;
            Changed?.Invoke();
            return true;
        }

        public void NotifyChanged() { Changed?.Invoke(); }

        public void RecordFight(bool won)
        {
            if (won) Wins++; else Losses++;
            Changed?.Invoke();
        }

        public static string Label(StatType s)
        {
            switch (s)
            {
                case StatType.Strength: return "FUERZA";
                case StatType.Agility: return "AGILIDAD";
                default: return "RESISTENCIA";
            }
        }

        public static string Short(StatType s)
        {
            switch (s)
            {
                case StatType.Strength: return "FUE";
                case StatType.Agility: return "AGI";
                default: return "RES";
            }
        }
    }
}
