using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// A townsperson who carries a knife (some of the grown men and women; never children or elders) and was hit by
    /// the player: instead of running, he draws it and defends himself until a guard arrives, the player backs off,
    /// or a few seconds pass. His blows are weak; in town the player cannot die.
    /// </summary>
    public class CitizenDefender : MonoBehaviour
    {
        public const float MaxSeconds = 18f, GuardNear = 4.5f, GiveUpDistance = 14f;
        /// <summary>How many are defending themselves right now (tests).</summary>
        public static int Active { get; private set; }
        public static string LastOutcome { get; private set; } = "";

        CitizenNPC npc;
        Fighter player;
        EnemyAI ai;
        float until;
        bool running;

        /// <summary>Does this citizen carry a knife? Decided by who they are: always the same people.</summary>
        public static bool CarriesKnife(CitizenData c)
        {
            if (c == null || c.Job == Job.Guardia || c.Job == Job.Nino || c.Age < 16 || c.Age > 60) return false;
            return (c.Id * 7 + 3) % 5 < 2;
        }

        public static CitizenDefender Begin(CitizenNPC n, Fighter attacker)
        {
            var d = n.GetComponent<CitizenDefender>();
            if (d == null) d = n.gameObject.AddComponent<CitizenDefender>();
            if (d.running) { d.until = Time.time + MaxSeconds; return d; }
            d.npc = n; d.player = attacker;
            d.running = true;
            d.until = Time.time + MaxSeconds;
            Active++;
            n.Controlled = true;
            var f = n.Fighter;
            if (f.InActivity) f.StopActivity();
            if (f.CurrentWeapon == null) f.Equip(WeaponFactory.Create(WeaponLibrary.Dagger));
            f.Harmless = false;
            f.DamageMultiplier = 0.45f;
            d.ai = n.GetComponent<EnemyAI>();
            if (d.ai == null) d.ai = n.gameObject.AddComponent<EnemyAI>();
            d.ai.TauntChance = 0f;
            d.ai.enabled = true;
            var town = TownBootstrap.Instance;
            if (town != null && town.Hud != null)
                town.Hud.ShowMessage(n.Citizen.Name.ToUpperInvariant() + " SACA UN CUCHILLO: ¡SE DEFIENDE!", 3.5f);
            Debug.Log("[TOWN] " + n.Citizen.Name + " defends with a knife");
            return d;
        }

        void Update()
        {
            if (!running) return;
            if (npc == null || npc.Citizen == null || player == null || !npc.Fighter.IsAlive) { End("gone", ""); return; }
            var guards = TownGuards.Instance;
            // the watch is here: it is their business now
            if (guards != null && guards.Hostile != null) { End("guard", "¡LA GUARDIA SE OCUPA!"); return; }
            var sp = CitizenSpawner.Instance;
            if (sp != null)
                foreach (var g in sp.Active)
                    if (g != npc && g.Citizen != null && g.Citizen.Job == Job.Guardia && g.gameObject.activeInHierarchy &&
                        (Flat(g.transform.position - transform.position) < GuardNear || Flat(g.transform.position - player.transform.position) < GuardNear))
                    { End("guard", "LLEGA LA GUARDIA: " + npc.Citizen.Name.ToUpperInvariant() + " GUARDA EL CUCHILLO"); return; }
            if (Flat(player.transform.position - transform.position) > GiveUpDistance) { End("away", ""); return; }
            if (player.Health.Normalized <= 0.3f) { End("lesson", npc.Citizen.Name.ToUpperInvariant() + ": ¡QUE TE SIRVA DE LECCION!"); return; }
            if (Time.time > until) { End("time", ""); return; }
        }

        public void End(string outcome, string message)
        {
            if (!running) return;
            running = false;
            Active = Mathf.Max(0, Active - 1);
            LastOutcome = outcome;
            if (ai != null) Destroy(ai);
            ai = null;
            if (npc != null)
            {
                var f = npc.Fighter;
                f.Harmless = true;
                f.DamageMultiplier = 1f;
                f.SetBlock(false);
                f.Move(Vector3.zero);
                f.Health.SetMax(1000000f, true);
                npc.Controlled = false;
                if (npc.Citizen != null && player != null && outcome != "guard") npc.Flee(player.transform.position, 4f);
            }
            var town = TownBootstrap.Instance;
            if (!string.IsNullOrEmpty(message) && town != null && town.Hud != null) town.Hud.ShowMessage(message, 4f);
            Debug.Log("[TOWN] knife defence over: " + outcome);
        }

        void OnDisable() { if (running) End("gone", ""); }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
