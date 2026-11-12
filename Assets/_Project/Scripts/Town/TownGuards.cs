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
        /// <summary>The fine was paid at the basilica: a clean slate.</summary>
        public void ClearWarnings() { Warnings = 0; ReleaseWarner(); }
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
            if (npc == Hostile || backups.Contains(npc)) { guardDamage += r.Damage; return; }
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

        /// <summary>More guards fighting next to <see cref="Hostile"/> (called to a house).</summary>
        readonly System.Collections.Generic.List<CitizenNPC> backups = new System.Collections.Generic.List<CitizenNPC>();
        public int Fighting => (Hostile != null ? 1 : 0) + backups.Count;

        // a guard goes for the player: gladius out, no longer harmless
        static void Rouse(CitizenNPC g)
        {
            g.Controlled = true;
            var f = g.Fighter;
            f.Harmless = false;
            f.DamageMultiplier = 0.6f;
            f.Health.SetMax(1000000f, true);
            if (f.InActivity) f.StopActivity();
            if (!f.SetSheathed(false, false, Fighter.QuickDrawSeconds) && f.Sheathed) f.SetSheathed(false, true);
            var ai = g.GetComponent<EnemyAI>();
            if (ai == null) ai = g.gameObject.AddComponent<EnemyAI>();
            ai.TauntChance = 0.1f;
            ai.enabled = true;
        }

        // ...and stands down: the gladius back in the belt
        static void Calm(CitizenNPC g)
        {
            if (g == null) return;
            var ai = g.GetComponent<EnemyAI>();
            if (ai != null) Destroy(ai);
            var f = g.Fighter;
            f.Harmless = true;
            f.DamageMultiplier = 1f;
            f.SetBlock(false);
            f.Move(Vector3.zero);
            f.Health.SetMax(1000000f, true);
            if (!f.SetSheathed(true) && !f.Sheathed) f.SetSheathed(true, true);
            g.Controlled = false;
        }

        void StartFight(CitizenNPC g, string shout)
        {
            ReleaseWarner();
            Hostile = g;
            Rouse(g);
            guardDamage = 0f;
            lostTimer = 0f;
            Say(shout, 3.5f);
        }

        /// <summary>
        /// The watch is called into a house (its owner found the player inside and the player would not leave): up to
        /// two guards turn up at the given places, swords drawn, no warnings. Returns how many came.
        /// </summary>
        public int CallToHouse(HouseInterior h, System.Collections.Generic.List<Vector3> spots)
        {
            var sp = CitizenSpawner.Instance;
            var sim = TownSimulation.Instance;
            if (sp == null || sim == null || h == null || player == null) return 0;
            if (spots == null) spots = new System.Collections.Generic.List<Vector3>();
            Vector3 door = HomeOwners.DoorSpot(h);
            if (spots.Count == 0) spots.Add(door);
            if (spots.Count == 1) spots.Add(Vector3.Lerp(spots[0], door, 0.5f) + h.transform.right * 0.6f);
            // the guards with a body nearest the house first, then the rest of the watch
            var watch = sim.Citizens.FindAll(c => c.Job == Job.Guardia);
            float Dist(CitizenData c)
            {
                foreach (var n in sp.Active) if (n.Citizen == c) return Flat(n.transform.position - door);
                return 1000f + c.Id;
            }
            watch.Sort((a, b) => Dist(a).CompareTo(Dist(b)));
            int came = 0;
            foreach (var c in watch)
            {
                if (came >= 2) break;
                if (Hostile != null && Hostile.Citizen == c) continue;
                Vector3 at = spots[came];
                Vector3 look = player.transform.position - at; look.y = 0f;
                var g = sp.Summon(c, at, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : h.transform.rotation);
                if (g == null) continue;
                if (Hostile == null) StartFight(g, GuardName(g) + ": ¡ALTO, LADRON! ¡EN GUARDIA!");
                else { Rouse(g); backups.Add(g); }
                came++;
            }
            Warnings = MaxWarnings;
            Debug.Log("[GUARDS] called to house " + h.Lot.Id + ": " + came + " came");
            return came;
        }

        void EndFight(string outcome, string message)
        {
            var g = Hostile;
            Hostile = null;
            LastOutcome = outcome;
            if (g != null)
            {
                Calm(g);
                if (outcome == "yield") g.Flee(player.transform.position, 6f);
            }
            foreach (var b in backups)
            {
                if (b == null) continue;
                Calm(b);
                if (outcome == "yield") b.Flee(player.transform.position, 6f);
            }
            backups.Clear();
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
            Vector3 from = Hostile != null ? player.transform.position - Hostile.transform.position : -player.transform.forward;
            EndFight("knockout", "");
            if (player.KnockDown(from, 30f))
            {
                // out cold on the street for a moment, then the bed
                Say("TE DEJARON INCONSCIENTE...", OutSeconds);
                OutCold = true;
                StartCoroutine(ComeRound());
            }
            else Wake();
        }

        /// <summary>Game hours that go by before you wake up in the infirmary.</summary>
        public const float HoursOut = 24f;
        /// <summary>How long you lie on the street before waking up elsewhere.</summary>
        public const float OutSeconds = 2.8f;
        /// <summary>Lying knocked out right now (tests).</summary>
        public bool OutCold { get; private set; }

        System.Collections.IEnumerator ComeRound()
        {
            yield return new WaitForSeconds(OutSeconds);
            Wake();
        }

        void Wake()
        {
            OutCold = false;
            if (player == null) return;
            player.WakeUp(0f);
            Vector3 at;
            string where = town.WakeUpAfterBeating(out at);
            // a whole day goes by before you come round (online the clock is the host's)
            var sim = TownSimulation.Instance;
            var net = Gladiator.Net.NetSession.Instance;
            bool guest = net != null && net.InGame && !net.IsHost;
            bool dayLost = sim != null && !guest;
            if (dayLost) sim.AdvanceHours(HoursOut);
            Say("TE DEJARON INCONSCIENTE... despiertas " + where + (dayLost ? ", UN DIA DESPUES" : ""), 6f);
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
