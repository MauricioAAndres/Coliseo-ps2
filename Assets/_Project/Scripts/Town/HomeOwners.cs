using System.Collections.Generic;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// Sleeping in somebody else's bed. The people of a house are data while they are indoors; when one of them is at
    /// home (comes back at their hour, or was in already) and finds the player asleep in a bed of theirs, they get a
    /// body next to the bed and wake the player up. From then the player has <see cref="Grace"/> seconds to be out of
    /// the house: after that the owner draws a knife and attacks, and the watch turns up in the house to fight too.
    /// Someone coming home while the player is just lying on the bed (awake) does the same.
    /// </summary>
    public class HomeOwners : MonoBehaviour
    {
        public const float Grace = 15f;
        public static HomeOwners Instance { get; private set; }

        /// <summary>The owner facing the player right now, or null.</summary>
        public CitizenNPC Owner { get; private set; }
        public HouseInterior House { get; private set; }
        /// <summary>Seconds left to get out before the owner attacks.</summary>
        public float TimeLeft { get; private set; }
        public bool Attacking { get; private set; }
        /// <summary>"" (nothing yet), "left" (the player went out in time), "attack" (the owner attacked).</summary>
        public string LastOutcome { get; private set; } = "";
        public int Confrontations { get; private set; }
        /// <summary>Guards that came to the house the last time (tests).</summary>
        public int GuardsCalled { get; private set; }

        TownBootstrap town;
        TownManager manager;
        Fighter player;
        HouseInterior watched;
        readonly HashSet<int> homeNow = new HashSet<int>();
        bool leaving;
        float leaveTimer, afterFight, lastShout;

        public void Init(TownBootstrap t, TownManager m)
        {
            Instance = this;
            town = t;
            manager = m;
            player = t.Player;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Say(string s, float seconds = 4f)
        {
            if (town != null && town.Hud != null) town.Hud.ShowMessage(s, seconds);
            Debug.Log("[HOME] " + s);
        }

        /// <summary>The people who live in a house (both storeys of a lot are one home).</summary>
        public static List<CitizenData> Residents(HouseInterior h)
        {
            var list = new List<CitizenData>();
            var sim = TownSimulation.Instance;
            if (sim == null || h == null || h.Lot == null) return list;
            foreach (var c in sim.Citizens)
            {
                var home = sim.Place(c.HomeId);
                if (home != null && home.LotId == h.Lot.Id) list.Add(c);
            }
            return list;
        }

        /// <summary>Indoors at home at this hour (asleep or not).</summary>
        public static bool AtHome(CitizenData c, float hour)
        {
            var sim = TownSimulation.Instance;
            if (sim == null || c == null) return false;
            var loc = sim.WhereIs(c, hour);
            return loc.PlaceId == c.HomeId && !loc.Walking && loc.Indoors;
        }

        /// <summary>A private home that is not the player's (nor empty and for sale, nor another player's).</summary>
        public bool Foreign(HouseInterior h)
        {
            if (h == null || h.Lot == null || h.Public || h.CellSpots.Count > 0 || h.BedSpots.Count > 0) return false;
            if (h.AltarSpot.HasValue || h.ThroneSpot.HasValue) return false;
            if (h.Lot.ForSale || HouseDeeds.Owns(town.Layout.Seed, h.Lot.Id) || town.OtherOwners.ContainsKey(h.Lot.Id)) return false;
            return true;
        }

        // who answers the door: a grown man or woman first, an elder next, a child last
        static int Rank(CitizenData c) => c.Age >= 16 && c.Age <= 60 ? (c.Female ? 1 : 0) : c.Age > 60 ? 2 : 3;

        void Update()
        {
            if (town == null || player == null) return;
            var sim = TownSimulation.Instance;
            if (sim == null) return;
            if (Owner != null) { UpdateConfrontation(); return; }
            if (TownGuards.Instance != null && TownGuards.Instance.Suspended) return; // tests: nobody minds

            var h = town.HouseAt(player.transform.position);
            if (!Foreign(h)) { watched = null; return; }
            bool fresh = h != watched;
            if (fresh) { watched = h; homeNow.Clear(); }
            bool lying = player.InActivity && player.ActivityKind == Fighter.ActLie && player.OnFurniture;
            bool asleep = manager != null && manager.Sleeping;

            CitizenData arrived = null, home = null;
            foreach (var c in Residents(h))
            {
                bool at = AtHome(c, sim.Hour);
                if (at)
                {
                    if (home == null || Rank(c) < Rank(home)) home = c;
                    if (homeNow.Add(c.Id) && !fresh && (arrived == null || Rank(c) < Rank(arrived))) arrived = c;
                }
                else homeNow.Remove(c.Id);
            }
            // asleep with somebody at home (just back, or in all along), or caught lying on the bed by who comes in
            if (asleep && home != null) Confront(h, arrived ?? home);
            else if (lying && arrived != null) Confront(h, arrived);
        }

        /// <summary>The owner appears by the player and throws them out (public for tests).</summary>
        public bool Confront(HouseInterior h, CitizenData c)
        {
            var sp = CitizenSpawner.Instance;
            if (sp == null || h == null || c == null || Owner != null) return false;
            if (manager != null && manager.Sleeping) manager.EndSleep(); // woken up
            Vector3 pp = player.transform.position;
            var spots = Spots(h, pp, 1, 1.0f, true);
            Vector3 at = spots.Count > 0 ? spots[0] : DoorSpot(h);
            Vector3 look = pp - at; look.y = 0f;
            var body = sp.Summon(c, at, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : h.transform.rotation);
            if (body == null) return false;
            Owner = body;
            House = h;
            TimeLeft = Grace;
            Attacking = false;
            leaving = false;
            lastShout = Grace;
            Confrontations++;
            LastOutcome = "";
            body.Fighter.SetFacing(look);
            body.Fighter.TryTaunt(5); // shaking the head: no
            var sim = TownSimulation.Instance;
            if (sim != null) sim.ChangeRelation(c, -10);
            Say(Short(c) + ": ¡¿QUE HACES EN MI CASA?! ¡FUERA!  (" + Mathf.RoundToInt(Grace) + " s)", 5f);
            Gladiator.Audio.AudioManager.UI("ui_back");
            return true;
        }

        static string Short(CitizenData c) => c.Name.Split(' ')[0].ToUpperInvariant();

        bool PlayerInside()
        {
            var at = town.HouseAt(player.transform.position);
            return at != null && House != null && at.Lot.Id == House.Lot.Id;
        }

        void UpdateConfrontation()
        {
            if (Owner.Citizen == null || !Owner.gameObject.activeInHierarchy) { Clear(); return; }
            var f = Owner.Fighter;
            Vector3 to = player.transform.position - Owner.transform.position; to.y = 0f;
            bool inside = PlayerInside();
            if (Attacking)
            {
                // the knife is out: it lasts until the owner is done (the player ran, learnt the lesson, or time)
                var def = Owner.GetComponent<CitizenDefender>();
                if (def != null && def.Running) { afterFight = 0f; return; }
                if ((afterFight += Time.deltaTime) > 6f || to.magnitude > 12f) Dismiss();
                return;
            }
            f.Move(Vector3.zero);
            f.SetFacing(to);
            if (leaving)
            {
                // the player went out: the owner stays at home (gone once nobody is looking, or after a while)
                if (inside) { leaving = false; Say(Short(Owner.Citizen) + ": ¡¿OTRA VEZ?! ¡FUERA!", 3f); return; }
                if ((leaveTimer += Time.deltaTime) > 20f || to.magnitude > 9f) Dismiss();
                return;
            }
            if (!inside)
            {
                leaving = true;
                leaveTimer = 0f;
                LastOutcome = "left";
                Say(Short(Owner.Citizen) + ": ¡Y NO VUELVAS!", 3f);
                return;
            }
            TimeLeft -= Time.deltaTime;
            if (TimeLeft <= 0f) { Attack(); return; }
            // a reminder every five seconds
            if (lastShout - TimeLeft >= 5f)
            {
                lastShout = TimeLeft;
                Say(Short(Owner.Citizen) + ": ¡FUERA DE MI CASA!  (" + Mathf.CeilToInt(TimeLeft) + " s)", 2.5f);
                if (f.State == FighterState.Idle) f.TryTaunt(5);
            }
        }

        /// <summary>Time is up: the owner attacks and the watch comes into the house.</summary>
        void Attack()
        {
            Attacking = true;
            LastOutcome = "attack";
            afterFight = 0f;
            if (player.InActivity) player.StopActivity(); // nobody fights lying down
            var def = CitizenDefender.Begin(Owner, player);
            def.HomeDefence = true;
            GuardsCalled = 0;
            var guards = TownGuards.Instance;
            if (guards != null) GuardsCalled = guards.CallToHouse(House, Spots(House, player.transform.position, 2, 1.6f, false));
            Say(Short(Owner.Citizen) + ": ¡TE LO ADVERTI!  ¡GUARDIAS!", 4.5f);
        }

        void Dismiss()
        {
            var sp = CitizenSpawner.Instance;
            if (sp != null && Owner != null && Owner.Citizen != null)
            {
                var def = Owner.GetComponent<CitizenDefender>();
                if (def != null && def.Running) def.End("gone", "");
                sp.Dismiss(Owner);
            }
            Clear();
        }

        void Clear()
        {
            Owner = null;
            House = null;
            Attacking = false;
            leaving = false;
            watched = null;
            homeNow.Clear();
        }

        // ------------------------------------------------------------------ where a body fits
        static float FloorY(HouseInterior h) => h.transform.position.y + (h.Floor == 0 ? 0.05f : 0.19f);

        static bool Free(Vector3 p)
        {
            int mask = ~((1 << Ragdoll.Layer) | (1 << Ragdoll.FighterLayer));
            return !Physics.CheckCapsule(p + Vector3.up * 0.62f, p + Vector3.up * 1.5f, 0.3f, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Just inside the door.</summary>
        public static Vector3 DoorSpot(HouseInterior h)
        {
            Vector3 dc = h.Door != null ? h.Door.Center : h.transform.position;
            Vector3 inward = h.transform.position - dc; inward.y = 0f;
            inward = inward.sqrMagnitude > 0.01f ? inward.normalized : -h.transform.forward;
            Vector3 p = dc + inward * 1.2f;
            p.y = FloorY(h);
            return p;
        }

        /// <summary>
        /// Free places to stand in the room the point is in, at least <paramref name="minDist"/> from it and apart from
        /// each other: the nearest to the point first (the owner, by the bed), or the nearest to the door (the watch
        /// rushing in). Empty when the room has none.
        /// </summary>
        public static List<Vector3> Spots(HouseInterior h, Vector3 around, int count, float minDist, bool nearest)
        {
            var found = new List<Vector3>();
            if (h == null) return found;
            Vector3 l = h.transform.InverseTransformPoint(around);
            HouseInterior.Room? room = null;
            foreach (var r in h.Rooms) if (l.x >= r.X0 && l.x <= r.X1 && l.z >= r.Z0 && l.z <= r.Z1) { room = r; break; }
            if (!room.HasValue) return found;
            var rm = room.Value;
            Vector3 door = h.Door != null ? h.Door.Center : h.transform.position;
            float fy = FloorY(h);
            var cand = new List<(Vector3 p, float score)>();
            for (float x = rm.X0 + 0.42f; x <= rm.X1 - 0.42f; x += 0.4f)
                for (float z = rm.Z0 + 0.42f; z <= rm.Z1 - 0.42f; z += 0.4f)
                {
                    Vector3 w = h.transform.TransformPoint(new Vector3(x, 0f, z));
                    w.y = fy;
                    Vector3 d = w - around; d.y = 0f;
                    if (d.magnitude < minDist || !Free(w)) continue;
                    Vector3 dd = w - door; dd.y = 0f;
                    cand.Add((w, nearest ? d.magnitude : dd.magnitude + Mathf.Max(0f, d.magnitude - 3.5f)));
                }
            cand.Sort((a, b) => a.score.CompareTo(b.score));
            foreach (var c in cand)
            {
                bool apart = true;
                foreach (var f in found) if ((f - c.p).magnitude < 0.9f) { apart = false; break; }
                if (!apart) continue;
                found.Add(c.p);
                if (found.Count >= count) break;
            }
            return found;
        }
    }
}
