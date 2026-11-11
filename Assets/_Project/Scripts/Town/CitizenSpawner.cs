using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Character;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// "LOD" of the simulation: every quarter of a second, the citizens who are outdoors within <see cref="Radius"/> of
    /// the player get a body (the same humanoid as the gladiators); the ones who walk out of range or go indoors give it
    /// back and are data again. Bodies come from a pool of <see cref="PoolSize"/> reusable Fighters: no Instantiate /
    /// Destroy while playing, and a body that comes back to the same person is not rebuilt.
    /// </summary>
    public class CitizenSpawner : MonoBehaviour
    {
        public const int PoolSize = 18;
        public const int CitizenTeam = 7;
        public float Radius = 40f;
        public float ReleaseRadius = 48f;

        TownSimulation sim;
        Fighter player;
        readonly List<CitizenNPC> pool = new List<CitizenNPC>();
        readonly Dictionary<int, CitizenNPC> active = new Dictionary<int, CitizenNPC>();
        readonly List<CitizenData> wanted = new List<CitizenData>();
        float tick, bumpCooldown;

        public static CitizenSpawner Instance { get; private set; }
        public int ActiveCount => active.Count;
        public int PoolCreated => pool.Count;
        public IEnumerable<CitizenNPC> Active => active.Values;
        /// <summary>Bodies built so far (a body reused for the same citizen is not rebuilt).</summary>
        public int Rebuilds { get; private set; }

        public void Init(Fighter playerFighter)
        {
            Instance = this;
            sim = TownSimulation.Ensure();
            player = playerFighter;
            Fighter.AnyHitResolved += OnHit;
        }

        void OnDestroy()
        {
            Fighter.AnyHitResolved -= OnHit;
            if (hooked != null) hooked.GameMessage -= OnNet;
            if (Instance == this) Instance = null;
        }

        Gladiator.Net.NetSession hooked;

        void OnNet(int from, string type, string payload)
        {
            if (type != "CH") return;
            var a = payload.Split('|');
            if (a.Length < 3 || !int.TryParse(a[0], out int id)) return;
            if (active.TryGetValue(id, out var npc))
                npc.Flee(new Vector3(Gladiator.Net.NetSession.PF(a[1]), 0f, Gladiator.Net.NetSession.PF(a[2])), 7f);
        }

        void Update()
        {
            var net = Gladiator.Net.NetSession.Instance;
            if (net != hooked) { if (hooked != null) hooked.GameMessage -= OnNet; hooked = net; if (net != null) net.GameMessage += OnNet; }
            if (sim == null || player == null) return;
            if ((tick -= Time.deltaTime) > 0f) return;
            tick = 0.25f;
            Vector3 p = player.transform.position;
            float hour = sim.Hour;

            // 1) let go of the bodies that are too far, or whose owner went indoors and reached the door
            var release = new List<int>();
            foreach (var kv in active)
            {
                var npc = kv.Value;
                float d = Flat(npc.transform.position - p);
                // going indoors: through the door, or simply gone once out of close view (or after a while)
                bool homeBound = npc.GoingIn && (d > 26f || npc.GoingInSeconds > 40f);
                if (npc.Controlled) continue; // a guard dealing with the player stays
                if (d > ReleaseRadius || npc.Finished || homeBound) release.Add(kv.Key);
            }
            foreach (int id in release) Release(id);

            // 2) who should have a body: outdoors and near, the closest first
            wanted.Clear();
            wanted.AddRange(sim.Near(p, Radius, hour));
            wanted.Sort((a, b) => Flat(sim.WhereIs(a, hour).Position - p).CompareTo(Flat(sim.WhereIs(b, hour).Position - p)));
            int spawned = 0;
            foreach (var c in wanted)
            {
                if (active.ContainsKey(c.Id)) continue;
                if (spawned >= 2) break;                    // a couple per tick: no hitches
                var body = Take(c);
                if (body == null) break;                     // pool exhausted (the nearest 18 are already out)
                var loc = sim.WhereIs(c, hour);
                if (!TownNavMesh.Snap(loc.Position, out Vector3 at, 4f)) { Give(body); continue; }
                body.Assign(c, at, sim);
                active[c.Id] = body;
                spawned++;
            }
            // 3) a much nearer citizen takes the farthest body when the pool is full (with a margin, once a second: no flicker)
            bumpCooldown -= 0.25f;
            if (wanted.Count > 0 && active.Count >= PoolSize && bumpCooldown <= 0f)
            {
                var nearest = wanted[0];
                if (!active.ContainsKey(nearest.Id))
                {
                    int far = -1; float farD = 0f;
                    foreach (var kv in active) { float d = Flat(kv.Value.transform.position - p); if (d > farD) { farD = d; far = kv.Key; } }
                    float nd = Flat(sim.WhereIs(nearest, hour).Position - p);
                    if (far >= 0 && nd < 20f && farD > 30f && farD > nd + 12f) { Release(far); bumpCooldown = 1f; }
                }
            }
        }

        CitizenNPC Take(CitizenData c)
        {
            // the body that last was this person (no rebuild), else any free one, else a new one while the pool grows
            CitizenNPC free = null;
            foreach (var n in pool)
            {
                if (n.gameObject.activeSelf) continue;
                if (n.LastCitizenId == c.Id) { n.gameObject.SetActive(true); return n; } // same person: no rebuild
                if (free == null) free = n;
            }
            if (free != null) { Rebuild(free, c); return free; }
            if (pool.Count >= PoolSize) return null;
            var f = CharacterFactory.Create(c.Name, Look(c), new Vector3(0, -50f, 0), Quaternion.identity, CitizenTeam, ArmorFor(c));
            Arm(f, c);
            f.Harmless = true;
            f.DisarmResistance = 1f;
            f.Health.SetMax(1000000f, true);
            var npc = f.gameObject.AddComponent<CitizenNPC>();
            npc.transform.SetParent(transform, true);
            npc.LastCitizenId = c.Id;
            Rebuilds++;
            pool.Add(npc);
            return npc;
        }

        void Rebuild(CitizenNPC n, CitizenData c)
        {
            n.gameObject.SetActive(true);
            CharacterFactory.RebuildBody(n.Fighter, Look(c), ArmorFor(c));
            Arm(n.Fighter, c);
            n.Fighter.DisplayName = c.Name;
            n.LastCitizenId = c.Id;
            Rebuilds++;
        }

        void Give(CitizenNPC n) { n.gameObject.SetActive(false); }

        void Release(int id)
        {
            if (!active.TryGetValue(id, out var n)) return;
            active.Remove(id);
            n.Clear();
            n.gameObject.SetActive(false);
        }

        public static readonly Color GuardRed = new Color(0.72f, 0.1f, 0.08f);

        /// <summary>The town guard's kit: bronze galea, lorica, iron shoulders, greaves (a Roman legionary).</summary>
        public static ArmorSet ArmorFor(CitizenData c) =>
            c.Job == Job.Guardia ? ArmorSet.FromIds(new[] { "galea_bronce", "lorica", "hombreras_hierro", "grebas" }) : ArmorSet.Empty;

        /// <summary>Guards carry a gladius and a big red scutum; everybody else goes empty-handed.</summary>
        public static void Arm(Fighter f, CitizenData c)
        {
            f.UnequipAll();
            // some grown men and women carry a knife (pugio) at the belt hand: they defend themselves with it
            if (CitizenDefender.CarriesKnife(c)) { f.Equip(WeaponFactory.Create(WeaponLibrary.Dagger)); return; }
            if (c.Job != Job.Guardia) return;
            f.Equip(WeaponFactory.Create(WeaponLibrary.Gladius));
            var sc = ShieldLibrary.ById("scutum");
            if (sc != null) Shield.Attach(f, sc, GuardRed);
        }

        /// <summary>A citizen's body from their data: deterministic (same person, same face), dressed as a townsperson.</summary>
        public static CharacterAppearance Look(CitizenData c)
        {
            var rng = new System.Random(c.LookSeed);
            var a = CharacterAppearance.Random(rng, true);
            a.Name = c.Name;
            // complexion: women from 50% to fully feminine, men from fully masculine to 50%
            a.Femininity = c.Female ? Mathf.Lerp(0.5f, 1f, (float)rng.NextDouble()) : Mathf.Lerp(0f, 0.5f, (float)rng.NextDouble());
            if (c.Female) { a.Beard = 0; a.HairStyle = new[] { 1, 2, 3, 5 }[rng.Next(4)]; }
            a.Armor = 0;
            a.Helmet = false;
            a.Outfit = c.Female || c.Age > 60 ? 2 : 1;          // long tunic for women and elders, short for the rest
            a.ClothColor = new[] { 0, 0, 1, 2, 3, 6, 7, 7 }[rng.Next(8)]; // mostly linen and ochre, a few colours
            if (c.Age > 60) { a.HairColor = 4; a.Fat = Mathf.Max(a.Fat, 0.3f); a.Muscle *= 0.6f; }
            if (c.Job == Job.Herrero || c.Job == Job.Guardia) a.Muscle = Mathf.Max(a.Muscle, 0.7f);
            if (c.Job == Job.Guardia) { a.Outfit = 1; a.ClothColor = 1; a.ClothOverride = GuardRed; a.Shirt = 1; a.Pants = 0; a.HairStyle = Mathf.Min(a.HairStyle, 2); }
            if (c.Job == Job.Sacerdote) { a.Outfit = 2; a.ClothColor = 7; }
            return a;
        }

        // ------------------------------------------------------------------ hit a townsperson: they run, the town remembers
        void OnHit(HitResult r)
        {
            if (r.Attacker != player || r.Target == null) return;
            var npc = r.Target.GetComponent<CitizenNPC>();
            if (npc == null || npc.Citizen == null) return;
            sim.ChangeRelation(npc.Citizen, -15);
            if (npc.Controlled || npc.Citizen.Job == Job.Guardia) return; // guards do not run: TownGuards deals with it
            bool defends = CitizenDefender.CarriesKnife(npc.Citizen);
            if (defends) CitizenDefender.Begin(npc, player); // knife out, until a guard comes
            else npc.Flee(player.transform.position, 7f);
            var net = Gladiator.Net.NetSession.Instance;
            if (net != null && net.InGame && !defends)
                net.Send("CH", npc.Citizen.Id + "|" + Gladiator.Net.NetSession.F(player.transform.position.x) + "|" + Gladiator.Net.NetSession.F(player.transform.position.z));
            foreach (var other in active.Values)
                if (other != npc && !other.Controlled && other.Citizen.Job != Job.Guardia && Flat(other.transform.position - npc.transform.position) < 9f)
                {
                    sim.ChangeRelation(other.Citizen, -4);
                    other.Flee(player.transform.position, 5f);
                }
        }

        /// <summary>The townsperson closest to a point (for talking), within a range.</summary>
        public CitizenNPC Nearest(Vector3 p, float range)
        {
            CitizenNPC best = null; float bd = range;
            foreach (var n in active.Values) { float d = Flat(n.transform.position - p); if (d < bd) { bd = d; best = n; } }
            return best;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
