using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Progression
{
    /// <summary>
    /// What the town's services leave you with until your next fight in the arena: the bonus of a bath at the thermae
    /// and the wager you placed on yourself at the basilica. Both are settled when the fight ends.
    /// </summary>
    public static class Boons
    {
        public const int BathPrice = 15;
        /// <summary>A bath: this much of your health back, and for your next fight more health and quicker feet.</summary>
        public const float BathHeal = 0.5f, BathHealth = 1.10f, BathSpeed = 1.05f;

        /// <summary>Bathed and not fought since: the bonus is waiting for the next arena fight.</summary>
        public static bool Bathed;
        /// <summary>Denarii staked on winning the next arena fight (0 = no wager). A win pays it back doubled.</summary>
        public static int Wager;
        /// <summary>How the last wager went: +stake won, -stake lost, 0 nothing to tell (the town shows it once).</summary>
        public static int LastWagerResult;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Bathed = false; Wager = 0; LastWagerResult = 0; }

        /// <summary>The arena: a gladiator fresh from the baths has a little more health and speed.</summary>
        public static void ApplyArena(Fighter f)
        {
            if (!Bathed || f == null) return;
            f.Health.SetMax(f.Health.Max * BathHealth, true);
            f.MoveSpeed *= BathSpeed;
        }

        /// <summary>The fight is over: the wager is paid or lost, the bath's bonus is spent.</summary>
        public static void SettleArena(PlayerProfile p, bool won)
        {
            if (Wager > 0)
            {
                if (won && p != null) p.AddDenarii(Wager * 2);
                LastWagerResult = won ? Wager : -Wager;
                Wager = 0;
            }
            Bathed = false;
        }
    }
}
