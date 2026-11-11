using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The town watch. Hitting a townsperson (or a guard) or walking into a house that is not yours is an offence;
    /// the nearest guard who sees it (a guard with a body within <see cref="SightRange"/>) comes over and warns you,
    /// shaking his head. The third time he draws his gladius and fights you. Guards knock you out, they do not kill
    /// (the player cannot drop below 1 HP in town): at <see cref="KnockoutAt"/> of your health the fight ends and you
    /// wake up somewhere else. Beat a guard (enough damage) and he yields; run far enough and he gives up.
    /// Warnings are forgotten the next day.
    /// </summary>
    public class TownGuards : MonoBehaviour
    {
        public const int MaxWarnings = 3;
        public const float SightRange = 45f;
        public const float KnockoutAt = 0.25f;
        /// <summary>Damage a guard takes before he yields.</summary>
        public const float GuardStamina = 140f;

        public static TownGuards Instance { get; private set; }
        public int Warnings { get; private set; }
        /// <summary>The guard fighting the player, or null.</summary>
        public CitizenNPC Hostile { get; private set; }
        /// <summary>The guard walking over to warn the player, or null.</summary>
        public CitizenNPC Warner { get; private set; }
        public int Offences { get; private set; }
        public string LastOutcome { get; private set; } = "";

        TownBootstrap town;
        Fighter player;
        HouseInterior lastHouse;
        float offenceCooldown, warnUntil, lostTimer, guardDamage, repath;
        bool warned;
        int day = -1;
        readonly System.Collections.Generic.List<Vector3> corners = new System.Collections.Generic.List<Vector3>();

        public void Init(TownBootstrap t)
        {
            Instance = this;
            town = t;
            player = t.Player;
            player.MinHealth = 1f;
            Fighter.AnyHitResolved += OnHit;
        }

        void OnDestroy()
        {
            Fighter.AnyHitResolved -= OnHit;
            if (Instance == this) Instance = null;
        }

        void Say(string s, float seconds = 3.5f)
        {
            if (town != null && town.Hud != null) town.Hud.ShowMessage(s, seconds);
            Debug.Log("[GUARDS] " + s);
        }

        void OnHit(HitResult r)
        {
            if (player == null || r.Attacker != player || r.Target == null) return;
            if (r.Target.GetComponent<GovernorNPC>() != null || r.Target.GetComponent<GovernorGuard>() != null)
            {
                Offence("¡NADIE TOCA AL GOBERNADOR!");
                return;
            }
            var npc = r.Target.GetComponent<CitizenNPC>();
            if (npc == null || npc.Citizen == null) return;
            if (npc == Hostile) { guardDamage += r.Damage; return; }
            bool guard = npc.Citizen.Job == Job.Guardia;
            Offence(guard ? "¡NO SE GOLPEA A UN GUARDIA!" : "¡NO GOLPEES A LA GENTE!");
        }

        /// <summary>Something the watch does not like happened (public for tests).</summary>
        /// <summary>Tests: the watch looks the other way.</summary>
        [System.NonSerialized] public bool Suspended;

        public void Offence(string why)
        {
            if (Suspended) return;
            if (Hostile != null || Time.time < offenceCooldown) return; // a flurry of blows is one offence
            var g = NearestGuard(player.transform.position, SightRange);
            if (g == null) { Debug.Log("[GUARDS] nobody saw: " + why); return; }
            offenceCooldown = Time.time + 3f;
            Offences++;
            Warnings++;
            string who = GuardName(g);
            if (Warnings >= MaxWarnings) { StartFight(g, who + ": ¡TE LO ADVERTI! ¡EN GUARDIA!"); return; }
            ReleaseWarner();
            Warner = g;
            g.Controlled = true;
            warned = false;
            warnUntil = Time.time + 12f;
            repath = 0f;
            Say(who + ": " + why + "  (AVISO " + Warnings + " DE " + (MaxWarnings - 1) + ")", 4.5f);
        }

        static string GuardName(CitizenNPC g)
        {
            var parts = g.Citizen.Name.Split(' ');
            return "GUARDIA " + parts[parts.Length - 1].ToUpperInvariant();
        }

        CitizenNPC NearestGuard(Vector3 p, float range)
        {
            var sp = CitizenSpawner.Instance;
            if (sp == null) return null;
            CitizenNPC best = null; float bd = range;
            foreach (var n in sp.Active)
            {
                if (n.Citizen == null || n.Citizen.Job != Job.Guardia || !n.Fighter.IsAlive || !n.gameObject.activeInHierarchy) continue;
                float d = Flat(n.transform.position - p);
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        void StartFight(CitizenNPC g, string shout)
        {
            ReleaseWarner();
            Hostile = g;
            g.Controlled = true;
            var f = g.Fighter;
            f.Harmless = false;
            f.DamageMultiplier = 0.6f;
            f.Health.SetMax(1000000f, true);
            if (f.InActivity) f.StopActivity();
            var ai = g.GetComponent<EnemyAI>();
            if (ai == null) ai = g.gameObject.AddComponent<EnemyAI>();
            ai.TauntChance = 0.1f;
            ai.enabled = true;
            guardDamage = 0f;
            lostTimer = 0f;
            Say(shout, 3.5f);
        }

        void EndFight(string outcome, string message)
        {
            var g = Hostile;
            Hostile = null;
            LastOutcome = outcome;
            if (g != null)
            {
                var ai = g.GetComponent<EnemyAI>();
                if (ai != null) Destroy(ai);
                var f = g.Fighter;
                f.Harmless = true;
                f.DamageMultiplier = 1f;
                f.SetBlock(false);
                f.Move(Vector3.zero);
                f.Health.SetMax(1000000f, true);
                g.Controlled = false;
                if (outcome == "yield") g.Flee(player.transform.position, 6f);
            }
            Say(message, 5f);
        }

        void ReleaseWarner()
        {
            if (Warner != null) { Warner.Controlled = false; Warner.Fighter.Move(Vector3.zero); }
            Warner = null;
        }

        void Update()
        {
            if (town == null || player == null) return;
            var sim = TownSimulation.Instance;
            if (sim != null && sim.Day != day) { if (day >= 0) Warnings = 0; day = sim.Day; } // a new day, a clean slate

            // walking into a house that is not yours
            var h = town.HouseAt(player.transform.position);
            if (h != lastHouse)
            {
                lastHouse = h;
                if (h != null && !h.Public && !h.Lot.ForSale && !HouseDeeds.Owns(town.Layout.Seed, h.Lot.Id)) Offence("¡ESA CASA NO ES TUYA, FUERA!");
            }

            if (Warner != null) UpdateWarner();
            if (Hostile != null) UpdateFight();
        }

        void UpdateWarner()
        {
            var g = Warner;
            if (g == null || g.Citizen == null || !g.gameObject.activeInHierarchy || Time.time > warnUntil) { ReleaseWarner(); return; }
            var f = g.Fighter;
            Vector3 to = player.transform.position - g.transform.position; to.y = 0f;
            if (to.magnitude > 2.4f && !warned)
            {
                // come over along the streets
                if ((repath -= Time.deltaTime) <= 0f) { TownNavMesh.Path(g.transform.position, player.transform.position, corners); repath = 0.8f; }
                Vector3 step = to;
                while (corners.Count > 0 && Flat(corners[0] - g.transform.position) < 0.5f) corners.RemoveAt(0);
                if (corners.Count > 0) { step = corners[0] - g.transform.position; step.y = 0f; }
                f.Move(step.normalized * 0.8f);
                f.SetFacing(step);
                return;
            }
            f.Move(Vector3.zero);
            f.SetFacing(to);
            if (!warned && f.State == FighterState.Idle && f.TryTaunt(5)) { warned = true; warnUntil = Mathf.Min(warnUntil, Time.time + 3.5f); }
        }

        void UpdateFight()
        {
            var g = Hostile;
            if (g == null || g.Citizen == null || !g.gameObject.activeInHierarchy) { EndFight("lost", ""); return; }
            if (player.Health.Normalized <= KnockoutAt) { KnockOut(); return; }
            if (guardDamage >= GuardStamina) { Warnings = 0; EndFight("yield", GuardName(g) + ": ¡BASTA! ¡ME RINDO!"); return; }
            float d = Flat(g.transform.position - player.transform.position);
            lostTimer = d > 30f ? lostTimer + Time.deltaTime : 0f;
            if (lostTimer > 8f) EndFight("escaped", "LE DISTE ESQUINAZO AL GUARDIA");
        }

        /// <summary>The guards beat you: you wake up elsewhere with some health back, the warnings forgotten.</summary>
        void KnockOut()
        {
            Warnings = 0;
            EndFight("knockout", "");
            Vector3 at;
            string where = town.WakeUpAfterBeating(out at);
            Say("TE DEJARON INCONSCIENTE... despiertas " + where, 6f);
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
