using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// Scatters crates and jars about the town: a few inside the homes and shops (along the walls) and some outside,
    /// against the houses. Where they go comes from the town's seed and the day, so every day finds them elsewhere
    /// (online only from the seed, so every player numbers them alike).
    /// </summary>
    public static class TownProps
    {
        public static int Indoors, Outdoors;

        public static void Spawn(TownBootstrap town)
        {
            var L = town.Layout;
            var net = Gladiator.Net.NetSession.Instance;
            bool online = net != null && net.InGame;
            int day = !online && TownSimulation.Instance != null ? TownSimulation.Instance.Day : 0;
            var rng = new System.Random(L.Seed * 31 + 7 + day * 131);
            var root = new GameObject("Props").transform;
            root.SetParent(town.transform, false);
            Physics.SyncTransforms();
            int mask = Ragdoll.WorldMask & ~(1 << Ragdoll.FighterLayer);
            int id = 0;
            Indoors = Outdoors = 0;

            // inside: along the walls of homes and shops
            if (town.Houses != null)
                foreach (var h in town.Houses)
                {
                    if (h == null || h.Floor != 0 || h.Lot == null || !(h.Lot.IsHome || h.Lot.IsShop)) continue;
                    int n = h.Lot.ForSale ? rng.Next(0, 2) : h.Lot.IsShop ? rng.Next(1, 4) : rng.Next(0, 3);
                    float jars = h.Lot.Kind == BuildingKind.Tavern || h.Lot.Kind == BuildingKind.Pottery ? 0.8f : h.Lot.Kind == BuildingKind.Smithy ? 0.2f : 0.5f;
                    for (int i = 0; i < n; i++)
                    {
                        id++;
                        var kind = rng.NextDouble() < jars ? TownProp.Kind.Jar : TownProp.Kind.Crate;
                        float scale = 0.85f + (float)rng.NextDouble() * 0.3f, yaw = (float)rng.NextDouble() * 360f;
                        bool placed = false;
                        for (int t = 0; t < 8; t++)
                        {
                            int wall = rng.Next(4);
                            float u = (float)rng.NextDouble() * 2f - 1f;
                            if (placed) continue; // the dice still roll: the same numbers for everybody
                            float x = wall < 2 ? (wall == 0 ? -1f : 1f) * (h.HalfX - 0.6f) : u * (h.HalfX - 0.6f);
                            float z = wall < 2 ? u * (h.HalfZ - 0.6f) : (wall == 2 ? -1f : 1f) * (h.HalfZ - 0.6f);
                            Vector3 p = h.transform.TransformPoint(new Vector3(x, 0f, z));
                            Vector3 dd = p - h.Lot.Door; dd.y = 0f;
                            if (dd.magnitude < 2.2f) continue;
                            if (!Free(ref p, h.transform.rotation, mask)) continue;
                            TownProp.Create(root, kind, id, p, wall < 2 ? 0f + h.transform.eulerAngles.y : yaw, scale);
                            placed = true; Indoors++;
                        }
                    }
                }

            // outside: against the side walls and the fronts of the houses
            foreach (var lot in L.Lots)
            {
                if (lot.Hidden || !(lot.IsHome || lot.IsShop)) continue;
                bool some = rng.NextDouble() < (lot.IsShop ? 0.8 : 0.4);
                int n = rng.Next(1, 4);
                int side = rng.Next(3);                      // 0 left wall, 1 right wall, 2 the front
                float along = (float)rng.NextDouble() * 2f - 1f;
                for (int i = 0; i < 3; i++)
                {
                    id++;
                    var kind = rng.NextDouble() < (lot.Kind == BuildingKind.Tavern || lot.Kind == BuildingKind.Pottery ? 0.75 : 0.4) ? TownProp.Kind.Jar : TownProp.Kind.Crate;
                    float scale = 0.85f + (float)rng.NextDouble() * 0.3f;
                    float jitter = (float)rng.NextDouble() * 0.2f;
                    if (!some || i >= n) continue;
                    float hw = lot.Width * 0.5f, hd = lot.Depth * 0.5f;
                    float step = (i - (n - 1) * 0.5f) * 0.8f;
                    Vector3 l = side < 2 ? new Vector3((side == 0 ? -1f : 1f) * (hw + 0.5f + jitter), 0f, Mathf.Clamp(along * (hd - 1f) + step, -hd + 0.5f, hd - 0.5f))
                                         : new Vector3(Mathf.Clamp(along * (hw - 1f) + step, -hw + 0.5f, hw - 0.5f), 0f, hd + 0.55f + jitter);
                    Vector3 p = lot.Center + lot.Rotation * l;
                    Vector3 dd = p - lot.Door; dd.y = 0f;
                    if (dd.magnitude < 1.9f) continue;
                    if (lot.Kind == BuildingKind.Smithy) { Vector3 da = p - lot.ArmorerSpot; da.y = 0f; if (da.magnitude < 2.6f) continue; }
                    if (town.HouseAt(p + Vector3.up * 0.3f) != null) continue;
                    if (!Free(ref p, lot.Rotation, mask)) continue;
                    TownProp.Create(root, kind, id, p, lot.Rotation.eulerAngles.y + jitter * 60f, scale);
                    Outdoors++;
                }
            }
            // the rows of jars outside the pottery and the taverns
            if (town.JarSpots != null)
                for (int i = 0; i < town.JarSpots.Count; i++)
                {
                    Vector3 jp = town.JarSpots[i];
                    if (Physics.Raycast(jp + Vector3.up * 1.2f, Vector3.down, out RaycastHit jh, 2.5f, mask, QueryTriggerInteraction.Ignore)) jp.y = jh.point.y;
                    TownProp.Create(root, TownProp.Kind.Jar, 5000 + i, jp, (i * 47) % 360, 1.15f);
                    Outdoors++;
                }
            Debug.Log($"[TOWN] props: {Indoors} indoors, {Outdoors} outdoors (day {day})");
        }

        /// <summary>Room for a crate there, on a floor: nothing in the way (walls, furniture, other props).</summary>
        static bool Free(ref Vector3 p, Quaternion rot, int mask)
        {
            if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 2.5f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.rigidbody != null) return false;
            if (hit.point.y > p.y + 0.3f || hit.point.y < p.y - 0.6f) return false; // on a table, a bed, a bench: no
            p.y = hit.point.y;
            if (Physics.CheckBox(p + Vector3.up * 0.5f, new Vector3(0.36f, 0.36f, 0.36f), rot, mask, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }
    }
}
