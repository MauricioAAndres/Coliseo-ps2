using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Progression
{
    /// <summary>
    /// Turns training points into combat numbers and scales opponents with the day count.
    ///   Fuerza      -> damage       x(1 + 0.06 per point)
    ///   Agilidad    -> attack speed x(1 + 0.04 per point, max x1.6) and move speed +0.06 m/s per point
    ///   Resistencia -> max HP        100 + 8 per point
    /// </summary>
    public static class StatEffects
    {
        public static float DamageMul(PlayerProfile p) => 1f + 0.06f * p.Strength;
        public static float SpeedMul(PlayerProfile p) => Mathf.Min(1.6f, 1f + 0.04f * p.Agility);
        public static float MoveSpeed(PlayerProfile p) => 4.6f + 0.06f * p.Agility;
        public static float MaxHP(PlayerProfile p) => 100f + 8f * p.Resistance;

        public static void ApplyToPlayer(Fighter f, PlayerProfile p)
        {
            f.DamageMultiplier = DamageMul(p);
            f.AttackSpeedMultiplier = SpeedMul(p);
            f.MoveSpeed = MoveSpeed(p);
            f.Health.SetMax(MaxHP(p), true);
        }

        /// <summary>Opponents get tougher every day; in free-for-all each one is a bit weaker (there are three).</summary>
        public static void ApplyToEnemy(Fighter f, int day, bool freeForAll)
        {
            // (free-for-all, 2 vs 1 and waves use the "many opponents" numbers)
            int d = Mathf.Max(0, day - 1);
            float hp = (freeForAll ? 70f : 95f) + d * (freeForAll ? 7f : 10f);
            f.Health.SetMax(hp, true);
            f.DamageMultiplier = (freeForAll ? 0.85f : 1f) + 0.06f * d;
            f.AttackSpeedMultiplier = Mathf.Min(1.4f, 1f + 0.03f * d);
            f.MoveSpeed = 4.2f + 0.05f * d;
        }

        /// <summary>Denarii for a fight: base for winning, a small purse for surviving a loss, bonus per kill.</summary>
        public static int Reward(bool won, bool freeForAll, int day, int kills)
            => Reward(freeForAll ? EncounterType.FreeForAll : EncounterType.Duel, won, day, kills, 0);

        /// <summary>
        /// Denarii per mode. Waves: 25 per enemy brought down, +200 for clearing the sixth wave (win or lose, the kills pay).
        /// </summary>
        public static int Reward(EncounterType mode, bool won, int day, int kills, int wavesCleared)
        {
            switch (mode)
            {
                case EncounterType.Waves: return 25 * kills + (won ? 200 : 0);
                case EncounterType.Champion: return won ? 250 + 20 * day : 15;
                case EncounterType.TwoVsOne: return (won ? 90 + 20 * day : 10) + 15 * kills;
                case EncounterType.FreeForAll: return (won ? 100 + 25 * day : 10) + 15 * kills;
                default: return (won ? 50 + 15 * day : 10) + 15 * kills;
            }
        }

        /// <summary>Wave fodder: easy to bring down (little health, weak, slow).</summary>
        public static void ApplyToWaveEnemy(Fighter f, int day)
        {
            f.Health.SetMax(42f + 3f * (day - 1), true);
            f.DamageMultiplier = 0.65f + 0.03f * (day - 1);
            f.AttackSpeedMultiplier = 0.85f;
            f.MoveSpeed = 3.9f;
        }

        /// <summary>The champion: much tougher, hits harder, a bit quicker.</summary>
        public static void ApplyToChampion(Fighter f, int day)
        {
            f.Health.SetMax(210f + 15f * (day - 1), true);
            f.DamageMultiplier = 1.15f + 0.05f * (day - 1);
            f.AttackSpeedMultiplier = Mathf.Min(1.5f, 1.15f + 0.03f * (day - 1));
            f.MoveSpeed = 4.7f + 0.04f * day;
        }

        public static string Describe(PlayerProfile p) =>
            "DAÑO x" + DamageMul(p).ToString("0.00") + "  VEL x" + SpeedMul(p).ToString("0.00") + "  VIDA " + Mathf.RoundToInt(MaxHP(p));
    }
}
