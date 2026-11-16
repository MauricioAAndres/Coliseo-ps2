using System.Collections.Generic;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// How somebody fighting in town (a guard, a householder with a knife) gets to whoever they are after: straight at
    /// them when they are close and in sight, otherwise along the navigation mesh, round the houses and in through the
    /// doors (opening them on the way). Plugged into <see cref="EnemyAI.Steer"/> while the town is loaded.
    /// </summary>
    public static class TownChase
    {
        /// <summary>Closer than this and in sight: fight, no path.</summary>
        public const float DirectRange = 7f;

        class State { public readonly List<Vector3> Corners = new List<Vector3>(); public float Repath; public Vector3 Goal; }
        static readonly Dictionary<Fighter, State> states = new Dictionary<Fighter, State>();
        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>Frames spent following a path, and doors opened on the way (tests).</summary>
        public static int SteeredFrames, DoorsOpened;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { states.Clear(); SteeredFrames = 0; DoorsOpened = 0; }

        public static void Clear() { states.Clear(); }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        /// <summary>Nothing solid between the two (other bodies and loose crates do not count).</summary>
        public static bool InSight(Fighter a, Fighter b)
        {
            Vector3 from = a.transform.position + Vector3.up * 1.3f, to = b.transform.position + Vector3.up * 1.3f;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.05f) return true;
            int mask = Ragdoll.WorldMask & ~(1 << Ragdoll.FighterLayer);
            int n = Physics.RaycastNonAlloc(from, d / len, hits, len, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null || c.attachedRigidbody != null || c.GetComponentInParent<Fighter>() != null) continue;
                return false;
            }
            return true;
        }

        /// <summary>Where to walk now to reach <paramref name="target"/>, or null to go straight at him.</summary>
        public static Vector3? Steer(Fighter self, Fighter target)
        {
            if (self == null || target == null || !TownNavMesh.Ready) return null;
            Vector3 a = self.transform.position, b = target.transform.position;
            float d = Flat(b - a);
            if (d < DirectRange && Mathf.Abs(b.y - a.y) < 1.2f && InSight(self, target)) { states.Remove(self); return null; }
            if (!states.TryGetValue(self, out var st)) states[self] = st = new State { Repath = 0f };
            st.Repath -= Time.deltaTime;
            if (st.Repath <= 0f || Flat(b - st.Goal) > 1.5f || st.Corners.Count == 0)
            {
                TownNavMesh.Path(a, b, st.Corners, true);
                st.Goal = b;
                st.Repath = 0.5f;
            }
            while (st.Corners.Count > 0 && Flat(st.Corners[0] - a) < 0.45f) st.Corners.RemoveAt(0);
            if (st.Corners.Count == 0) return null;
            Vector3 next = st.Corners[0];
            OpenDoorOnTheWay(a, next);
            SteeredFrames++;
            return next;
        }

        static void OpenDoorOnTheWay(Vector3 at, Vector3 next)
        {
            var town = TownBootstrap.Instance;
            if (town == null) return;
            var door = town.NearestDoor(at, 1.7f);
            if (door == null || door.IsOpen) return;
            Vector3 toDoor = door.Center - at; toDoor.y = 0f;
            Vector3 toNext = next - at; toNext.y = 0f;
            bool ahead = toDoor.sqrMagnitude < 0.36f || (toNext.sqrMagnitude > 0.0001f && Vector3.Dot(toDoor.normalized, toNext.normalized) > 0.25f)
                         || Flat(door.Center - next) < 1.8f;
            if (!ahead) return;
            door.Toggle();
            TownManager.SendDoor(door);
            DoorsOpened++;
        }
    }
}
