using System.Collections.Generic;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The townsfolk are data while they are indoors. This gives them a body in the one building the player is in (or
    /// at the door of, or a tavern he is walking up to), doing what the simulation says they do there: asleep in their
    /// beds, sitting at the kitchen table for breakfast and supper, at a table of the tavern, in the pool of the
    /// baths, kneeling at the altar, or on their feet at work.
    /// The people already there when the player turns up are in place at once (nobody was looking). From then on,
    /// whoever comes in walks in from the door to their seat, whoever has something else to do gets up and walks to
    /// it, and whoever goes out walks to the door and out into the street, where the townsfolk's own brain takes over.
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
            public Vector3 Stand;           // where he gets up to (and walks to before sitting)
            public Vector3 Dest;            // where he walks to now
            public bool Seated, Walking, Leaving, Placed, NeedWalk, Out;
            public float Tries, Turn, WalkTime, Stuck;
            public Vector3 LastPos;
            public Vector3 Face;
            public readonly List<Vector3> Route = new List<Vector3>();
        }

        TownBootstrap town;
        Fighter player;
        readonly Dictionary<int, Guest> guests = new Dictionary<int, Guest>();
        readonly HashSet<int> want = new HashSet<int>();
        readonly List<int> gone = new List<int>();
        float tick;
        bool settling; // just arrived at a building: who is there gets placed at once

        /// <summary>The building being shown, or null.</summary>
        public HouseInterior Focus { get; private set; }
        public int Count => guests.Count;
        public bool Holds(int citizenId) => guests.ContainsKey(citizenId);
        /// <summary>Tests: came in through the door, walked to a new seat inside, walked out into the street.</summary>
        public int CameIn { get; private set; }
        public int MovedInside { get; private set; }
        public int WentOut { get; private set; }

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

        /// <summary>Somebody shown walking right now (tests).</summary>
        public int WalkingNow { get { int n = 0; foreach (var g in guests.Values) if (g.Walking) n++; return n; } }

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

            foreach (var g in guests.Values) Drive(g);
            // the ones who went out into the street: the street's brain has them now
            gone.Clear();
            foreach (var kv in guests) if (kv.Value.Out || kv.Value.Body == null || kv.Value.Body.Citizen != kv.Value.C) gone.Add(kv.Key);
            foreach (int id in gone) guests.Remove(id);

            if ((tick -= Time.deltaTime) > 0f) return;
            tick = 0.25f;
            var h = Pick(player.transform.position);
            if (h != Focus) { DismissAll(); Focus = h; settling = true; }
            if (Focus == null) return;

            float hour = sim.Hour;
            var owner = HomeOwners.Instance != null ? HomeOwners.Instance.Owner : null;
            want.Clear();
            int added = 0, pending = 0;
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
                    if (have.Act != loc.Activity && !have.Leaving) Reassign(have, loc.Activity); // something else to do here: walks to it
                    continue;
                }
                // somebody with a body in the street right by the door is coming in: let him reach it first
                bool outside = false;
                foreach (var n in sp.Active) if (n.Citizen == c && !n.Controlled) { outside = true; break; }
                if (outside) continue;
                if (added >= (settling ? 6 : 2)) { pending++; continue; }      // a few per tick: no hitches
                if (Add(c, loc.Activity, sp, !settling)) added++;
            }
            if (pending == 0) settling = false;
            gone.Clear();
            foreach (var kv in guests) if (!want.Contains(kv.Key) && !kv.Value.Leaving && !kv.Value.Out) gone.Add(kv.Key);
            foreach (int id in gone) Leave(guests[id]);
        }

        // ------------------------------------------------------------------ moving about
        void Drive(Guest g)
        {
            if (g.Body == null || g.Body.Citizen != g.C || !g.Body.gameObject.activeInHierarchy) return;
            var f = g.Body.Fighter;
            if (f.InActivity)
            {
                // on the bed / the chair: up first if he has to go somewhere
                if (!g.Seated && !f.ActivityLeaving) f.StopActivity();
                return;
            }
            if (g.NeedWalk) { g.NeedWalk = false; WalkTo(g, g.Leaving ? g.Dest : DestFor(g)); }
            if (g.Walking)
            {
                g.WalkTime += Time.deltaTime;
                Vector3 pos = f.transform.position;
                while (g.Route.Count > 0 && Flat(g.Route[0] - pos) < 0.35f) g.Route.RemoveAt(0);
                // there (close enough: sitting down slides him onto the seat), or blocked by furniture for a moment
                g.Stuck = Flat(pos - g.LastPos) < 0.25f * Time.deltaTime ? g.Stuck + Time.deltaTime : 0f;
                g.LastPos = pos;
                bool there = Flat(g.Dest - pos) < (g.Leaving ? 0.5f : 0.8f);
                if (g.Leaving && Focus != null && Focus.Door != null && !Focus.Door.IsOpen && Flat(Focus.Door.Center - pos) < 1.8f)
                { Focus.Door.Toggle(); TownManager.SendDoor(Focus.Door); }
                if (g.Route.Count > 0 && !there && g.Stuck < (g.Leaving ? 2.5f : 1.2f) && g.WalkTime < 12f)
                {
                    Vector3 d = g.Route[0] - pos; d.y = 0f;
                    f.Move(d.normalized * TownSimulation.WalkShare(g.C));
                    f.SetFacing(d);
                    return;
                }
                f.Move(Vector3.zero);
                g.Walking = false;
                if (g.Leaving) { GoOut(g); return; }
                g.Tries = 0f;
            }
            f.Move(Vector3.zero);
            if (g.Kind >= 0 && !g.Seated)
            {
                // on the bed / the chair: at once if he was put there, else he sits down from where he stands
                g.Tries += Time.deltaTime;
                bool ok = g.Furniture >= 0 || g.Pool >= 0 ? f.TryActivityAt(g.Kind, g.At, g.Stand, g.Placed) : f.TryActivity(g.Kind, g.Placed);
                if (ok) { g.Seated = true; if (!g.Placed) MovedInside++; }
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

        void WalkTo(Guest g, Vector3 dest)
        {
            g.Dest = dest;
            g.Walking = true;
            g.WalkTime = 0f;
            g.Stuck = 0f;
            g.LastPos = g.Body.transform.position;
            TownNavMesh.Path(g.Body.transform.position, dest, g.Route, true);
        }

        /// <summary>The bed / chair / place he goes to for an activity (claims it).</summary>
        void Choose(Guest g, Activity act)
        {
            var c = g.C;
            g.Act = act; g.Furniture = -1; g.Pool = -1; g.Kind = -1; g.Seated = false; g.Placed = false; g.Tries = 0f;
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
                    foreach (var o in guests.Values) if (o != g && o.Pool == i) taken = true;
                    foreach (var o in Fighter.All) if (o != null && o.OnFurniture && (o.FurnitureAt.position - Focus.PoolSpots[i].position).sqrMagnitude < 0.2f) taken = true;
                    if (!taken) g.Pool = i;
                }
                if (g.Pool >= 0) { g.Kind = Fighter.ActSitGround; g.At = Focus.PoolSpots[g.Pool]; g.Stand = Focus.PoolStand; }
            }
            if (g.Furniture >= 0)
            {
                var fs = Focus.Furniture[g.Furniture];
                g.At = fs.At;
                Vector3 floor = fs.At.position; floor.y = Focus.transform.position.y + 0.05f;
                if (!TownNavMesh.Snap(floor, out Vector3 beside, 1.6f, true)) beside = floor;
                g.Stand = fs.Stand ?? beside;
            }
        }

        /// <summary>Where he stands on his feet, or by his seat.</summary>
        Vector3 DestFor(Guest g)
        {
            if (g.Furniture >= 0 || g.Pool >= 0) return g.Stand;
            return FloorSpot(g.C, out Vector3 at) ? at : g.Body.transform.position;
        }

        void Reassign(Guest g, Activity act)
        {
            Choose(g, act);
            g.Walking = false;
            g.NeedWalk = true; // Drive gets him up first if he is sitting, then he walks
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
            for (int r = 0; r < Focus.Rooms.Count; r++)
            {
                var rm = Focus.Rooms[(r + c.Id) % Focus.Rooms.Count];
                Vector3 centre = Focus.transform.TransformPoint(new Vector3((rm.X0 + rm.X1) * 0.5f + ((c.Id * 37) % 5 - 2) * 0.35f, 0f, (rm.Z0 + rm.Z1) * 0.5f + ((c.Id * 53) % 5 - 2) * 0.35f));
                foreach (var s in HomeOwners.Spots(Focus, centre, 8, 0f, true))
                {
                    if (Flat(s - player.transform.position) < 1.1f) continue;
                    bool apart = true;
                    foreach (var g in guests.Values) if (g.Body != null && (Flat(g.Body.transform.position - s) < 0.95f || (g.Walking && Flat(g.Dest - s) < 0.95f))) { apart = false; break; }
                    if (!apart) continue;
                    at = s;
                    return true;
                }
            }
            return false;
        }

        /// <summary>A body for somebody indoors: in place at once (<paramref name="walkIn"/> false: nobody was looking),
        /// or coming in through the door and walking to his place.</summary>
        bool Add(CitizenData c, Activity act, CitizenSpawner sp, bool walkIn)
        {
            var g = new Guest { C = c };
            guests[c.Id] = g; // claims the seat while choosing
            Choose(g, act);
            Vector3 at;
            Quaternion rot = Focus.transform.rotation;
            if (walkIn && Focus.Door != null)
            {
                at = HomeOwners.DoorSpot(Focus);
                Vector3 inward = Focus.transform.position - at; inward.y = 0f;
                if (inward.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(inward);
            }
            else if (g.Furniture >= 0)
            {
                at = g.At.position; at.y = Focus.transform.position.y + 0.05f;
                rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(g.At.rotation * Vector3.forward, Vector3.up).normalized);
            }
            else if (g.Pool >= 0) { at = g.At.position; rot = g.At.rotation; }
            else if (!FloorSpot(c, out at)) { guests.Remove(c.Id); return false; } // no room for one more on his feet

            var body = sp.Summon(c, at, rot);
            if (body == null) { guests.Remove(c.Id); return false; }
            g.Body = body;
            g.Face = rot * Vector3.forward;
            if (walkIn && Focus.Door != null)
            {
                if (!Focus.Door.IsOpen) { Focus.Door.Toggle(); TownManager.SendDoor(Focus.Door); }
                WalkTo(g, DestFor(g));
                CameIn++;
            }
            else g.Placed = true;
            return true;
        }

        /// <summary>Off out: up, to the door, and out into the street.</summary>
        void Leave(Guest g)
        {
            g.Leaving = true;
            g.Seated = false;
            g.Furniture = -1; g.Pool = -1;
            if (g.Body == null) return;
            if (Focus == null || Focus.Door == null) { Remove(g.C.Id, true); return; }
            // out through the doorway to the street, on his own feet
            Vector3 street = Focus.Lot.Door;
            if (TownNavMesh.Snap(street, out Vector3 st, 2f)) street = st;
            g.Dest = street;
            if (!Focus.Door.IsOpen) { Focus.Door.Toggle(); TownManager.SendDoor(Focus.Door); } // open by the time he gets there
            g.Walking = false;
            g.NeedWalk = true;
        }

        /// <summary>At the door: through it, into the street (the street's brain drives him from here).</summary>
        void GoOut(Guest g)
        {
            var body = g.Body;
            if (body == null || Focus == null) return;
            var door = Focus.Door;
            if (door != null && !door.IsOpen) { door.Toggle(); TownManager.SendDoor(door); }
            // already out in the street (or stuck in the doorway: then the last step is a short hop)
            if (Flat(body.transform.position - g.Dest) > 0.9f)
            {
                Vector3 outward = g.Dest - Focus.transform.position; outward.y = 0f;
                body.Place(g.Dest, outward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(outward) : body.transform.rotation);
            }
            body.Controlled = false;
            g.Out = true; // dropped from the list after this frame's round
            WentOut++;
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
