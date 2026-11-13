using System.Collections.Generic;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The townsfolk are data while they are indoors. This gives them a body in the one building the player is in (or
    /// at the door of, or a tavern he is walking up to), doing what the simulation says they do there: asleep in their
    /// beds, sitting at the kitchen table for breakfast and supper, at a table of the tavern, in the pool of the
    /// baths, kneeling at the altar, or on their feet at work. They go back to data when the player walks away.
    /// </summary>
    public class IndoorLife : MonoBehaviour
    {
        public static IndoorLife Instance { get; private set; }

        class Guest
        {
            public CitizenData C;
            public CitizenNPC Body;
            public Activity Act;
            public int Furniture = -1;      // index in the house's furniture, or -1
            public int Pool = -1;           // index of the pool seat, or -1
            public int Kind = -1;           // Fighter.Act*, or -1 standing
            public Pose At;
            public Vector3 Stand;
            public bool Seated;
            public float Tries, Turn;
            public Vector3 Face;
        }

        TownBootstrap town;
        Fighter player;
        readonly Dictionary<int, Guest> guests = new Dictionary<int, Guest>();
        readonly HashSet<int> want = new HashSet<int>();
        readonly List<int> gone = new List<int>();
        float tick;

        /// <summary>The building being shown, or null.</summary>
        public HouseInterior Focus { get; private set; }
        public int Count => guests.Count;
        public bool Holds(int citizenId) => guests.ContainsKey(citizenId);

        /// <summary>How many of the people shown are doing an activity (Fighter.Act*, or -1 standing) right now.</summary>
        public int Doing(int kind)
        {
            int n = 0;
            foreach (var g in guests.Values)
            {
                if (g.Body == null) continue;
                var f = g.Body.Fighter;
                if (kind < 0 ? !f.InActivity : f.InActivity && f.ActivityKind == kind) n++;
            }
            return n;
        }

        public void Init(TownBootstrap t)
        {
            Instance = this;
            town = t;
            player = t.Player;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        HouseInterior Pick(Vector3 p)
        {
            HouseInterior h = town.HouseAt(p);
            if (h == null)
            {
                var d = town.NearestDoor(p, 4f);
                if (d != null) h = d.GetComponentInParent<HouseInterior>();
            }
            if (h == null && town.Houses != null)
                foreach (var t in town.Houses)
                    if (t != null && t.Service == "tavern" && Flat(t.transform.position - p) < t.HalfZ + 10f) { h = t; break; }
            if (h == null || h.Lot == null) return null;
            if (h.CellSpots.Count > 0 || h.BedSpots.Count > 0 || h.ThroneSpot.HasValue) return null; // jail, infirmary, governor: their own people
            return h;
        }

        void Update()
        {
            if (town == null || player == null) return;
            var sim = TownSimulation.Instance;
            var sp = CitizenSpawner.Instance;
            if (sim == null || sp == null) return;

            // every frame: the ones on their feet stay put; the ones waiting for their bed or chair get on it
            foreach (var g in guests.Values) Drive(g);

            if ((tick -= Time.deltaTime) > 0f) return;
            tick = 0.25f;
            var h = Pick(player.transform.position);
            if (h != Focus) { DismissAll(); Focus = h; }
            if (Focus == null) return;

            float hour = sim.Hour;
            var owner = HomeOwners.Instance != null ? HomeOwners.Instance.Owner : null;
            want.Clear();
            int added = 0;
            foreach (var c in sim.Citizens)
            {
                var loc = sim.WhereIs(c, hour);
                if (loc.Walking || !loc.Indoors) continue;
                var place = sim.Place(loc.PlaceId);
                if (place == null || place.LotId != Focus.Lot.Id) continue;
                if (Focus.Floor != 0) continue;                                   // the family lives downstairs
                if (c.Job == Job.Tabernero && Focus.Service == "tavern") continue; // the barkeeper is behind the bar already
                if (owner != null && owner.Citizen == c) continue;                // throwing the player out: HomeOwners has him
                want.Add(c.Id);
                if (guests.TryGetValue(c.Id, out var have))
                {
                    if (have.Act != loc.Activity) Remove(c.Id, true);             // something else to do now: seated again next tick
                    continue;
                }
                if (added >= 2) continue;                                         // a couple per tick: no hitches
                if (Add(c, loc.Activity, sp)) added++;
            }
            gone.Clear();
            foreach (var id in guests.Keys) if (!want.Contains(id)) gone.Add(id);
            foreach (int id in gone) Remove(id, true);
        }

        void Drive(Guest g)
        {
            if (g.Body == null || g.Body.Citizen != g.C || !g.Body.gameObject.activeInHierarchy) return;
            var f = g.Body.Fighter;
            if (f.InActivity) { g.Seated = true; return; }
            f.Move(Vector3.zero);
            if (g.Kind >= 0 && !g.Seated)
            {
                // just given a body: on the bed / the chair as soon as it stands on the floor
                g.Tries += Time.deltaTime;
                bool ok = g.Furniture >= 0 || g.Pool >= 0 ? f.TryActivityAt(g.Kind, g.At, g.Stand, true) : f.TryActivity(g.Kind, true);
                if (ok) g.Seated = true;
                else if (g.Tries > 3f) g.Kind = -1; // no way: stays on his feet
                return;
            }
            // on his feet: looks at the player when he is close, else about the room now and then
            Vector3 to = player.transform.position - f.transform.position; to.y = 0f;
            if (to.magnitude < 3.5f && to.sqrMagnitude > 0.01f) f.SetFacing(to);
            else if ((g.Turn -= Time.deltaTime) <= 0f)
            {
                g.Turn = Random.Range(4f, 9f);
                g.Face = Quaternion.Euler(0, Random.Range(-70f, 70f), 0) * (g.Face.sqrMagnitude > 0.01f ? g.Face : f.transform.forward);
                f.SetFacing(g.Face);
            }
        }

        bool FurnitureTaken(int idx)
        {
            foreach (var g in guests.Values) if (g.Furniture == idx) return true;
            return HouseInterior.Occupied(Focus.Furniture[idx], null);
        }

        int FreeFurniture(int kind, int salt)
        {
            var list = Focus.Furniture;
            int n = list.Count;
            for (int k = 0; k < n; k++)
            {
                int i = (k + salt) % n;
                if (list[i].Kind == kind && !FurnitureTaken(i)) return i;
            }
            return -1;
        }

        /// <summary>A free place to stand in the building, away from the others and from the player.</summary>
        bool FloorSpot(CitizenData c, out Vector3 at)
        {
            at = Focus.transform.position;
            if (Focus.Rooms.Count == 0) return false;
            // workers and guests of a shop stay in the front room; at home anybody may be in any room
            for (int r = 0; r < Focus.Rooms.Count; r++)
            {
                var rm = Focus.Rooms[(r + c.Id) % Focus.Rooms.Count];
                Vector3 centre = Focus.transform.TransformPoint(new Vector3((rm.X0 + rm.X1) * 0.5f + ((c.Id * 37) % 5 - 2) * 0.35f, 0f, (rm.Z0 + rm.Z1) * 0.5f + ((c.Id * 53) % 5 - 2) * 0.35f));
                foreach (var s in HomeOwners.Spots(Focus, centre, 8, 0f, true))
                {
                    if (Flat(s - player.transform.position) < 1.1f) continue;
                    bool apart = true;
                    foreach (var g in guests.Values) if (g.Body != null && Flat(g.Body.transform.position - s) < 0.95f) { apart = false; break; }
                    if (!apart) continue;
                    at = s;
                    return true;
                }
            }
            return false;
        }

        bool Add(CitizenData c, Activity act, CitizenSpawner sp)
        {
            var g = new Guest { C = c, Act = act };
            float hour = TownSimulation.Instance.Hour;
            bool sits = act == Activity.Comer || act == Activity.Taberna
                        || (act == Activity.EnCasa && (hour >= 19f || (c.Id + Mathf.FloorToInt(hour / 2f)) % 2 == 0));
            if (act == Activity.Dormir) { g.Furniture = FreeFurniture(Fighter.ActLie, c.Id); g.Kind = Fighter.ActLie; }
            else if (sits) { g.Furniture = FreeFurniture(Fighter.ActChair, c.Id); g.Kind = g.Furniture >= 0 ? Fighter.ActChair : act == Activity.Comer ? Fighter.ActEat : -1; }
            else if (act == Activity.Rezar) { g.Furniture = FreeFurniture(Fighter.ActPray, 0); g.Kind = g.Furniture >= 0 ? Fighter.ActPray : -1; }
            else if (act == Activity.Banarse && Focus.PoolSpots.Count > 0)
            {
                for (int k = 0; k < Focus.PoolSpots.Count && g.Pool < 0; k++)
                {
                    int i = (k + c.Id) % Focus.PoolSpots.Count;
                    bool taken = false;
                    foreach (var o in guests.Values) if (o.Pool == i) taken = true;
                    foreach (var o in Fighter.All) if (o != null && o.OnFurniture && (o.FurnitureAt.position - Focus.PoolSpots[i].position).sqrMagnitude < 0.2f) taken = true;
                    if (!taken) g.Pool = i;
                }
                if (g.Pool >= 0) { g.Kind = Fighter.ActSitGround; g.At = Focus.PoolSpots[g.Pool]; g.Stand = Focus.PoolStand; }
            }

            Vector3 at;
            Quaternion rot = Focus.transform.rotation;
            if (g.Furniture >= 0)
            {
                var fs = Focus.Furniture[g.Furniture];
                g.At = fs.At;
                // he appears on his feet next to it and gets on at once
                Vector3 side = fs.At.position; side.y = Focus.transform.position.y + 0.05f;
                if (!FloorSpot(c, out at)) at = side;
                g.Stand = fs.Stand ?? at;
                rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(fs.At.rotation * Vector3.forward, Vector3.up).normalized);
            }
            else if (g.Pool >= 0) { at = g.At.position; rot = g.At.rotation; }
            else if (!FloorSpot(c, out at)) return false; // no room for one more on his feet

            var body = sp.Summon(c, at, rot);
            if (body == null) return false;
            g.Body = body;
            g.Face = rot * Vector3.forward;
            guests[c.Id] = g;
            return true;
        }

        /// <summary>Somebody else needs this person's body now (the owner throwing the player out): he gets up at once
        /// and is no longer ours.</summary>
        public void Yield(CitizenData c)
        {
            if (c == null || !guests.TryGetValue(c.Id, out var g)) return;
            if (g.Body != null && g.Body.Citizen == c) g.Body.Fighter.AbortActivity();
            guests.Remove(c.Id);
        }

        void Remove(int id, bool dismiss)
        {
            if (!guests.TryGetValue(id, out var g)) return;
            guests.Remove(id);
            if (g.Body == null || g.Body.Citizen != g.C) return;
            g.Body.Fighter.AbortActivity();
            var owner = HomeOwners.Instance != null ? HomeOwners.Instance.Owner : null;
            if (dismiss && owner != g.Body && CitizenSpawner.Instance != null) CitizenSpawner.Instance.Dismiss(g.Body);
        }

        void DismissAll()
        {
            gone.Clear();
            gone.AddRange(guests.Keys);
            foreach (int id in gone) Remove(id, true);
            guests.Clear();
        }
    }
}
