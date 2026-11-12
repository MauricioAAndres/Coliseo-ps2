using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// Turns a <see cref="TownLayout"/> into low-poly geometry with the ludus' materials (PS2_Lit, same textures and
    /// tints). Static pieces are merged into one mesh per material (a few dozen draw calls for the whole town);
    /// trees, torches and colliders are separate objects. Ambient light is baked into vertex colours like the ludus.
    /// </summary>
    public class TownBuilder
    {
        public static readonly Color AmbientSky = new Color(0.52f, 0.5f, 0.54f);
        public static readonly Color AmbientGround = new Color(0.32f, 0.25f, 0.17f);

        readonly Dictionary<Material, MeshBuilder> batches = new Dictionary<Material, MeshBuilder>();
        readonly Transform root, colliders;
        readonly TownLayout L;

        // ---- palette (same keys as the ludus, so the materials are literally shared)
        readonly Material sand, sandFar, stone, stoneDark, wood, darkWood, roofMat, banner, straw, clay, iron, awningA, awningB, foliage, water, dark, ember, paving, pavingDark;
        readonly Material[] plaster, roofs;
        static readonly Color[] PlasterTints =
        {
            new Color(0.95f, 0.88f, 0.76f), new Color(0.96f, 0.8f, 0.6f), new Color(0.9f, 0.7f, 0.58f),
            new Color(0.98f, 0.95f, 0.88f), new Color(0.85f, 0.78f, 0.62f), new Color(0.93f, 0.74f, 0.66f),
        };
        static readonly Color[] RoofTints =
        {
            new Color(0.75f, 0.38f, 0.26f), new Color(0.68f, 0.32f, 0.22f), new Color(0.8f, 0.46f, 0.3f), new Color(0.6f, 0.36f, 0.28f),
        };

        public int BuildingCount { get; private set; }

        /// <summary>Who lives in a storey (lot id, floor): a couple?, other sleepers (children, elders), people at the table.</summary>
        public System.Func<int, int, (bool couple, int singles, int people)> Household;
        readonly Material waxMat = PS2Materials.Get("wax", PS2Textures.White, new Color(0.95f, 0.92f, 0.8f));
        readonly Material flameMat = PS2Materials.Flat(new Color(1f, 0.62f, 0.15f), 0.3f);
        readonly Material glass = MakeGlass();

        /// <summary>Window glass: translucent, seen from both sides.</summary>
        static Material MakeGlass()
        {
            var sh = Shader.Find("Gladiator/PS2_Glass");
            if (sh == null) return PS2Materials.Flat(new Color(0.45f, 0.6f, 0.7f), 1f);
            var m = new Material(sh) { name = "PS2_glass" };
            m.SetColor("_BaseColor", new Color(0.62f, 0.82f, 0.92f, 0.26f));
            return m;
        }

        public TownBuilder(TownLayout layout, Transform parent)
        {
            L = layout;
            root = new GameObject("Town").transform;
            root.SetParent(parent, false);
            colliders = new GameObject("Colliders").transform;
            colliders.SetParent(root, false);

            sand = PS2Materials.Get("sand", PS2Textures.Sand, Color.white);
            sandFar = PS2Materials.Get("sand_far", PS2Textures.Sand, new Color(0.9f, 0.85f, 0.78f));
            stone = PS2Materials.Get("stone", PS2Textures.Stone, Color.white);
            stoneDark = PS2Materials.Get("stone_dark", PS2Textures.Stone, new Color(0.72f, 0.66f, 0.6f));
            paving = PS2Materials.Get("paving", PS2Textures.Stone, new Color(0.86f, 0.82f, 0.76f));
            pavingDark = PS2Materials.Get("paving_dark", PS2Textures.Stone, new Color(0.7f, 0.66f, 0.6f));
            wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            darkWood = PS2Materials.Get("wood_dark", PS2Textures.Wood, new Color(0.6f, 0.5f, 0.42f));
            roofMat = PS2Materials.Get("roof", PS2Textures.Stone, RoofTints[0]);
            banner = PS2Materials.Get("banner", PS2Textures.Cloth, new Color(0.62f, 0.1f, 0.08f));
            straw = PS2Materials.Get("straw", PS2Textures.Cloth, new Color(0.95f, 0.8f, 0.42f));
            clay = PS2Materials.Get("clay", PS2Textures.Sand, new Color(0.75f, 0.45f, 0.3f));
            iron = PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f));
            awningA = PS2Materials.Get("awning_a", PS2Textures.Cloth, new Color(0.85f, 0.75f, 0.55f));
            awningB = PS2Materials.Get("awning_b", PS2Textures.Cloth, new Color(0.25f, 0.4f, 0.65f));
            foliage = PS2Materials.Get("foliage", PS2Textures.Cloth, new Color(0.22f, 0.36f, 0.2f));
            water = PS2Materials.Flat(new Color(0.25f, 0.4f, 0.45f), 1f);
            dark = PS2Materials.Flat(new Color(0.08f, 0.06f, 0.05f), 1f);
            ember = PS2Materials.Flat(new Color(1f, 0.45f, 0.12f), 0.3f);
            plaster = new Material[PlasterTints.Length];
            for (int i = 0; i < plaster.Length; i++)
                plaster[i] = i == 0 ? PS2Materials.Get("plaster", PS2Textures.Stone, PlasterTints[0])
                                    : PS2Materials.Get("plaster_" + i, PS2Textures.Stone, PlasterTints[i]);
            roofs = new Material[RoofTints.Length];
            for (int i = 0; i < roofs.Length; i++)
                roofs[i] = i == 0 ? roofMat : PS2Materials.Get("roof_" + i, PS2Textures.Stone, RoofTints[i]);
        }

        // ------------------------------------------------------------------ batching helpers
        // Where Box/Face/Gable write: the town-wide batches, or one house's own pieces (its roof hides when you go in).
        Dictionary<Material, MeshBuilder> current;

        MeshBuilder B(Material m)
        {
            var target = current ?? batches;
            if (!target.TryGetValue(m, out var b)) { b = new MeshBuilder(); target[m] = b; }
            return b;
        }

        /// <summary>The houses you can walk into (TownManager hides their roof while you are inside).</summary>
        public readonly List<HouseInterior> Houses = new List<HouseInterior>();

        /// <summary>Box in world space.</summary>
        void Box(Material m, Vector3 center, Vector3 size, Quaternion rot, float uv = 1f / 3f) => B(m).AddBox(center, size, rot, Color.white, uv);

        /// <summary>A flat polygon (3 or 4 points, world). The winding is fixed so it faces <paramref name="outward"/>.</summary>
        void Face(Material m, Vector3 outward, float uv, params Vector3[] p)
        {
            var b = B(m);
            Vector3 n = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
            if (Vector3.Dot(n, outward) < 0f) { System.Array.Reverse(p); n = -n; }
            Vector3 u = (p[1] - p[0]).normalized, v = Vector3.Cross(n, u);
            int[] idx = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
                idx[i] = b.Add(p[i], n, new Vector2(Vector3.Dot(p[i], u), Vector3.Dot(p[i], v)) * uv, Color.white);
            if (p.Length == 3) b.Tri(idx[0], idx[1], idx[2]);
            else b.Quad(idx[0], idx[1], idx[2], idx[3]);
        }

        /// <summary>Gable roof: ridge along local x, slopes front/back, triangular ends in the wall material.</summary>
        void Gable(Material roofM, Material wallM, Vector3 baseCenter, Quaternion rot, float w, float d, float rise)
        {
            Vector3 P(float x, float y, float z) => baseCenter + rot * new Vector3(x, y, z);
            float hw = w * 0.5f, hd = d * 0.5f;
            Vector3 fl = P(-hw, 0, hd), fr = P(hw, 0, hd), bl = P(-hw, 0, -hd), br = P(hw, 0, -hd), rl = P(-hw, rise, 0), rr = P(hw, rise, 0);
            Vector3 up = rot * Vector3.up, fwd = rot * Vector3.forward, right = rot * Vector3.right;
            Face(roofM, up + fwd, 0.5f, fl, fr, rr, rl);
            Face(roofM, up - fwd, 0.5f, br, bl, rl, rr);
            Face(wallM, -right, 1f / 3f, bl, fl, rl);
            Face(wallM, right, 1f / 3f, fr, br, rr);
            // underside so it is never see-through from below
            Face(roofM, -up, 0.5f, fl, bl, br, fr);
        }

        void Collider(Vector3 center, Vector3 size, Quaternion rot) => PS2Spawn.BoxCollider(colTarget != null ? colTarget : colliders, center, size, rot);

        // ------------------------------------------------------------------ build
        public void Build()
        {
            BuildGround();
            foreach (var lot in L.Lots) BuildLot(lot);
            BuildFountain();
            foreach (var s in L.Stalls) BuildStall(s.Key, s.Value);
            BuildArena();
            // the ludus is built by Training.LudusInTown (its yard, open to the town)
            BuildTownWall();

            // merge: one mesh per material
            var statics = new GameObject("Static").transform;
            statics.SetParent(root, false);
            foreach (var kv in batches)
                PS2Spawn.Mesh(statics, "Town_" + kv.Key.name, kv.Value.Build("Town_" + kv.Key.name), Vector3.zero, kv.Key);
            LowPolyMesh.BakeAmbient(statics.gameObject, AmbientSky, AmbientGround);

            foreach (var t in L.Trees) BuildCypress(t);
            foreach (var t in L.Torches) BuildTorch(t);
        }

        void BuildGround()
        {
            // plain outside the walls
            var gb = new MeshBuilder();
            gb.AddPolarDisc(260f, 6, 24, -0.03f, Color.white, 1f / 6f);
            PS2Spawn.Mesh(root, "Plain", gb.Build("TownPlain"), (L.WallCorners[0] + L.WallCorners[2]) * 0.5f, sandFar);

            // sand inside the walls: one subdivided grid (vertex lighting needs vertices)
            Vector3 c = (L.WallCorners[0] + L.WallCorners[2]) * 0.5f;
            Quaternion q = Quaternion.Euler(0, L.RotationDeg, 0);
            float wx = TownLayout.WallX * 2f, wz = TownLayout.WallZMax - TownLayout.WallZMin;
            var floor = new MeshBuilder();
            floor.AddGrid(wx, wz, 40, 60, 0f, Color.white, 1f / 3.5f);
            var fm = floor.Build("TownSand");
            var fgo = PS2Spawn.Mesh(root, "Ground", fm, c, sand, q);
            LowPolyMesh.BakeAmbient(fgo, AmbientSky, AmbientGround);
            var col = fgo.AddComponent<BoxCollider>();
            col.center = new Vector3(0, -0.5f, 0);
            col.size = new Vector3(wx + 20f, 1f, wz + 20f);

            // paving (plaza lighter, streets darker), slightly raised to avoid z-fighting
            foreach (var p in L.Paving)
            {
                var b = new MeshBuilder();
                int dx = Mathf.Max(2, Mathf.RoundToInt(p.Size.x / 3f)), dz = Mathf.Max(2, Mathf.RoundToInt(p.Size.y / 3f));
                b.AddGrid(p.Size.x, p.Size.y, dx, dz, p.Plaza ? 0.025f : 0.015f, Color.white, 1f / 2f);
                var go = PS2Spawn.Mesh(root, p.Plaza ? "Plaza" : "Street", b.Build("Paving"), p.Center, p.Plaza ? paving : pavingDark, Quaternion.Euler(0, p.Yaw, 0));
                LowPolyMesh.BakeAmbient(go, AmbientSky, AmbientGround);
            }
        }

        // ------------------------------------------------------------------ buildings
        void BuildLot(TownLot lot)
        {
            if (lot.Hidden) return; // under the jail
            BuildingCount++;
            switch (lot.Kind)
            {
                case BuildingKind.Garden: Garden(lot); return;
                case BuildingKind.Temple: Temple(lot); return;
                case BuildingKind.Baths: Baths(lot); return;
                case BuildingKind.Forum: Basilica(lot); return;
                case BuildingKind.Infirmary: Infirmary(lot); return;
                case BuildingKind.Governor: GovernorDomus(lot); return;
                case BuildingKind.Jail: Jail(lot); return;
            }
            if (lot.IsHome) { HomeHouse(lot); return; }
            // shops: a real door and the inside of a home (kitchen, bedroom, bathroom); the trade goes outside the front
            if (lot.IsShop) HomeHouse(lot); else House(lot);
            switch (lot.Kind)
            {
                case BuildingKind.Shop: ShopFront(lot, awningA, awningB); break;
                case BuildingKind.Tavern: ShopFront(lot, banner, awningA); Amphorae(lot, 4); Sign(lot, new Color(0.62f, 0.1f, 0.08f)); break;
                case BuildingKind.Smithy: Smithy(lot); Sign(lot, new Color(0.35f, 0.35f, 0.38f)); ShopMarker(lot); break;
                case BuildingKind.Bakery: Oven(lot); Sign(lot, new Color(0.85f, 0.65f, 0.3f)); break;
                case BuildingKind.Pottery: Amphorae(lot, 7); Oven(lot); Sign(lot, new Color(0.75f, 0.45f, 0.3f)); break;
            }
        }

        Vector3 LP(TownLot lot, float x, float y, float z) => lot.Center + lot.Rotation * new Vector3(x, y, z);

        /// <summary>Plaster house: stone base, door, shuttered windows per floor, gable roof (low for insulae, with a balcony).</summary>
        void House(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth, floorH = 3.1f, h = floorH * lot.Floors;
            Material wall = plaster[lot.Tint % plaster.Length];
            Box(wall, LP(lot, 0, h * 0.5f, 0), new Vector3(w, h, d), q);
            Box(stoneDark, LP(lot, 0, 0.35f, 0), new Vector3(w + 0.1f, 0.7f, d + 0.1f), q);
            bool insula = lot.Kind == BuildingKind.Insula;
            Gable(roofs[lot.RoofTint % roofs.Length], wall, LP(lot, 0, h, 0), q, w + 0.6f, d + 0.6f, insula ? 0.9f : Mathf.Min(1.8f, d * 0.22f));

            // door (front), with a wooden frame
            float dx = lot.DoorOffset * w, hz = d * 0.5f;
            Box(dark, LP(lot, dx, 1.1f, hz + 0.02f), new Vector3(1.2f, 2.2f, 0.06f), q);
            Box(darkWood, LP(lot, dx, 2.3f, hz + 0.05f), new Vector3(1.5f, 0.2f, 0.12f), q);
            // windows: small dark openings with wooden shutters, upper floors more of them
            for (int f = 0; f < lot.Floors; f++)
            {
                int n = f == 0 ? 2 : Mathf.Max(2, Mathf.FloorToInt(w / 2.4f));
                for (int i = 0; i < n; i++)
                {
                    float x = -w * 0.5f + w * (i + 0.5f) / n;
                    if (f == 0 && Mathf.Abs(x - dx) < 1.4f) continue;
                    float y = f * floorH + (f == 0 ? 2.0f : 1.6f);
                    Box(dark, LP(lot, x, y, hz + 0.02f), new Vector3(0.6f, 0.75f, 0.06f), q);
                    Box(darkWood, LP(lot, x - 0.42f, y, hz + 0.05f), new Vector3(0.22f, 0.8f, 0.05f), q);
                    Box(darkWood, LP(lot, x + 0.42f, y, hz + 0.05f), new Vector3(0.22f, 0.8f, 0.05f), q);
                }
                // side windows
                if (f > 0 || !insula)
                    Box(dark, LP(lot, w * 0.5f + 0.02f, f * floorH + 1.8f, 0), new Vector3(0.06f, 0.7f, 0.6f), q);
            }
            if (insula)
            {
                // wooden balcony along the first floor
                Box(wood, LP(lot, 0, floorH, hz + 0.6f), new Vector3(w * 0.8f, 0.12f, 1.2f), q);
                Box(darkWood, LP(lot, 0, floorH + 0.55f, hz + 1.15f), new Vector3(w * 0.8f, 0.08f, 0.08f), q);
                for (int i = 0; i <= 4; i++)
                    Box(darkWood, LP(lot, -w * 0.4f + w * 0.8f * i / 4f, floorH + 0.3f, hz + 1.15f), new Vector3(0.08f, 0.6f, 0.08f), q);
                foreach (float s in new[] { -0.4f, 0.4f })
                    Box(darkWood, LP(lot, w * s, floorH * 0.5f, hz + 1.15f), new Vector3(0.14f, floorH, 0.14f), q);
            }
            // full height (roof included): the camera must know the house is there
            float top = h + (insula ? 0.9f : Mathf.Min(1.8f, d * 0.22f));
            Collider(LP(lot, 0, top * 0.5f, 0), new Vector3(w, top, d), q);
            Collider(LP(lot, 0, h + 0.2f + (top - h) * 0.5f, 0), new Vector3(w + 0.6f, top - h, d + 0.6f), q); // eaves (camera)
        }

        /// <summary>
        /// A home you can enter. Every storey has a kitchen at the front and, behind a partition with two openings, a
        /// bedroom and a bathroom (floors: stone, wood, light tiles; no inner doors, no furniture yet). The ground floor
        /// opens on the street; a two-storey house has an outside staircase along one side up to a landing and a door
        /// into its first floor (its own kitchen, bedroom and bathroom). Whatever is above the storey you are in
        /// (upper floor, roof) hides while you are inside so the camera can look in.
        /// </summary>
        void HomeHouse(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth, H = 3.1f;
            float hx = w * 0.5f, hz = d * 0.5f, h = H * lot.Floors;
            bool insula = lot.Kind == BuildingKind.Insula;
            bool stairs = lot.Floors >= 2;
            bool third = lot.Floors >= 3;          // a second outside staircase, from the front balcony, on the other side
            float rise = insula ? 0.9f : Mathf.Min(1.8f, d * 0.22f);
            Material wall = plaster[lot.Tint % plaster.Length];
            int ss = lot.StairSide == 0 ? 1 : lot.StairSide;

            // ---- ground floor (town-wide batches, static colliders)
            var ground = Storey(lot, 0, wall, false, ss);

            // ---- first floor of a two-storey house: its own object, hidden while you are downstairs
            GameObject midGo = null;
            HouseInterior upperHouse = null;
            if (stairs)
            {
                midGo = new GameObject("Floor1_" + lot.Id);
                midGo.transform.SetParent(root, false);
                var mid = new Dictionary<Material, MeshBuilder>();
                current = mid; colTarget = midGo.transform;
                Box(wall, LP(lot, 0, H + 0.075f, 0), new Vector3(w, 0.15f, d), q);           // the floor between storeys
                Collider(LP(lot, 0, H + 0.075f, 0), new Vector3(w, 0.15f, d), q);
                upperHouse = Storey(lot, 1, wall, true, ss);
                if (insula && !third)
                {   // the wooden balcony on the front of the first floor
                    Box(wood, LP(lot, 0, H, hz + 0.6f), new Vector3(w * 0.8f, 0.12f, 1.2f), q);
                    Box(darkWood, LP(lot, 0, H + 0.55f, hz + 1.15f), new Vector3(w * 0.8f, 0.08f, 0.08f), q);
                    for (int i = 0; i <= 4; i++)
                        Box(darkWood, LP(lot, -w * 0.4f + w * 0.8f * i / 4f, H + 0.3f, hz + 1.15f), new Vector3(0.08f, 0.6f, 0.08f), q);
                    foreach (float sx in new[] { -0.4f, 0.4f })
                        Box(darkWood, LP(lot, w * sx, H * 0.5f, hz + 1.15f), new Vector3(0.14f, H, 0.14f), q);
                }
                current = null; colTarget = null;
                foreach (var kv in mid) PS2Spawn.Mesh(midGo.transform, "F1_" + kv.Key.name, kv.Value.Build("HouseFloor1"), Vector3.zero, kv.Key);
                LowPolyMesh.BakeAmbient(midGo, AmbientSky, AmbientGround);
                upperHouse.transform.SetParent(midGo.transform, true);
                BuildStairs(lot, ss, H, upperHouse, third);
            }

            // ---- second floor of a three-storey house: its own object, reached by the balcony and the second staircase
            GameObject topGo = null;
            HouseInterior topHouse = null;
            if (third)
            {
                topGo = new GameObject("Floor2_" + lot.Id);
                topGo.transform.SetParent(root, false);
                var top2 = new Dictionary<Material, MeshBuilder>();
                current = top2; colTarget = topGo.transform;
                Box(wall, LP(lot, 0, 2f * H + 0.075f, 0), new Vector3(w, 0.15f, d), q);
                Collider(LP(lot, 0, 2f * H + 0.075f, 0), new Vector3(w, 0.15f, d), q);
                // the second stair arrives at the back: this storey is laid out turned round (kitchen and side door at
                // the back, bedroom + bathroom towards the street), so you walk in through the kitchen. In the turned
                // frame the real -ss side is +ss and its "front" door spot (hz - 1.3) is the real back.
                var turned = new TownLot
                {
                    Id = lot.Id, Kind = lot.Kind, Center = lot.Center, Front = -lot.Front, Width = lot.Width, Depth = lot.Depth,
                    Floors = lot.Floors, Tint = lot.Tint, RoofTint = lot.RoofTint, DoorOffset = lot.DoorOffset, StairSide = lot.StairSide,
                    ForSale = lot.ForSale, Large = lot.Large, Price = lot.Price,
                };
                topHouse = Storey(turned, 2, wall, true, ss);
                current = null; colTarget = null;
                foreach (var kv in top2) PS2Spawn.Mesh(topGo.transform, "F2_" + kv.Key.name, kv.Value.Build("HouseFloor2"), Vector3.zero, kv.Key);
                LowPolyMesh.BakeAmbient(topGo, AmbientSky, AmbientGround);
                topHouse.transform.SetParent(topGo.transform, true);
                BuildBalconyAndSecondStairs(lot, ss, H, topHouse);
            }

            // ---- the top: ceiling of the highest open storey, solid storeys above it, roof + a collider for the camera
            float topBase = third ? 3f * H : stairs ? 2f * H : H;
            var mine = new Dictionary<Material, MeshBuilder>();
            current = mine;
            Box(wall, LP(lot, 0, topBase + 0.075f, 0), new Vector3(w, 0.15f, d), q);
            if (h > topBase + 0.2f) Box(wall, LP(lot, 0, (topBase + 0.15f + h) * 0.5f, 0), new Vector3(w, h - topBase - 0.15f, d), q);
            Gable(roofs[lot.RoofTint % roofs.Length], wall, LP(lot, 0, h, 0), q, w + 0.6f, d + 0.6f, rise);
            for (int f = third ? 3 : stairs ? 2 : 1; f < lot.Floors; f++)
            {
                int n = Mathf.Max(2, Mathf.FloorToInt(w / 2.4f));
                for (int i = 0; i < n; i++)
                {
                    float x = -w * 0.5f + w * (i + 0.5f) / n, y = f * H + 1.6f;
                    Box(dark, LP(lot, x, y, hz + 0.02f), new Vector3(0.6f, 0.75f, 0.06f), q);
                    Box(darkWood, LP(lot, x - 0.42f, y, hz + 0.05f), new Vector3(0.22f, 0.8f, 0.05f), q);
                    Box(darkWood, LP(lot, x + 0.42f, y, hz + 0.05f), new Vector3(0.22f, 0.8f, 0.05f), q);
                }
            }
            current = null;
            var upper = new GameObject("Upper_" + lot.Id);
            upper.transform.SetParent(root, false);
            foreach (var kv in mine)
                PS2Spawn.Mesh(upper.transform, "Top_" + kv.Key.name, kv.Value.Build("HouseTop"), Vector3.zero, kv.Key);
            LowPolyMesh.BakeAmbient(upper, AmbientSky, AmbientGround);
            float top = h + rise;
            // the roof's eaves stick out 0.3 m: the camera must count them too
            PS2Spawn.BoxCollider(upper.transform, LP(lot, 0, (topBase + 0.2f + top) * 0.5f, 0), new Vector3(w + 0.6f, top - topBase - 0.2f, d + 0.6f), q);

            ground.Upper = upper;
            ground.Hide.Add(upper);
            ground.Public = !lot.IsHome; // a shop: walking in is not trespassing
            if (midGo != null) ground.Hide.Add(midGo);
            if (topGo != null) ground.Hide.Add(topGo);
            Houses.Add(ground);
            if (upperHouse != null)
            {
                upperHouse.Upper = upper;
                upperHouse.Hide.Add(upper);
                if (topGo != null) upperHouse.Hide.Add(topGo);
                Houses.Add(upperHouse);
            }
            if (topHouse != null)
            {
                topHouse.Upper = upper;
                topHouse.Hide.Add(upper);
                Houses.Add(topHouse);
            }

            // ---- for sale: a board on a post by the door
            if (lot.ForSale)
            {
                Vector3 at = LP(lot, lot.DoorOffset * w + 1.5f, 0, hz + 1.1f);
                var sign = new GameObject("SeVende_" + lot.Id).transform;
                sign.SetParent(root, false);
                sign.SetPositionAndRotation(at, q);
                PS2Spawn.Box(sign, "Post", new Vector3(0.1f, 1.6f, 0.1f), new Vector3(0, 0.8f, 0), darkWood);
                PS2Spawn.Box(sign, "Board", new Vector3(0.9f, 0.55f, 0.06f), new Vector3(0, 1.45f, 0.06f), PS2Materials.Get("sale_board", PS2Textures.Wood, new Color(0.95f, 0.85f, 0.6f)));
                PS2Spawn.Box(sign, "Stripe", new Vector3(0.75f, 0.16f, 0.08f), new Vector3(0, 1.45f, 0.08f), PS2Materials.Flat(new Color(0.75f, 0.1f, 0.08f), 0.3f));
                LowPolyMesh.BakeAmbient(sign.gameObject, AmbientSky, AmbientGround);
                ground.SaleSign = sign.gameObject;
            }
        }

        /// <summary>
        /// The infirmary (valetudinarium): one long hall you can walk into, ten beds in two rows (straw mattress,
        /// pillow, blanket), a table with the medicus' bowls and jars, two lamps. Its roof hides while you are inside.
        /// Beaten gladiators wake up in one of its beds.
        /// </summary>
        void Infirmary(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth, H = 3.6f, t = 0.25f;
            float hx = w * 0.5f, hz = d * 0.5f;
            Material wall = plaster[0];
            var go = new GameObject("Infirmary_" + lot.Id);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(lot.Center, q);
            var house = go.AddComponent<HouseInterior>();
            house.Lot = lot; house.HalfX = hx; house.HalfZ = hz; house.Floor = 0; house.Public = true;
            house.Rooms.Add(new HouseInterior.Room { Name = "ENFERMERIA", X0 = -hx, X1 = hx, Z0 = -hz, Z1 = hz });

            void Seg(float x0, float x1, float z0, float z1, float ya, float yb)
            {
                if (x1 - x0 < 0.05f || z1 - z0 < 0.02f || yb - ya < 0.02f) return;
                var size = new Vector3(x1 - x0, yb - ya, z1 - z0);
                Vector3 c = LP(lot, (x0 + x1) * 0.5f, (ya + yb) * 0.5f, (z0 + z1) * 0.5f);
                Box(wall, c, size, q);
                Collider(c, size, q);
                if (ya <= 0.01f) Box(stoneDark, LP(lot, (x0 + x1) * 0.5f, 0.35f, (z0 + z1) * 0.5f), new Vector3(size.x + 0.1f, 0.7f, size.z + 0.1f), q);
            }
            float dw = 0.9f;
            Seg(-hx, hx, -hz, -hz + t, 0, H);                  // back
            Seg(-hx, -hx + t, -hz, hz, 0, H);                  // sides
            Seg(hx - t, hx, -hz, hz, 0, H);
            Seg(-hx, -dw, hz - t, hz, 0, H);                   // front with a wide doorway in the middle
            Seg(dw, hx, hz - t, hz, 0, H);
            Seg(-dw, dw, hz - t, hz, 2.6f, H);
            Box(darkWood, LP(lot, 0, 2.66f, hz + 0.05f), new Vector3(dw * 2f + 0.3f, 0.18f, 0.14f), q);
            // a green cloth sign over the door
            Box(darkWood, LP(lot, 0, 3.25f, hz + 0.45f), new Vector3(0.08f, 0.08f, 0.9f), q);
            Box(PS2Materials.Get("sign_infirmary", PS2Textures.Cloth, new Color(0.2f, 0.5f, 0.3f)), LP(lot, 0, 2.95f, hz + 0.75f), new Vector3(1.4f, 0.5f, 0.06f), q);
            // windows high on the back wall
            for (int i = 0; i < 4; i++)
            {
                float x = -hx + w * (i + 0.5f) / 4f;
                Box(dark, LP(lot, x, 2.6f, -hz - 0.02f), new Vector3(0.8f, 0.6f, 0.06f), q);
            }
            // floor: light stone tiles
            var tiles = PS2Materials.Get("infirmary_floor", PS2Textures.Stone, new Color(0.85f, 0.82f, 0.74f));
            Box(tiles, LP(lot, 0, 0.03f, 0), new Vector3(w - 2f * t, 0.06f, d - 2f * t), q, 0.5f);

            // beds: six along the back wall, four by the front (either side of the door), heads to the wall
            var mattress = PS2Materials.Get("mattress", PS2Textures.Cloth, new Color(0.88f, 0.82f, 0.62f));
            var pillowM = PS2Materials.Get("pillow", PS2Textures.Cloth, new Color(0.95f, 0.93f, 0.88f));
            var blanketM = PS2Materials.Get("blanket_inf", PS2Textures.Cloth, new Color(0.62f, 0.12f, 0.1f));
            void Bed(float x, int headSide) // headSide -1 = head at the back wall, +1 = head at the front wall
            {
                float zHead = headSide < 0 ? -hz + t + 0.05f : hz - t - 0.05f;
                float zc = zHead - headSide * 1.0f;
                Box(darkWood, LP(lot, x, 0.22f, zc), new Vector3(0.95f, 0.36f, 2.0f), q);
                Collider(LP(lot, x, 0.25f, zc), new Vector3(0.95f, 0.5f, 2.0f), q);
                Box(mattress, LP(lot, x, 0.45f, zc), new Vector3(0.85f, 0.12f, 1.9f), q);
                Box(pillowM, LP(lot, x, 0.55f, zHead - headSide * 0.25f), new Vector3(0.6f, 0.1f, 0.32f), q);
                Box(blanketM, LP(lot, x, 0.53f, zc - headSide * 0.42f), new Vector3(0.88f, 0.05f, 1.05f), q);
                // where a patient lies: hips about 0.78 m from the head end, looking to the foot of the bed
                Vector3 at = LP(lot, x, 0.52f, zHead - headSide * 0.78f);
                var bedPose = new Pose(at, q * Quaternion.LookRotation(new Vector3(0, 0, -headSide)));
                house.BedSpots.Add(bedPose);
                house.Furniture.Add(new FurnitureSpot(Gladiator.Combat.Fighter.ActLie, bedPose, InfirmaryStand(bedPose)));
            }
            for (int i = 0; i < 6; i++) Bed(-hx + 1.2f + i * (w - 2.4f) / 5f, -1);
            foreach (float x in new[] { -hx + 1.2f, -hx + 2.6f, hx - 2.6f, hx - 1.2f }) Bed(x, 1);
            // the medicus' table in the middle: jars and rolled bandages
            Box(wood, LP(lot, 0, 0.78f, 0.3f), new Vector3(2.2f, 0.08f, 0.9f), q);
            foreach (var lx in new[] { -1f, 1f }) foreach (var lz in new[] { -0.35f, 0.35f })
                Box(darkWood, LP(lot, lx * 1.0f, 0.38f, 0.3f + lz), new Vector3(0.08f, 0.76f, 0.08f), q);
            Collider(LP(lot, 0, 0.4f, 0.3f), new Vector3(2.2f, 0.8f, 0.9f), q);
            for (int k = 0; k < 4; k++)
            {
                Box(clay, LP(lot, -0.8f + k * 0.5f, 0.92f, 0.2f + (k % 2) * 0.25f), new Vector3(0.18f, 0.22f, 0.18f), q);
                Box(pillowM, LP(lot, -0.6f + k * 0.4f, 0.86f, 0.55f), new Vector3(0.22f, 0.08f, 0.08f), q);
            }
            // two lamps (on while the player is inside)
            foreach (float lx in new[] { -hx * 0.5f, hx * 0.5f })
            {
                var lamp = new GameObject("InfirmaryLamp").AddComponent<Light>();
                lamp.transform.SetParent(go.transform, false);
                lamp.transform.position = LP(lot, lx, 2.6f, 0f);
                lamp.type = LightType.Point; lamp.range = 7f; lamp.intensity = 1.2f; lamp.color = new Color(1f, 0.75f, 0.45f);
                lamp.shadows = LightShadows.None;
                lamp.enabled = false;
                house.Lights.Add(lamp);
                Box(ember, LP(lot, lx, 2.9f, 0f), new Vector3(0.12f, 0.12f, 0.12f), q);
            }

            // ceiling + roof: their own object, hidden while you are inside
            var mine = new Dictionary<Material, MeshBuilder>();
            current = mine;
            Box(wall, LP(lot, 0, H + 0.075f, 0), new Vector3(w, 0.15f, d), q);
            float rise = 1.6f;
            Gable(roofs[lot.RoofTint % roofs.Length], wall, LP(lot, 0, H, 0), q, w + 0.6f, d + 0.6f, rise);
            current = null;
            var upper = new GameObject("Upper_" + lot.Id);
            upper.transform.SetParent(root, false);
            foreach (var kv in mine) PS2Spawn.Mesh(upper.transform, "Top_" + kv.Key.name, kv.Value.Build("InfirmaryTop"), Vector3.zero, kv.Key);
            LowPolyMesh.BakeAmbient(upper, AmbientSky, AmbientGround);
            PS2Spawn.BoxCollider(upper.transform, LP(lot, 0, H + 0.2f + rise * 0.5f, 0), new Vector3(w + 0.6f, rise, d + 0.6f), q);
            house.Upper = upper;
            house.Hide.Add(upper);
            Houses.Add(house);
        }

        /// <summary>Getting out of an infirmary bed: on the floor at its foot.</summary>
        static Vector3 InfirmaryStand(Pose bed)
        {
            Vector3 v = bed.position + bed.rotation * Vector3.forward * 1.65f;
            v.y = bed.position.y - 0.52f + 0.05f;
            return v;
        }

        /// <summary>Where the next storey's side door goes (local z); NaN = over the first stair's landing, near the front.</summary>
        float sideDoorZ = float.NaN;

        // Where Collider() puts its boxes (null = the town's static colliders).
        Transform colTarget;

        /// <summary>
        /// One storey of a home: outer walls (front door on the ground floor, a side door at the top of the stairs on the
        /// first), the kitchen / bedroom / bathroom partition, the floors, the windows and the door on its hinge.
        /// Walls go into the current batch; returns the storey's interior (rooms, door).
        /// </summary>
        HouseInterior Storey(TownLot lot, int level, Material wall, bool sideDoor, int ss)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth, H = 3.1f, t = 0.22f, pt = 0.15f;
            float hx = w * 0.5f, hz = d * 0.5f, y0 = level * H, y1 = y0 + H;

            var go = new GameObject((level == 0 ? "House_" : "House1_") + lot.Id);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(lot.Center + Vector3.up * y0, q);
            var house = go.AddComponent<HouseInterior>();
            house.Lot = lot; house.HalfX = hx; house.HalfZ = hz; house.Floor = level;

            void Seg(float x0, float x1, float z0, float z1, float ya, float yb, bool outside)
            {
                if (x1 - x0 < 0.05f || z1 - z0 < 0.02f || yb - ya < 0.02f) return;
                var size = new Vector3(x1 - x0, yb - ya, z1 - z0);
                Vector3 c = LP(lot, (x0 + x1) * 0.5f, (ya + yb) * 0.5f, (z0 + z1) * 0.5f);
                Box(wall, c, size, q);
                Collider(c, size, q);
                if (outside && ya <= 0.01f)
                    Box(stoneDark, LP(lot, (x0 + x1) * 0.5f, 0.35f, (z0 + z1) * 0.5f), new Vector3(size.x + 0.1f, 0.7f, size.z + 0.1f), q);
            }
            float dw = 0.6f;
            float dx = lot.DoorOffset * w;                  // front door (ground floor)
            // front windows: real openings in the wall with a pane of glass (you see in and out)
            var wins = new List<float>();
            int nwin = level == 0 ? 2 : Mathf.Max(2, Mathf.FloorToInt(w / 2.4f));
            float wy = y0 + (level == 0 ? 2.0f : 1.6f);
            const float winW = 0.6f, winH = 0.75f;
            for (int i = 0; i < nwin; i++)
            {
                float x = -w * 0.5f + w * (i + 0.5f) / nwin;
                if (!sideDoor && Mathf.Abs(x - dx) < 1.4f) continue;
                wins.Add(x);
            }
            // a stretch of the front wall, with the window openings left out
            void Front(float x0, float x1, float ya, float yb, bool outside)
            {
                float cur = x0;
                foreach (float wx in wins)
                {
                    float a = wx - winW * 0.5f, b = wx + winW * 0.5f;
                    if (a < cur + 0.05f || b > x1 - 0.05f) continue;
                    Seg(cur, a, hz - t, hz, ya, yb, outside);
                    Seg(a, b, hz - t, hz, ya, wy - winH * 0.5f, outside);
                    Seg(a, b, hz - t, hz, wy + winH * 0.5f, yb, false);
                    cur = b;
                }
                Seg(cur, x1, hz - t, hz, ya, yb, outside);
            }
            float dz = float.IsNaN(sideDoorZ) ? hz - 1.3f : sideDoorZ; // side door (upper floors), over the stair landing
            float sideX0 = ss > 0 ? hx - t : -hx, sideX1 = ss > 0 ? hx : -hx + t;
            float otherX0 = ss > 0 ? -hx : hx - t, otherX1 = ss > 0 ? -hx + t : hx;
            Seg(-hx, hx, -hz, -hz + t, y0, y1, true);                                   // back
            Seg(otherX0, otherX1, -hz, hz, y0, y1, true);                               // side without the stairs
            if (sideDoor)
            {
                Seg(sideX0, sideX1, -hz, dz - dw, y0, y1, true);                       // stair side, behind the door
                Seg(sideX0, sideX1, dz + dw, hz, y0, y1, true);                        // stair side, in front of the door
                Seg(sideX0, sideX1, dz - dw, dz + dw, y0 + 2.3f, y1, false);           // over the door
                Front(-hx, hx, y0, y1, true);                                           // front (no door)
            }
            else
            {
                Seg(sideX0, sideX1, -hz, hz, y0, y1, true);
                Front(-hx, dx - dw, y0, y1, true);                                      // front, left of the door
                Front(dx + dw, hx, y0, y1, true);                                       // front, right of the door
                Seg(dx - dw, dx + dw, hz - t, hz, y0 + 2.3f, y1, false);                // over the door
                Box(darkWood, LP(lot, dx, y0 + 2.36f, hz + 0.05f), new Vector3(1.5f, 0.16f, 0.12f), q);
            }

            // rooms. A normal home: kitchen in front; bedroom + bathroom at the back. A large home: kitchen in front, a
            // hallway down the middle with a 2 x 2 m storeroom at its end, two bedrooms at the back and two bathrooms.
            float ix0 = -hx + t, ix1 = hx - t, iz0 = -hz + t, iz1 = hz - t;
            bool large = lot.Large && level == 0;
            float zs, xs = 0f, bedX0 = 0f, bedX1 = 0f, bathX0 = 0f, bathX1 = 0f, bedC = 0f, bathC = 0f;
            const float cx0 = -1f, cx1 = 1f;                 // hallway (large homes)
            float zStore = iz0 + 2f, zb = iz0 + 3.5f;        // storeroom front wall, bedroom | bathroom wall
            if (!large)
            {
                zs = iz0 + (iz1 - iz0) * 0.45f;
                bool bathRight = (lot.DoorOffset < 0f) ^ (level == 1);
                xs = bathRight ? ix1 - (ix1 - ix0) * 0.4f : ix0 + (ix1 - ix0) * 0.4f;
                bedX0 = bathRight ? ix0 : xs; bedX1 = bathRight ? xs : ix1;
                bathX0 = bathRight ? xs : ix0; bathX1 = bathRight ? ix1 : xs;
                bedC = (bedX0 + bedX1) * 0.5f; bathC = (bathX0 + bathX1) * 0.5f;
                var cuts = new List<float> { ix0 };
                foreach (float g in bedC < bathC ? new[] { bedC, bathC } : new[] { bathC, bedC }) { cuts.Add(g - 0.5f); cuts.Add(g + 0.5f); }
                cuts.Add(ix1);
                for (int i = 0; i + 1 < cuts.Count; i += 2) Seg(cuts[i], cuts[i + 1], zs - pt * 0.5f, zs + pt * 0.5f, y0, y1, false);
                Seg(bedC - 0.5f, bedC + 0.5f, zs - pt * 0.5f, zs + pt * 0.5f, y0 + 2.3f, y1, false);
                Seg(bathC - 0.5f, bathC + 0.5f, zs - pt * 0.5f, zs + pt * 0.5f, y0 + 2.3f, y1, false);
                Seg(xs - pt * 0.5f, xs + pt * 0.5f, iz0, zs, y0, y1, false);
                float fy = y0 + (level == 0 ? 0.03f : 0.17f);
                Box(stoneDark, LP(lot, 0, fy, (zs + iz1) * 0.5f), new Vector3(ix1 - ix0, 0.04f, iz1 - zs), q, 0.5f);
                Box(wood, LP(lot, bedC, fy, (iz0 + zs) * 0.5f), new Vector3(bedX1 - bedX0, 0.04f, zs - iz0), q, 0.5f);
                Box(paving, LP(lot, bathC, fy, (iz0 + zs) * 0.5f), new Vector3(bathX1 - bathX0, 0.04f, zs - iz0), q, 0.8f);
                house.PartitionZ = zs; house.BedGapX = bedC; house.BathGapX = bathC;
                house.Rooms.Add(new HouseInterior.Room { Name = "COCINA", X0 = ix0, X1 = ix1, Z0 = zs, Z1 = iz1 });
                house.Rooms.Add(new HouseInterior.Room { Name = "HABITACION", X0 = bedX0, X1 = bedX1, Z0 = iz0, Z1 = zs });
                house.Rooms.Add(new HouseInterior.Room { Name = "BANO", X0 = bathX0, X1 = bathX1, Z0 = iz0, Z1 = zs });
            }
            else
            {
                zs = iz1 - 3.2f;
                float h2 = pt * 0.5f;
                // kitchen | back part: one opening into the hallway
                Seg(ix0, cx0 + 0.4f, zs - h2, zs + h2, y0, y1, false);
                Seg(cx1 - 0.4f, ix1, zs - h2, zs + h2, y0, y1, false);
                Seg(cx0 + 0.4f, cx1 - 0.4f, zs - h2, zs + h2, y0 + 2.3f, y1, false);
                // hallway walls, with an opening into each bedroom and each bathroom
                float gBed = iz0 + 2.75f, gBath = (zb + zs) * 0.5f;
                foreach (float x in new[] { cx0, cx1 })
                {
                    var c = new List<float> { iz0, gBed - 0.45f, gBed + 0.45f, gBath - 0.45f, gBath + 0.45f, zs - h2 };
                    for (int i = 0; i + 1 < c.Count; i += 2) Seg(x - h2, x + h2, c[i], c[i + 1], y0, y1, false);
                    Seg(x - h2, x + h2, gBed - 0.45f, gBed + 0.45f, y0 + 2.3f, y1, false);
                    Seg(x - h2, x + h2, gBath - 0.45f, gBath + 0.45f, y0 + 2.3f, y1, false);
                }
                // storeroom at the end of the hallway (opening in the middle)
                Seg(cx0, -0.45f, zStore - h2, zStore + h2, y0, y1, false);
                Seg(0.45f, cx1, zStore - h2, zStore + h2, y0, y1, false);
                Seg(-0.45f, 0.45f, zStore - h2, zStore + h2, y0 + 2.3f, y1, false);
                // bedroom | bathroom on each side
                Seg(ix0, cx0, zb - h2, zb + h2, y0, y1, false);
                Seg(cx1, ix1, zb - h2, zb + h2, y0, y1, false);
                float fy = y0 + 0.03f;
                Box(stoneDark, LP(lot, 0, fy, (zs + iz1) * 0.5f), new Vector3(ix1 - ix0, 0.04f, iz1 - zs), q, 0.5f);
                Box(wood, LP(lot, 0, fy, (zStore + zs) * 0.5f), new Vector3(cx1 - cx0, 0.04f, zs - zStore), q, 0.5f);
                Box(stoneDark, LP(lot, 0, fy, (iz0 + zStore) * 0.5f), new Vector3(cx1 - cx0, 0.04f, zStore - iz0), q, 0.5f);
                foreach (var (a, b) in new[] { (ix0, cx0), (cx1, ix1) })
                {
                    Box(wood, LP(lot, (a + b) * 0.5f, fy, (iz0 + zb) * 0.5f), new Vector3(b - a, 0.04f, zb - iz0), q, 0.5f);
                    Box(paving, LP(lot, (a + b) * 0.5f, fy, (zb + zs) * 0.5f), new Vector3(b - a, 0.04f, zs - zb), q, 0.8f);
                }
                house.PartitionZ = zs; house.BedGapX = cx0 - 1.6f; house.BathGapX = cx1 + 1.6f;
                house.Rooms.Add(new HouseInterior.Room { Name = "COCINA", X0 = ix0, X1 = ix1, Z0 = zs, Z1 = iz1 });
                house.Rooms.Add(new HouseInterior.Room { Name = "PASILLO", X0 = cx0, X1 = cx1, Z0 = zStore, Z1 = zs });
                house.Rooms.Add(new HouseInterior.Room { Name = "TRASTERO", X0 = cx0, X1 = cx1, Z0 = iz0, Z1 = zStore });
                house.Rooms.Add(new HouseInterior.Room { Name = "HABITACION", X0 = ix0, X1 = cx0, Z0 = iz0, Z1 = zb });
                house.Rooms.Add(new HouseInterior.Room { Name = "BANO", X0 = ix0, X1 = cx0, Z0 = zb, Z1 = zs });
                house.Rooms.Add(new HouseInterior.Room { Name = "HABITACION", X0 = cx1, X1 = ix1, Z0 = iz0, Z1 = zb });
                house.Rooms.Add(new HouseInterior.Room { Name = "BANO", X0 = cx1, X1 = ix1, Z0 = zb, Z1 = zs });
            }

            // ---- furniture (empty houses for sale stay empty, also once bought). At most one bed per bedroom.
            if (!lot.ForSale)
            {
                var hh = Household != null ? Household(lot.Id, level) : (true, 1, 3);
                float fy0 = y0 + (level == 0 ? 0.05f : 0.19f);
                Vector3 P(float x, float y, float z) => LP(lot, x, fy0 + y, z);
                void Solid(Material m, float x, float y, float z, Vector3 size, bool col = true, float yaw = 0f)
                {
                    Quaternion r = q * Quaternion.Euler(0f, yaw, 0f);
                    Box(m, P(x, y, z), size, r);
                    if (col) Collider(P(x, y, z), size, r);
                }
                void Candle(float x, float y, float z)
                {
                    Box(waxMat, P(x, y + 0.08f, z), new Vector3(0.07f, 0.16f, 0.07f), q, 0f);
                    Box(ember, P(x, y + 0.2f, z), new Vector3(0.04f, 0.07f, 0.04f), q * Quaternion.Euler(0, 45f, 0), 0f);
                }
                Material blanket = PS2Materials.Get("blanket_" + (lot.Tint % 3), PS2Textures.Cloth,
                    lot.Tint % 3 == 0 ? new Color(0.62f, 0.12f, 0.1f) : lot.Tint % 3 == 1 ? new Color(0.22f, 0.32f, 0.58f) : new Color(0.55f, 0.45f, 0.25f));

                // a chest (wood with iron bands)
                void Chest(float x, float z)
                {
                    Solid(darkWood, x, 0.25f, z, new Vector3(0.85f, 0.5f, 0.5f));
                    Box(wood, P(x, 0.52f, z), new Vector3(0.88f, 0.08f, 0.53f), q, 0f);
                    foreach (float e in new[] { -0.3f, 0.3f })
                        Box(iron, P(x + e, 0.3f, z), new Vector3(0.06f, 0.56f, 0.55f), q, 0f);
                    Box(iron, P(x, 0.38f, z + 0.26f), new Vector3(0.1f, 0.1f, 0.03f), q, 0f);
                }
                // a standing shelf against a side wall (x), running along z, with jars
                void Shelf(float x, float z, float len)
                {
                    Solid(wood, x, 0.95f, z, new Vector3(0.36f, 1.9f, 0.08f), false);
                    for (int k = 0; k < 4; k++) Box(wood, P(x, 0.15f + k * 0.55f, z), new Vector3(0.38f, 0.05f, len), q, 0f);
                    foreach (float e in new[] { -len * 0.48f, len * 0.48f }) Box(darkWood, P(x, 0.95f, z + e), new Vector3(0.38f, 1.9f, 0.05f), q, 0f);
                    int n = Mathf.Max(2, Mathf.FloorToInt(len / 0.38f));
                    for (int k = 0; k < 3; k++)
                        for (int j = 0; j < n; j++)
                            if ((k + j + lot.Id) % 3 != 0)
                                Box(clay, P(x, 0.32f + k * 0.55f, z - len * 0.5f + (j + 0.5f) * len / n), new Vector3(0.2f, 0.26f, 0.2f), q * Quaternion.Euler(0, 45f, 0), 0f);
                    Collider(P(x, 0.95f, z), new Vector3(0.4f, 1.9f, len + 0.05f), q);
                }
                // a bedroom: ONE bed with its head on the back wall (z0) at the far side, a chest of drawers with a candle,
                // and (normal homes, which have no storeroom) a chest at the foot of the bed
                void Bedroom(float x0, float x1, float z0, float z1, int farSide, bool dbl, bool chest)
                {
                    float width = dbl ? 1.5f : 0.9f;
                    float cx = farSide < 0 ? x0 + 0.12f + width * 0.5f : x1 - 0.12f - width * 0.5f, bz = z0 + 1.05f;
                    Solid(darkWood, cx, 0.2f, bz, new Vector3(width, 0.4f, 2.0f));
                    Box(darkWood, P(cx, 0.55f, z0 + 0.08f), new Vector3(width, 0.7f, 0.08f), q, 0f);
                    Box(awningA, P(cx, 0.46f, bz), new Vector3(width - 0.1f, 0.14f, 1.9f), q, 0f);
                    Box(blanket, P(cx, 0.55f, bz + 0.25f), new Vector3(width - 0.06f, 0.05f, 1.3f), q, 0f);
                    for (int k = 0; k < (dbl ? 2 : 1); k++)
                        Box(awningA, P(cx + (dbl ? (k == 0 ? -0.35f : 0.35f) : 0f), 0.58f, z0 + 0.38f), new Vector3(0.5f, 0.1f, 0.32f), q, 0f);
                    house.Beds.Add(cx + "," + bz);
                    // a single bed sleeps one; a double bed two, one on each side
                    foreach (float lx in dbl ? new[] { cx - farSide * 0.35f, cx + farSide * 0.35f } : new[] { cx })
                        house.Furniture.Add(new FurnitureSpot(Gladiator.Combat.Fighter.ActLie,
                            new Pose(P(lx, 0.53f, z0 + 0.83f), q * Quaternion.LookRotation(Vector3.forward))));
                    if (chest && z0 + 2.7f < z1 - 0.1f) Chest(cx, z0 + 2.4f);
                    // chest of drawers on the back wall at the other end, if it fits; candle on it
                    float room = (x1 - x0) - 0.24f - width - 0.15f;
                    if (room >= 1.1f)
                    {
                        float dxD = farSide < 0 ? x1 - 0.12f - 0.55f : x0 + 0.12f + 0.55f;
                        Solid(wood, dxD, 0.45f, z0 + 0.3f, new Vector3(1.1f, 0.9f, 0.48f));
                        for (int k = 0; k < 3; k++)
                            Box(darkWood, P(dxD, 0.18f + k * 0.27f, z0 + 0.55f), new Vector3(1.0f, 0.03f, 0.02f), q, 0f);
                        Candle(dxD + 0.35f, 0.9f, z0 + 0.3f);
                    }
                    else if (chest && z0 + 2.7f < z1 - 0.1f) Candle(cx + 0.25f, 0.56f, z0 + 2.4f);
                }
                // a bathroom. Doorway in the z1 wall (normal homes): the tub along the back wall z0, the latrine by the far
                // side near z1. Doorway in a side wall (large homes, doorSide = -1 at x0 / +1 at x1): the tub along the
                // opposite side wall, the latrine against the z1 wall between the tub and the doorway. Candle on the tub.
                void Bathroom(float x0, float x1, float z0, float z1, int farSide, int doorSide = 0)
                {
                    if (doorSide != 0)
                    {
                        float dpt = z1 - z0, wdt = x1 - x0;
                        float tL = Mathf.Max(1.4f, dpt - 0.25f), tW = Mathf.Min(1.3f, wdt - 1.7f);
                        if (tW < 0.9f) tW = 0.9f;
                        float tx = doorSide > 0 ? x0 + 0.1f + tW * 0.5f : x1 - 0.1f - tW * 0.5f, tzc = (z0 + z1) * 0.5f;
                        Box(stone, P(tx - tW * 0.5f + 0.08f, 0.3f, tzc), new Vector3(0.16f, 0.6f, tL), q);
                        Box(stone, P(tx + tW * 0.5f - 0.08f, 0.3f, tzc), new Vector3(0.16f, 0.6f, tL), q);
                        Box(stone, P(tx, 0.3f, tzc - tL * 0.5f + 0.08f), new Vector3(tW, 0.6f, 0.16f), q);
                        Box(stone, P(tx, 0.3f, tzc + tL * 0.5f - 0.08f), new Vector3(tW, 0.6f, 0.16f), q);
                        Box(stone, P(tx, 0.05f, tzc), new Vector3(tW, 0.1f, tL), q);
                        Box(water, P(tx, 0.42f, tzc), new Vector3(tW - 0.3f, 0.03f, tL - 0.3f), q, 0f);
                        Collider(P(tx, 0.3f, tzc), new Vector3(tW, 0.6f, tL), q);
                        Candle(tx, 0.6f, tzc + tL * 0.5f - 0.1f);
                        // latrine along the z1 wall, between the tub and the doorway
                        float free0 = doorSide > 0 ? x0 + 0.2f + tW : x0 + 0.1f, free1 = doorSide > 0 ? x1 - 0.1f : x1 - 0.2f - tW;
                        if (free1 - free0 >= 0.95f)
                        {
                            float lxc = (free0 + free1) * 0.5f, lzc = z1 - 0.35f;
                            Solid(stone, lxc, 0.22f, lzc, new Vector3(0.9f, 0.44f, 0.6f));
                            Box(dark, P(lxc, 0.445f, lzc), new Vector3(0.3f, 0.01f, 0.24f), q, 0f);
                            Box(water, P(lxc, 0.03f, lzc - 0.42f), new Vector3(0.9f, 0.03f, 0.14f), q, 0f);
                        }
                        return;
                    }
                    float bw = x1 - x0, depth = z1 - z0;
                    float tubW = Mathf.Max(1.4f, bw - 0.3f), tubD = Mathf.Min(1.35f, depth - 1.2f);
                    if (tubD < 0.9f) tubD = 0.9f;
                    float tcx = (x0 + x1) * 0.5f, tcz = z0 + 0.1f + tubD * 0.5f;
                    Box(stone, P(tcx, 0.3f, tcz - tubD * 0.5f + 0.08f), new Vector3(tubW, 0.6f, 0.16f), q);
                    Box(stone, P(tcx, 0.3f, tcz + tubD * 0.5f - 0.08f), new Vector3(tubW, 0.6f, 0.16f), q);
                    Box(stone, P(tcx - tubW * 0.5f + 0.08f, 0.3f, tcz), new Vector3(0.16f, 0.6f, tubD), q);
                    Box(stone, P(tcx + tubW * 0.5f - 0.08f, 0.3f, tcz), new Vector3(0.16f, 0.6f, tubD), q);
                    Box(stone, P(tcx, 0.05f, tcz), new Vector3(tubW, 0.1f, tubD), q);
                    Box(water, P(tcx, 0.42f, tcz), new Vector3(tubW - 0.3f, 0.03f, tubD - 0.3f), q, 0f);
                    Collider(P(tcx, 0.3f, tcz), new Vector3(tubW, 0.6f, tubD), q);
                    Candle(tcx + tubW * 0.5f - 0.1f, 0.6f, tcz + tubD * 0.5f - 0.1f);
                    float lx = farSide < 0 ? x0 + 0.3f : x1 - 0.3f, lz = z1 - 0.6f;
                    if (lz - 0.45f > tcz + tubD * 0.5f + 0.1f)
                    {
                        Solid(stone, lx, 0.22f, lz, new Vector3(0.6f, 0.44f, 0.9f));
                        Box(dark, P(lx, 0.445f, lz), new Vector3(0.24f, 0.01f, 0.3f), q, 0f);
                        Box(water, P(lx + (farSide < 0 ? 0.42f : -0.42f), 0.03f, lz), new Vector3(0.14f, 0.03f, 0.9f), q, 0f);
                    }
                }

                // KITCHEN: a masonry cooking range along the whole far side wall (the door is never there): an oven at the
                // front end, a fire on the worktop with a pot, jars, and a shelf hanging above it. Table with chairs, a
                // standing shelf on the other wall (normal homes; large ones keep their shelves in the storeroom).
                float f = sideDoor ? -ss : (dx >= 0f ? -1f : 1f);
                float xFar = f > 0 ? ix1 : ix0, xNear = f > 0 ? ix0 : ix1;
                float kMid = (zs + iz1) * 0.5f;
                {
                    const float cd = 0.72f;                                   // depth of the range
                    float rx = xFar - f * cd * 0.5f, rz0 = zs + 0.1f, rz1 = iz1 - 0.05f, rLen = rz1 - rz0;
                    // worktop body + top slab + a dark band (the fire boxes underneath)
                    Solid(stone, rx, 0.45f, (rz0 + rz1) * 0.5f, new Vector3(cd, 0.9f, rLen));
                    Box(stoneDark, P(rx, 0.93f, (rz0 + rz1) * 0.5f), new Vector3(cd + 0.04f, 0.06f, rLen + 0.04f), q, 0f);
                    Box(dark, P(rx - f * (cd * 0.5f + 0.005f), 0.3f, rz0 + rLen * 0.35f), new Vector3(0.01f, 0.32f, rLen * 0.4f), q, 0f);
                    // the oven at the front end: a block with a dark arched mouth, embers inside and a chimney
                    float oz = rz1 - 0.55f;
                    Solid(stoneDark, rx, 0.75f, oz, new Vector3(cd + 0.06f, 1.5f, 1.05f));
                    Box(clay, P(rx, 1.6f, oz), new Vector3(cd - 0.04f, 0.22f, 0.95f), q, 0f);
                    Box(dark, P(rx - f * (cd * 0.5f + 0.04f), 0.95f, oz), new Vector3(0.02f, 0.5f, 0.5f), q, 0f);
                    Box(ember, P(rx - f * (cd * 0.5f + 0.045f), 0.78f, oz), new Vector3(0.02f, 0.12f, 0.42f), q, 0f);
                    Box(stoneDark, P(xFar - f * 0.25f, 2.4f, oz), new Vector3(0.4f, 1.6f, 0.4f), q);                 // chimney up the wall
                    // the fire on the worktop with a pot over it (its light: on only while you are in this storey)
                    float fz = rz0 + Mathf.Min(1.2f, rLen * 0.4f);
                    Box(ember, P(rx, 0.97f, fz), new Vector3(0.45f, 0.03f, 0.45f), q, 0f);
                    Box(iron, P(rx, 1.12f, fz), new Vector3(0.36f, 0.26f, 0.36f), q * Quaternion.Euler(0, 45f, 0), 0f);
                    var fire = new GameObject("CookingFire").transform;
                    fire.SetParent(house.transform, false);
                    fire.position = P(rx, 0.97f, fz);
                    var fl = new GameObject("Flame").transform;
                    fl.SetParent(fire, false);
                    PS2Spawn.Mesh(fl, "F", LowPolyMesh.Box(new Vector3(0.18f, 0.16f, 0.18f), new Vector3(0, 0.08f, 0)), new Vector3(0.12f, 0f, 0.1f), flameMat, Quaternion.Euler(0, 45f, 0));
                    var li = new GameObject("KitchenLight").AddComponent<Light>();
                    li.transform.SetParent(fire, false);
                    li.transform.localPosition = new Vector3(0, 1.0f, 0);
                    li.type = LightType.Point; li.range = 5.5f; li.intensity = 1.4f; li.color = new Color(1f, 0.6f, 0.28f);
                    li.shadows = LightShadows.None;
                    li.enabled = false;
                    var tf = fire.gameObject.AddComponent<Gladiator.Arena.TorchFlicker>();
                    tf.Light = li; tf.Flame = fl; tf.BaseIntensity = li.intensity;
                    house.Lights.Add(li);
                    // jars on the worktop
                    for (int k = 0; k < 3; k++)
                    {
                        float jz = fz + 0.55f + k * 0.32f;
                        if (jz > oz - 0.65f) break;
                        Box(clay, P(rx + f * 0.1f, 1.05f, jz), new Vector3(0.18f, 0.22f, 0.18f), q * Quaternion.Euler(0, 45f, 0), 0f);
                    }
                    // the hanging shelf above the worktop, on the same wall (away from any door)
                    float hz1 = Mathf.Min(oz - 0.75f, fz + 1.4f), hz0 = Mathf.Max(rz0 + 0.1f, hz1 - 1.2f);
                    if (hz1 - hz0 > 0.5f)
                    {
                        float hcz = (hz0 + hz1) * 0.5f, hxw = xFar - f * 0.15f;
                        Box(wood, P(hxw, 1.75f, hcz), new Vector3(0.28f, 0.05f, hz1 - hz0), q, 0f);
                        foreach (float e in new[] { -0.45f, 0.45f })
                            Box(darkWood, P(hxw + f * 0.02f, 1.62f, hcz + e * (hz1 - hz0) / 1.2f), new Vector3(0.18f, 0.25f, 0.05f), q, 0f);
                        for (int k = 0; k < 3; k++)
                            Box(k == 1 ? iron : clay, P(hxw, 1.87f, hz0 + (k + 0.5f) * (hz1 - hz0) / 3f), new Vector3(0.16f, 0.2f, 0.16f), q, 0f);
                    }

                    // table with chairs (one per person, 2..4), candle and a jar on it, clear of the range
                    float tx = xFar - f * 2.35f, tz = kMid - 0.35f;
                    Solid(wood, tx, 0.74f, tz, new Vector3(1.25f, 0.07f, 0.72f));
                    foreach (float sx in new[] { -0.52f, 0.52f })
                        foreach (float sz in new[] { -0.28f, 0.28f })
                            Box(darkWood, P(tx + sx, 0.37f, tz + sz), new Vector3(0.07f, 0.74f, 0.07f), q, 0f);
                    int chairs = Mathf.Clamp(hh.Item3, 2, 4);
                    for (int c = 0; c < chairs; c++)
                    {
                        float cx = tx + (c % 2 == 0 ? -0.32f : 0.32f), side = c < 2 ? -1f : 1f;
                        if (chairs == 2) { cx = tx; side = c == 0 ? -1f : 1f; }
                        float cz = tz + side * 0.62f;
                        Solid(darkWood, cx, 0.44f, cz, new Vector3(0.4f, 0.06f, 0.4f), false);
                        foreach (float lx in new[] { -0.16f, 0.16f })
                            foreach (float lz in new[] { -0.16f, 0.16f })
                                Box(darkWood, P(cx + lx, 0.22f, cz + lz), new Vector3(0.05f, 0.44f, 0.05f), q, 0f);
                        Box(darkWood, P(cx, 0.75f, cz + side * 0.18f), new Vector3(0.4f, 0.55f, 0.05f), q, 0f);
                        Collider(P(cx, 0.4f, cz), new Vector3(0.4f, 0.8f, 0.4f), q);
                        house.Furniture.Add(new FurnitureSpot(Gladiator.Combat.Fighter.ActChair,
                            new Pose(P(cx, 0.02f, cz), q * Quaternion.LookRotation(new Vector3(0, 0, -side)))));
                    }
                    Candle(tx + 0.25f, 0.78f, tz);
                    Box(clay, P(tx - 0.25f, 0.84f, tz + 0.05f), new Vector3(0.16f, 0.16f, 0.16f), q * Quaternion.Euler(0, 45f, 0), 0f);
                    if (!large) Shelf(xNear + f * 0.22f, zs + 0.95f, 1.2f);
                }

                if (!large)
                {
                    bool outerLeft = bedX0 <= ix0 + 0.01f, outerLeftB = bathX0 <= ix0 + 0.01f;
                    Bedroom(bedX0, bedX1, iz0, zs, outerLeft ? -1 : 1, hh.Item1, true);
                    Bathroom(bathX0, bathX1, iz0, zs, outerLeftB ? -1 : 1);
                }
                else
                {
                    // two bedrooms (the couple's double bed in one, a single bed in the other), two bathrooms, the storeroom
                    Bedroom(ix0, cx0, iz0, zb, -1, hh.Item1, false);
                    Bedroom(cx1, ix1, iz0, zb, 1, false, false);
                    Bathroom(ix0, cx0, zb, zs, -1, 1);     // doorway in the hallway wall (x1 side of the left bathroom)
                    Bathroom(cx1, ix1, zb, zs, 1, -1);
                    // storeroom 2 x 2: shelves along both side walls, the chest against the back wall, a candle on it
                    Shelf(cx0 + 0.27f, iz0 + 0.95f, 1.2f);
                    Shelf(cx1 - 0.27f, iz0 + 0.95f, 1.2f);
                    Chest(0f, iz0 + 0.35f);
                    Candle(0.25f, 0.56f, iz0 + 0.35f);
                }
            }


            // front windows: the glass in the opening, a wooden cross and sill, the shutters open outside
            foreach (float x in wins)
            {
                float zc = hz - t * 0.5f;
                Box(glass, LP(lot, x, wy, zc), new Vector3(winW, winH, 0.02f), q, 0f);
                Box(darkWood, LP(lot, x, wy, zc), new Vector3(winW, 0.04f, 0.05f), q, 0f);
                Box(darkWood, LP(lot, x, wy, zc), new Vector3(0.04f, winH, 0.05f), q, 0f);
                Box(wood, LP(lot, x, wy - winH * 0.5f - 0.02f, zc), new Vector3(winW + 0.12f, 0.05f, t + 0.1f), q, 0f);
                Box(darkWood, LP(lot, x - 0.42f, wy, hz + 0.05f), new Vector3(0.22f, 0.8f, 0.05f), q);
                Box(darkWood, LP(lot, x + 0.42f, wy, hz + 0.05f), new Vector3(0.22f, 0.8f, 0.05f), q);
                Collider(LP(lot, x, wy, zc), new Vector3(winW, winH, t), q); // nobody climbs in through it
                house.Windows.Add(LP(lot, x, wy, zc));
            }

            // the door on its hinge (it swings inwards)
            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(go.transform, false);
            Quaternion baseRot = Quaternion.identity;
            if (sideDoor)
            {
                baseRot = Quaternion.Euler(0f, ss > 0 ? 90f : -90f, 0f);
                hinge.localPosition = new Vector3(ss * (hx - t * 0.5f), 0f, ss > 0 ? dz + dw : dz - dw);
            }
            else hinge.localPosition = new Vector3(dx - dw, 0f, hz - t * 0.5f);
            hinge.localRotation = baseRot;
            var panel = PS2Spawn.Box(hinge, "DoorPanel", new Vector3(dw * 2f - 0.04f, 2.26f, 0.08f), new Vector3(dw, 1.15f, 0f), darkWood, null, 0.8f);
            PS2Spawn.Box(panel.transform, "Band", new Vector3(dw * 2f - 0.1f, 0.1f, 0.1f), new Vector3(0, 0.5f, 0f), iron);
            PS2Spawn.Box(panel.transform, "Band2", new Vector3(dw * 2f - 0.1f, 0.1f, 0.1f), new Vector3(0, -0.6f, 0f), iron);
            panel.AddComponent<BoxCollider>().size = new Vector3(dw * 2f - 0.04f, 2.26f, 0.12f);
            LowPolyMesh.BakeAmbient(hinge.gameObject, AmbientSky, AmbientGround);
            var door = go.AddComponent<TownDoor>();
            door.Hinge = hinge; door.Width = dw * 2f; door.BaseRot = baseRot;
            house.Door = door;
            return house;
        }

        /// <summary>
        /// Outside staircase along the stair side: from the ground at the back up to a landing at the first floor, in
        /// front of the side door. A ramp collider under solid steps; a railing on the open side.
        /// </summary>
        void BuildStairs(TownLot lot, int ss, float H, HouseInterior upper, bool toBalcony = false)
        {
            Quaternion q = lot.Rotation;
            float hx = lot.Width * 0.5f, hz = lot.Depth * 0.5f, sw = 1.15f;
            float x0 = ss * hx, xc = ss * (hx + sw * 0.5f), xo = ss * (hx + sw);
            float zBottom = -hz + 0.3f, zLand = hz - 2.1f, zEnd = toBalcony ? hz : hz - 0.5f;
            float run = zLand - zBottom;
            int n = Mathf.Max(10, Mathf.CeilToInt(H / 0.22f));
            for (int i = 0; i < n; i++)
            {
                float top = H * (i + 1) / n, za = zBottom + run * i / n, zb = zBottom + run * (i + 1) / n;
                Box(stone, LP(lot, xc, top * 0.5f, (za + zb) * 0.5f), new Vector3(sw, top, zb - za), q);
            }
            Box(stone, LP(lot, xc, H * 0.5f, (zLand + zEnd) * 0.5f), new Vector3(sw, H, zEnd - zLand), q);   // landing block
            // walking: a ramp over the steps + the landing top
            float len = Mathf.Sqrt(run * run + H * H), ang = Mathf.Atan2(H, run) * Mathf.Rad2Deg;
            Vector3 mid = LP(lot, xc, H * 0.5f - 0.05f, zBottom + run * 0.5f);
            PS2Spawn.BoxCollider(colliders, mid, new Vector3(sw, 0.1f, len), q * Quaternion.Euler(-ang, 0f, 0f));
            Collider(LP(lot, xc, H * 0.5f, (zLand + zEnd) * 0.5f), new Vector3(sw, H, zEnd - zLand), q);
            // railing on the open side: posts + a rail along the slope and the landing
            for (int i = 0; i <= 4; i++)
            {
                float z = zBottom + run * i / 4f, y = H * i / 4f;
                Box(darkWood, LP(lot, xo - ss * 0.05f, y + 0.5f, z), new Vector3(0.08f, 1f, 0.08f), q);
            }
            Box(darkWood, LP(lot, xo - ss * 0.05f, H * 0.5f + 1f, zBottom + run * 0.5f), new Vector3(0.07f, 0.07f, len), q * Quaternion.Euler(-ang, 0f, 0f));
            Box(darkWood, LP(lot, xo - ss * 0.05f, H + 1f, (zLand + zEnd) * 0.5f), new Vector3(0.07f, 0.07f, zEnd - zLand), q);
            Collider(LP(lot, xo - ss * 0.05f, H + 0.5f, (zLand + zEnd) * 0.5f), new Vector3(0.1f, 1f, zEnd - zLand), q);
            if (!toBalcony) Collider(LP(lot, xc, H + 0.5f, zEnd - 0.05f), new Vector3(sw, 1f, 0.1f), q); // end of the landing
            upper.StairBottom = LP(lot, xc, 0.05f, zBottom - 0.6f);
            upper.StairTop = LP(lot, xc, H + 0.05f, hz - 1.3f);
            upper.StairDir = q * Vector3.forward;
        }

        /// <summary>
        /// Three-storey homes: from the first staircase's landing a wooden balcony runs along the front of the house to
        /// the other side, where a second staircase climbs towards the back to a landing in front of the second floor's
        /// side door. Balcony and stair have railings (with colliders) and posts down to the street.
        /// </summary>
        void BuildBalconyAndSecondStairs(TownLot lot, int ss, float H, HouseInterior top)
        {
            Quaternion q = lot.Rotation;
            float hx = lot.Width * 0.5f, hz = lot.Depth * 0.5f, sw = 1.15f, bd = 1.2f;
            float span = hx + sw;                                   // the balcony covers both stair strips
            // balcony floor (top at H), rail along the street and at both ends, posts down to the ground
            Box(wood, LP(lot, 0, H - 0.06f, hz + bd * 0.5f), new Vector3(span * 2f, 0.12f, bd), q);
            Collider(LP(lot, 0, H - 0.25f, hz + bd * 0.5f), new Vector3(span * 2f, 0.5f, bd), q);
            Box(darkWood, LP(lot, 0, H + 1f, hz + bd - 0.05f), new Vector3(span * 2f, 0.07f, 0.07f), q);
            Collider(LP(lot, 0, H + 0.5f, hz + bd - 0.05f), new Vector3(span * 2f, 1f, 0.1f), q);
            for (int i = 0; i <= 6; i++)
            {
                float x = -span + 0.05f + (span * 2f - 0.1f) * i / 6f;
                Box(darkWood, LP(lot, x, H + 0.5f, hz + bd - 0.05f), new Vector3(0.07f, 1f, 0.07f), q);
            }
            foreach (int sgn in new[] { -1, 1 })
            {
                Box(darkWood, LP(lot, sgn * (span - 0.05f), H + 1f, hz + bd * 0.5f), new Vector3(0.07f, 0.07f, bd), q);
                Collider(LP(lot, sgn * (span - 0.05f), H + 0.5f, hz + bd * 0.5f), new Vector3(0.1f, 1f, bd), q);
                Box(darkWood, LP(lot, sgn * (span - 0.1f), H * 0.5f, hz + bd - 0.1f), new Vector3(0.16f, H, 0.16f), q);
                Collider(LP(lot, sgn * (span - 0.1f), H * 0.5f, hz + bd - 0.1f), new Vector3(0.16f, H, 0.16f), q);
            }

            // second staircase: on the other side (-ss), from the balcony (height H, front) up to 2H at the back
            int os = -ss;
            float xc = os * (hx + sw * 0.5f), xo = os * (hx + sw);
            float zStart = hz, zLand = -hz + 2.1f, zEnd = -hz + 0.3f;
            float run = zStart - zLand;
            int n = Mathf.Max(10, Mathf.CeilToInt(H / 0.22f));
            for (int i = 0; i < n; i++)
            {
                float y = H + H * (i + 1) / n, za = zStart - run * i / n, zb = zStart - run * (i + 1) / n;
                Box(stone, LP(lot, xc, y - 0.11f, (za + zb) * 0.5f), new Vector3(sw, 0.22f, za - zb), q);
            }
            // a sloping beam under the steps and two posts holding it up
            float len = Mathf.Sqrt(run * run + H * H), ang = Mathf.Atan2(H, run) * Mathf.Rad2Deg;
            Box(darkWood, LP(lot, xc, H * 1.5f - 0.3f, (zStart + zLand) * 0.5f), new Vector3(sw * 0.8f, 0.25f, len), q * Quaternion.Euler(ang, 0f, 0f));
            foreach (float z in new[] { (zStart + zLand) * 0.5f, zLand })
            {
                float yTop = z == zLand ? 2f * H - 0.3f : H * 1.5f - 0.4f;
                Box(darkWood, LP(lot, xo - os * 0.12f, yTop * 0.5f, z), new Vector3(0.16f, yTop, 0.16f), q);
                Collider(LP(lot, xo - os * 0.12f, yTop * 0.5f, z), new Vector3(0.16f, yTop, 0.16f), q);
            }
            // landing at the second floor
            Box(stone, LP(lot, xc, 2f * H - 0.15f, (zLand + zEnd) * 0.5f), new Vector3(sw, 0.3f, zLand - zEnd), q);
            Collider(LP(lot, xc, 2f * H - 0.15f, (zLand + zEnd) * 0.5f), new Vector3(sw, 0.3f, zLand - zEnd), q);
            // walking: a ramp over the steps
            PS2Spawn.BoxCollider(colliders, LP(lot, xc, H * 1.5f - 0.05f, (zStart + zLand) * 0.5f), new Vector3(sw, 0.1f, len), q * Quaternion.Euler(ang, 0f, 0f));
            // railing on the open side (slope + landing) and at the end of the landing
            Box(darkWood, LP(lot, xo - os * 0.05f, H * 1.5f + 1f, (zStart + zLand) * 0.5f), new Vector3(0.07f, 0.07f, len), q * Quaternion.Euler(ang, 0f, 0f));
            PS2Spawn.BoxCollider(colliders, LP(lot, xo - os * 0.05f, H * 1.5f + 0.5f, (zStart + zLand) * 0.5f), new Vector3(0.1f, 1f, len), q * Quaternion.Euler(ang, 0f, 0f));
            for (int i = 0; i <= 4; i++)
            {
                float z = zStart - run * i / 4f, y = H + H * i / 4f;
                Box(darkWood, LP(lot, xo - os * 0.05f, y + 0.5f, z), new Vector3(0.08f, 1f, 0.08f), q);
            }
            Box(darkWood, LP(lot, xo - os * 0.05f, 2f * H + 1f, (zLand + zEnd) * 0.5f), new Vector3(0.07f, 0.07f, zLand - zEnd), q);
            Collider(LP(lot, xo - os * 0.05f, 2f * H + 0.5f, (zLand + zEnd) * 0.5f), new Vector3(0.1f, 1f, zLand - zEnd), q);
            Collider(LP(lot, xc, 2f * H + 0.5f, zEnd + 0.05f), new Vector3(sw, 1f, 0.1f), q);
            top.StairBottom = LP(lot, xc, H + 0.05f, hz + bd * 0.5f);
            top.StairTop = LP(lot, xc, 2f * H + 0.05f, -hz + 1.3f);
            top.StairDir = q * Vector3.back;
        }

        /// <summary>Open shop front: counter, striped awning.</summary>
        void ShopFront(TownLot lot, Material a, Material b)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, hz = lot.Depth * 0.5f;
            float dx = lot.DoorOffset * w;
            float cx = dx + (dx >= 0f ? -2.1f : 2.1f);   // beside the door, never in front of it
            Box(dark, LP(lot, cx, 1.2f, hz + 0.02f), new Vector3(2.4f, 2.2f, 0.06f), q);
            Box(wood, LP(lot, cx, 0.5f, hz + 0.45f), new Vector3(2.4f, 1.0f, 0.5f), q);
            int strips = 6;
            for (int i = 0; i < strips; i++)
                Box(i % 2 == 0 ? a : b, LP(lot, cx - 1.5f + 3f * (i + 0.5f) / strips, 2.75f, hz + 0.8f), new Vector3(3f / strips, 0.06f, 1.7f), q * Quaternion.Euler(14f, 0, 0), 0f);
            Collider(LP(lot, cx, 0.5f, hz + 0.45f), new Vector3(2.4f, 1f, 0.5f), q);
        }

        void Sign(TownLot lot, Color c)
        {
            Quaternion q = lot.Rotation;
            float hz = lot.Depth * 0.5f, x = lot.DoorOffset * lot.Width + 1.3f;
            Box(darkWood, LP(lot, x, 2.9f, hz + 0.45f), new Vector3(0.08f, 0.08f, 0.9f), q);
            Box(PS2Materials.Get("sign_" + ColorUtility.ToHtmlStringRGB(c), PS2Textures.Cloth, c), LP(lot, x, 2.55f, hz + 0.75f), new Vector3(0.06f, 0.55f, 0.55f), q);
        }

        /// <summary>Where you can buy: a yellow ring on the ground at the door and a tall yellow banner on a pole.</summary>
        void ShopMarker(TownLot lot)
        {
            var b = new MeshBuilder();
            b.AddAnnulus(0.95f, 1.15f, 0.04f, 20, Color.white, 1f);
            var ring = PS2Spawn.Mesh(root, "ShopRing_" + lot.Id, b.Build("ShopRing"), lot.ShopSpot, PS2Materials.Flat(new Color(1f, 0.82f, 0.1f), 0.2f));
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float side = lot.DoorOffset >= 0f ? 1f : -1f;   // the side away from the shed
            Vector3 pole = lot.ShopSpot + lot.Right * (side * 1.2f) - lot.Front * 0.5f;
            var y = PS2Materials.Get("banner_shop", PS2Textures.Cloth, new Color(0.95f, 0.75f, 0.1f));
            Box(darkWood, pole + Vector3.up * 2f, new Vector3(0.12f, 4f, 0.12f), lot.Rotation);
            Box(y, pole + Vector3.up * 3.3f + lot.Right * 0.45f, new Vector3(0.8f, 1.2f, 0.05f), lot.Rotation, 0f);
            Box(iron, pole + Vector3.up * 3.3f + lot.Right * 0.45f + lot.Front * 0.04f, new Vector3(0.35f, 0.12f, 0.03f), lot.Rotation * Quaternion.Euler(0, 0, 45f), 0f);
            Box(iron, pole + Vector3.up * 3.3f + lot.Right * 0.45f + lot.Front * 0.04f, new Vector3(0.35f, 0.12f, 0.03f), lot.Rotation * Quaternion.Euler(0, 0, -45f), 0f);
            Collider(pole + Vector3.up * 1f, new Vector3(0.2f, 2f, 0.2f), lot.Rotation);
        }

        void Amphorae(TownLot lot, int n)
        {
            Quaternion q = lot.Rotation;
            float hz = lot.Depth * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float x = lot.Width * 0.5f + 0.35f + (i / 7) * 0.45f, z = hz - 0.5f - (i % 7) * 0.6f; // along the side wall, outside
                Box(clay, LP(lot, x, 0.32f, z), new Vector3(0.32f, 0.64f, 0.32f), q * Quaternion.Euler(0, 45f, 0), 0f);
                Box(clay, LP(lot, x, 0.7f, z), new Vector3(0.16f, 0.14f, 0.16f), q, 0f);
            }
        }

        void Smithy(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, hz = lot.Depth * 0.5f;
            // open shed with an anvil and a forge glowing
            float ddx = lot.DoorOffset * w;
            float sx = ddx + (ddx >= 0f ? -2.3f : 2.3f);   // the shed stands beside the door
            foreach (float s in new[] { -1.3f, 1.3f })
                Box(wood, LP(lot, sx + s, 1.3f, hz + 2.2f), new Vector3(0.14f, 2.6f, 0.14f), q);
            Box(roofs[lot.RoofTint % roofs.Length], LP(lot, sx, 2.7f, hz + 1.2f), new Vector3(3f, 0.12f, 2.4f), q * Quaternion.Euler(-12f, 0, 0), 0.5f);
            Box(iron, LP(lot, sx - 0.5f, 0.35f, hz + 1.3f), new Vector3(0.35f, 0.7f, 0.3f), q, 0f);
            Box(iron, LP(lot, sx - 0.5f, 0.75f, hz + 1.3f), new Vector3(0.7f, 0.15f, 0.3f), q, 0f);
            Box(stoneDark, LP(lot, sx + 0.7f, 0.45f, hz + 0.9f), new Vector3(1.1f, 0.9f, 0.9f), q);
            Box(ember, LP(lot, sx + 0.7f, 0.92f, hz + 0.9f), new Vector3(0.7f, 0.06f, 0.5f), q, 0f);
            // chimney
            Box(stoneDark, LP(lot, w * 0.3f, lot.Floors * 3.1f + 1.2f, -lot.Depth * 0.2f), new Vector3(0.7f, 2.4f, 0.7f), q);
            Collider(LP(lot, sx + 0.7f, 0.45f, hz + 0.9f), new Vector3(1.1f, 0.9f, 0.9f), q);
        }

        void Oven(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            Vector3 p = LP(lot, -lot.Width * 0.5f - 1.0f, 0, lot.Depth * 0.1f);
            Box(stoneDark, p + Vector3.up * 0.5f, new Vector3(1.6f, 1.0f, 1.6f), q);
            Box(clay, p + Vector3.up * 1.2f, new Vector3(1.3f, 0.5f, 1.3f), q * Quaternion.Euler(0, 45f, 0), 0f);
            Box(ember, p + q * new Vector3(0, 0.5f, 0.81f), new Vector3(0.5f, 0.4f, 0.04f), q, 0f);
            Collider(p + Vector3.up * 0.7f, new Vector3(1.6f, 1.4f, 1.6f), q);
        }

        void Garden(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth;
            Material wall = plaster[lot.Tint % plaster.Length];
            // low wall with a gap at the front
            Box(wall, LP(lot, 0, 0.5f, -d * 0.5f), new Vector3(w, 1f, 0.3f), q);
            Box(wall, LP(lot, -w * 0.5f, 0.5f, 0), new Vector3(0.3f, 1f, d), q);
            Box(wall, LP(lot, w * 0.5f, 0.5f, 0), new Vector3(0.3f, 1f, d), q);
            Box(wall, LP(lot, -w * 0.3f, 0.5f, d * 0.5f), new Vector3(w * 0.4f, 1f, 0.3f), q);
            Box(wall, LP(lot, w * 0.3f, 0.5f, d * 0.5f), new Vector3(w * 0.4f, 1f, 0.3f), q);
            // vegetable rows
            for (int i = 0; i < 4; i++)
                Box(foliage, LP(lot, -w * 0.3f + i * w * 0.2f, 0.18f, -d * 0.15f), new Vector3(0.5f, 0.36f, d * 0.5f), q, 0.5f);
            L.Trees.Add(LP(lot, w * 0.32f, 0, -d * 0.32f));
            // the low walls only: you walk in through the gap and between the rows
            Collider(LP(lot, 0, 0.5f, -d * 0.5f), new Vector3(w, 1f, 0.3f), q);
            Collider(LP(lot, -w * 0.5f, 0.5f, 0), new Vector3(0.3f, 1f, d), q);
            Collider(LP(lot, w * 0.5f, 0.5f, 0), new Vector3(0.3f, 1f, d), q);
            Collider(LP(lot, -w * 0.3f, 0.5f, d * 0.5f), new Vector3(w * 0.4f, 1f, 0.3f), q);
            Collider(LP(lot, w * 0.3f, 0.5f, d * 0.5f), new Vector3(w * 0.4f, 1f, 0.3f), q);
        }

        /// <summary>
        /// The temple (left of the arena road, on the plaza): a podium with steps up to a portico of four columns and the
        /// cella you walk into. Inside: the god's statue on a pedestal at the back, an altar with fire in front of it
        /// (pray there) and two braziers. Its roof, ceiling and pediment hide while you are inside.
        /// </summary>
        void Temple(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth, P = 1.2f, H = 5.0f, t = 0.3f;
            float hx = w * 0.5f, hz = d * 0.5f;
            var marble = PS2Materials.Get("marble", PS2Textures.Stone, new Color(0.93f, 0.91f, 0.86f));
            var gold = PS2Materials.Get("gold", PS2Textures.Metal, new Color(1f, 0.82f, 0.35f));
            // podium you can stand on, steps up from the plaza (a ramp under them)
            Box(stone, LP(lot, 0, P * 0.5f, 0), new Vector3(w, P, d), q);
            Collider(LP(lot, 0, P * 0.5f, 0), new Vector3(w, P, d), q);
            const int ns = 4; const float sd = 0.42f;
            for (int s = 0; s < ns; s++)
            {
                float top = P * (s + 1) / ns;
                Box(stone, LP(lot, 0, top * 0.5f, hz + (ns - s - 0.5f) * sd), new Vector3(w * 0.8f, top, sd), q);
            }
            float rl = Mathf.Sqrt(ns * sd * ns * sd + P * P), ra = Mathf.Atan2(P, ns * sd) * Mathf.Rad2Deg;
            PS2Spawn.BoxCollider(colliders, LP(lot, 0, P * 0.5f - 0.05f, hz + ns * sd * 0.5f), new Vector3(w * 0.8f, 0.1f, rl), q * Quaternion.Euler(ra, 0f, 0f));

            // the cella: walls with a wide doorway at the front
            float cz0 = -hz + 0.3f, cz1 = hz - 2.6f, cx = hx - 0.5f, y0 = P, y1 = P + H, dwh = 1.1f;
            void Seg(float x0, float x1, float z0, float z1, float ya, float yb)
            {
                var size = new Vector3(x1 - x0, yb - ya, z1 - z0);
                Vector3 c = LP(lot, (x0 + x1) * 0.5f, (ya + yb) * 0.5f, (z0 + z1) * 0.5f);
                Box(plaster[3], c, size, q);
                Collider(c, size, q);
            }
            Seg(-cx, cx, cz0, cz0 + t, y0, y1);
            Seg(-cx, -cx + t, cz0, cz1, y0, y1);
            Seg(cx - t, cx, cz0, cz1, y0, y1);
            Seg(-cx, -dwh, cz1 - t, cz1, y0, y1);
            Seg(dwh, cx, cz1 - t, cz1, y0, y1);
            Seg(-dwh, dwh, cz1 - t, cz1, y0 + 3.8f, y1);
            Box(darkWood, LP(lot, 0, y0 + 3.86f, cz1 + 0.05f), new Vector3(dwh * 2f + 0.3f, 0.18f, 0.14f), q);
            Box(marble, LP(lot, 0, P + 0.02f, (cz0 + cz1) * 0.5f), new Vector3(cx * 2f - 2f * t, 0.04f, cz1 - cz0 - t), q, 0.5f);
            // portico: four columns in front of the cella
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = LP(lot, -hx + 0.7f + i * (w - 1.4f) / 3f, P, hz - 0.7f);
                AddMesh(marble, LowPolyMesh.Cylinder(0.32f, H, 8, true, false, 1f, 1f / 3f), p, q);
                Box(marble, p + Vector3.up * (H + 0.05f), new Vector3(0.8f, 0.2f, 0.8f), q);
                Collider(p + Vector3.up * (H * 0.5f), new Vector3(0.6f, H, 0.6f), q);
            }

            // inside: the god on his pedestal, the altar with fire, two braziers
            float sz = cz0 + t + 1.1f;
            Box(stone, LP(lot, 0, P + 0.5f, sz), new Vector3(1.5f, 1.0f, 1.3f), q);
            Collider(LP(lot, 0, P + 0.5f, sz), new Vector3(1.5f, 1.0f, 1.3f), q);
            Box(marble, LP(lot, 0, P + 1.0f + 1.0f, sz), new Vector3(0.75f, 2.0f, 0.55f), q);          // robe
            Box(marble, LP(lot, 0, P + 1.0f + 2.25f, sz), new Vector3(0.95f, 0.5f, 0.5f), q);          // shoulders
            Box(marble, LP(lot, 0, P + 1.0f + 2.7f, sz + 0.02f), new Vector3(0.36f, 0.42f, 0.38f), q); // head
            Box(gold, LP(lot, 0, P + 1.0f + 2.97f, sz + 0.02f), new Vector3(0.42f, 0.08f, 0.42f), q);  // laurel
            Box(marble, LP(lot, 0.6f, P + 1.0f + 2.2f, sz + 0.1f), new Vector3(0.22f, 0.7f, 0.22f), q * Quaternion.Euler(0, 0, -20f)); // raised arm
            Box(gold, LP(lot, 0.75f, P + 1.0f + 1.7f, sz + 0.12f), new Vector3(0.07f, 3.0f, 0.07f), q); // sceptre
            Box(gold, LP(lot, -0.48f, P + 1.0f + 1.6f, sz + 0.25f), new Vector3(0.3f, 0.3f, 0.06f), q); // a golden eagle on the hand
            float az = sz + 2.4f;
            Box(stone, LP(lot, 0, P + 0.45f, az), new Vector3(1.2f, 0.9f, 0.8f), q);
            Box(stoneDark, LP(lot, 0, P + 0.93f, az), new Vector3(1.3f, 0.06f, 0.9f), q);
            Box(ember, LP(lot, 0, P + 1.0f, az), new Vector3(0.6f, 0.08f, 0.4f), q, 0f);
            Box(flameMat, LP(lot, 0, P + 1.15f, az), new Vector3(0.3f, 0.25f, 0.25f), q * Quaternion.Euler(0, 45f, 0), 0f);
            Collider(LP(lot, 0, P + 0.45f, az), new Vector3(1.2f, 0.9f, 0.8f), q);
            var go = new GameObject("Temple_" + lot.Id);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(LP(lot, 0, P, (cz0 + cz1) * 0.5f), q);
            var house = go.AddComponent<HouseInterior>();
            house.Lot = lot; house.HalfX = cx; house.HalfZ = (cz1 - cz0) * 0.5f; house.Floor = 0; house.Public = true;
            house.Rooms.Add(new HouseInterior.Room { Name = "TEMPLO", X0 = -cx, X1 = cx, Z0 = -house.HalfZ, Z1 = house.HalfZ });
            // where you kneel to pray: in front of the altar, facing the god
            house.AltarSpot = new Pose(LP(lot, 0, P + 0.05f, az + 1.15f), q * Quaternion.LookRotation(Vector3.back));
            house.Furniture.Add(new FurnitureSpot(Gladiator.Combat.Fighter.ActPray, house.AltarSpot.Value));
            foreach (float bx in new[] { -cx + 0.7f, cx - 0.7f })
            {
                Box(iron, LP(lot, bx, P + 0.5f, sz + 0.4f), new Vector3(0.12f, 1.0f, 0.12f), q);
                Box(iron, LP(lot, bx, P + 1.05f, sz + 0.4f), new Vector3(0.5f, 0.12f, 0.5f), q);
                Box(ember, LP(lot, bx, P + 1.13f, sz + 0.4f), new Vector3(0.35f, 0.06f, 0.35f), q, 0f);
                var li = new GameObject("TempleFire").AddComponent<Light>();
                li.transform.SetParent(go.transform, false);
                li.transform.position = LP(lot, bx, P + 1.8f, sz + 0.4f);
                li.type = LightType.Point; li.range = 7f; li.intensity = 1.3f; li.color = new Color(1f, 0.62f, 0.3f);
                li.shadows = LightShadows.None; li.enabled = false;
                house.Lights.Add(li);
            }

            // above: ceiling, entablature and the pediment over the portico (hidden while you are inside)
            var mine = new Dictionary<Material, MeshBuilder>();
            current = mine;
            Box(plaster[3], LP(lot, 0, y1 + 0.075f, (cz0 + cz1) * 0.5f), new Vector3(cx * 2f, 0.15f, cz1 - cz0), q);
            Box(marble, LP(lot, 0, y1 + 0.35f, 0.2f), new Vector3(w, 0.5f, d - 0.4f), q);
            Gable(roofs[0], marble, LP(lot, 0, y1 + 0.6f, 0.2f), q * Quaternion.Euler(0, 90f, 0), d, w + 0.4f, 1.6f);
            current = null;
            var upper = new GameObject("Upper_" + lot.Id);
            upper.transform.SetParent(root, false);
            foreach (var kv in mine) PS2Spawn.Mesh(upper.transform, "Top_" + kv.Key.name, kv.Value.Build("TempleTop"), Vector3.zero, kv.Key);
            LowPolyMesh.BakeAmbient(upper, AmbientSky, AmbientGround);
            PS2Spawn.BoxCollider(upper.transform, LP(lot, 0, y1 + 1.2f, 0.2f), new Vector3(w + 0.4f, 2.2f, d), q);
            house.Upper = upper;
            house.Hide.Add(upper);
            Houses.Add(house);
        }

        /// <summary>
        /// The governor's domus (right of the arena road, on the plaza): a door on its hinge, a vestibule, the atrium with
        /// its impluvium (a shallow pool under the opening in the roof) and four columns, and at the back the tablinum: a
        /// raised dais with the governor's chair of state between purple hangings. The roof hides while you are inside.
        /// </summary>
        void GovernorDomus(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth, H = 4.2f, t = 0.25f;
            float hx = w * 0.5f, hz = d * 0.5f;
            var wall = PS2Materials.Get("domus_wall", PS2Textures.Stone, new Color(0.96f, 0.86f, 0.72f));
            var red = PS2Materials.Get("domus_red", PS2Textures.Stone, new Color(0.62f, 0.16f, 0.12f));
            var purple = PS2Materials.Get("domus_purple", PS2Textures.Cloth, new Color(0.42f, 0.12f, 0.4f));
            var mosaic = PS2Materials.Get("mosaic", PS2Textures.Stone, new Color(0.88f, 0.82f, 0.7f));
            var marble = PS2Materials.Get("marble", PS2Textures.Stone, new Color(0.93f, 0.91f, 0.86f));
            var gold = PS2Materials.Get("gold", PS2Textures.Metal, new Color(1f, 0.82f, 0.35f));
            var go = new GameObject("Domus_" + lot.Id);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(lot.Center, q);
            var house = go.AddComponent<HouseInterior>();
            house.Lot = lot; house.HalfX = hx; house.HalfZ = hz; house.Floor = 0; house.Public = true;
            void Seg(Material m, float x0, float x1, float z0, float z1, float ya, float yb, bool outside)
            {
                var size = new Vector3(x1 - x0, yb - ya, z1 - z0);
                Vector3 c = LP(lot, (x0 + x1) * 0.5f, (ya + yb) * 0.5f, (z0 + z1) * 0.5f);
                Box(m, c, size, q);
                Collider(c, size, q);
                if (outside) Box(stoneDark, LP(lot, (x0 + x1) * 0.5f, 0.35f, (z0 + z1) * 0.5f), new Vector3(size.x + 0.1f, 0.7f, size.z + 0.1f), q);
            }
            float dw = 0.7f;
            Seg(wall, -hx, hx, -hz, -hz + t, 0, H, true);
            Seg(wall, -hx, -hx + t, -hz, hz, 0, H, true);
            Seg(wall, hx - t, hx, -hz, hz, 0, H, true);
            Seg(wall, -hx, -dw, hz - t, hz, 0, H, true);
            Seg(wall, dw, hx, hz - t, hz, 0, H, true);
            Seg(wall, -dw, dw, hz - t, hz, 2.6f, H, false);
            // a red dado inside, a grand doorway outside: two pilasters and a lintel
            foreach (int s in new[] { -1, 1 })
                Box(marble, LP(lot, s * (dw + 0.2f), 1.4f, hz + 0.08f), new Vector3(0.35f, 2.8f, 0.2f), q);
            Box(marble, LP(lot, 0, 2.95f, hz + 0.1f), new Vector3(dw * 2f + 0.9f, 0.35f, 0.25f), q);
            Box(red, LP(lot, 0, 0.6f, -hz + t + 0.01f), new Vector3(w - 2f * t, 1.2f, 0.02f), q);
            // floor: a pale mosaic
            Box(mosaic, LP(lot, 0, 0.03f, 0), new Vector3(w - 2f * t, 0.06f, d - 2f * t), q, 0.5f);
            // atrium: the impluvium and four columns
            float ac = 0.6f;
            Box(marble, LP(lot, 0, 0.1f, ac), new Vector3(2.4f, 0.2f, 2.0f), q);
            Box(water, LP(lot, 0, 0.16f, ac), new Vector3(2.0f, 0.04f, 1.6f), q, 0f);
            foreach (int sx in new[] { -1, 1 }) foreach (int szz in new[] { -1, 1 })
            {
                Vector3 p = LP(lot, sx * 1.6f, 0, ac + szz * 1.4f);
                AddMesh(marble, LowPolyMesh.Cylinder(0.18f, H, 8, true, false, 1f, 1f / 3f), p, q);
                Collider(p + Vector3.up * (H * 0.5f), new Vector3(0.36f, H, 0.36f), q);
            }
            // tablinum: the dais, the chair of state, hangings
            float dz0 = -hz + t, dz1 = -hz + t + 2.2f;
            Box(marble, LP(lot, 0, 0.15f, (dz0 + dz1) * 0.5f), new Vector3(w - 2f * t, 0.3f, dz1 - dz0), q);
            Collider(LP(lot, 0, 0.15f, (dz0 + dz1) * 0.5f), new Vector3(w - 2f * t, 0.3f, dz1 - dz0), q);
            float tz = dz0 + 0.7f;
            Box(darkWood, LP(lot, 0, 0.3f + 0.25f, tz), new Vector3(0.9f, 0.5f, 0.7f), q);              // seat
            Box(darkWood, LP(lot, 0, 0.3f + 1.0f, tz - 0.32f), new Vector3(0.9f, 1.4f, 0.1f), q);       // high back
            Box(gold, LP(lot, 0, 0.3f + 1.75f, tz - 0.32f), new Vector3(0.5f, 0.12f, 0.12f), q);
            foreach (int s in new[] { -1, 1 }) Box(darkWood, LP(lot, s * 0.48f, 0.3f + 0.7f, tz), new Vector3(0.08f, 0.15f, 0.7f), q); // arms
            Box(purple, LP(lot, 0, 0.3f + 0.52f, tz + 0.02f), new Vector3(0.8f, 0.06f, 0.6f), q);      // cushion
            Collider(LP(lot, 0, 0.3f + 0.5f, tz), new Vector3(0.9f, 1.0f, 0.7f), q);
            foreach (int s in new[] { -1, 1 })
                Box(purple, LP(lot, s * 1.5f, 2.2f, -hz + t + 0.05f), new Vector3(1.2f, 3.2f, 0.05f), q, 0.6f);
            Box(gold, LP(lot, 0, 3.2f, -hz + t + 0.06f), new Vector3(1.0f, 0.5f, 0.05f), q);           // the eagle of Rome
            house.Rooms.Add(new HouseInterior.Room { Name = "ATRIO", X0 = -hx, X1 = hx, Z0 = dz1, Z1 = hz });
            house.Rooms.Add(new HouseInterior.Room { Name = "TABLINUM", X0 = -hx, X1 = hx, Z0 = -hz, Z1 = dz1 });
            house.ThroneSpot = new Pose(LP(lot, 0, 0.35f, tz + 0.85f), q);
            // lamps
            foreach (float lx in new[] { -hx * 0.55f, hx * 0.55f })
            {
                var lamp = new GameObject("DomusLamp").AddComponent<Light>();
                lamp.transform.SetParent(go.transform, false);
                lamp.transform.position = LP(lot, lx, 2.8f, -0.5f);
                lamp.type = LightType.Point; lamp.range = 7f; lamp.intensity = 1.2f; lamp.color = new Color(1f, 0.75f, 0.45f);
                lamp.shadows = LightShadows.None; lamp.enabled = false;
                house.Lights.Add(lamp);
            }
            // the door on its hinge (it swings inwards)
            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(go.transform, false);
            hinge.localPosition = new Vector3(-dw, 0f, hz - t * 0.5f);
            var panel = PS2Spawn.Box(hinge, "DoorPanel", new Vector3(dw * 2f - 0.04f, 2.56f, 0.08f), new Vector3(dw, 1.3f, 0f), darkWood, null, 0.8f);
            PS2Spawn.Box(panel.transform, "Band", new Vector3(dw * 2f - 0.1f, 0.1f, 0.1f), new Vector3(0, 0.6f, 0f), gold);
            PS2Spawn.Box(panel.transform, "Band2", new Vector3(dw * 2f - 0.1f, 0.1f, 0.1f), new Vector3(0, -0.7f, 0f), gold);
            panel.AddComponent<BoxCollider>().size = new Vector3(dw * 2f - 0.04f, 2.56f, 0.12f);
            LowPolyMesh.BakeAmbient(hinge.gameObject, AmbientSky, AmbientGround);
            var door = go.AddComponent<TownDoor>();
            door.Hinge = hinge; door.Width = dw * 2f; door.BaseRot = Quaternion.identity;
            house.Door = door;
            // above: ceiling (open over the impluvium) and a tiled roof
            var mine = new Dictionary<Material, MeshBuilder>();
            current = mine;
            Box(wall, LP(lot, 0, H + 0.075f, (ac + 1.2f + hz) * 0.5f), new Vector3(w, 0.15f, hz - ac - 1.2f), q);
            Box(wall, LP(lot, 0, H + 0.075f, (-hz + ac - 1.2f) * 0.5f), new Vector3(w, 0.15f, ac - 1.2f + hz), q);
            Gable(roofs[lot.RoofTint % roofs.Length], wall, LP(lot, 0, H, 0), q, w + 0.6f, d + 0.6f, 1.6f);
            current = null;
            var upper = new GameObject("Upper_" + lot.Id);
            upper.transform.SetParent(root, false);
            foreach (var kv in mine) PS2Spawn.Mesh(upper.transform, "Top_" + kv.Key.name, kv.Value.Build("DomusTop"), Vector3.zero, kv.Key);
            LowPolyMesh.BakeAmbient(upper, AmbientSky, AmbientGround);
            PS2Spawn.BoxCollider(upper.transform, LP(lot, 0, H + 0.9f, 0), new Vector3(w + 0.6f, 1.6f, d + 0.6f), q);
            house.Upper = upper;
            house.Hide.Add(upper);
            Houses.Add(house);
        }

        /// <summary>
        /// The jail (carcer), beside the arena: thick dark stone walls, a heavy iron-banded door, the jailer's room in
        /// front (table, chair, keys, a rack of spears) and at the back two cells behind iron bars, each with its own
        /// barred gate that opens with E, a straw pallet, a bucket, shackles on the wall and a high barred window.
        /// The roof hides while you are inside.
        /// </summary>
        void Jail(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth, H = 3.4f, t = 0.3f;
            float hx = w * 0.5f, hz = d * 0.5f;
            var wall = PS2Materials.Get("jail_wall", PS2Textures.Stone, new Color(0.52f, 0.5f, 0.47f));
            var floorM = PS2Materials.Get("jail_floor", PS2Textures.Stone, new Color(0.42f, 0.4f, 0.37f));
            var bars = PS2Materials.Get("jail_iron", PS2Textures.Metal, new Color(0.3f, 0.3f, 0.32f));
            var go = new GameObject("Jail_" + lot.Id);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(lot.Center, q);
            var house = go.AddComponent<HouseInterior>();
            house.Lot = lot; house.HalfX = hx; house.HalfZ = hz; house.Floor = 0; house.Public = true;

            void Seg(float x0, float x1, float z0, float z1, float ya, float yb, bool outside)
            {
                if (x1 - x0 < 0.05f || z1 - z0 < 0.02f || yb - ya < 0.02f) return;
                var size = new Vector3(x1 - x0, yb - ya, z1 - z0);
                Vector3 c = LP(lot, (x0 + x1) * 0.5f, (ya + yb) * 0.5f, (z0 + z1) * 0.5f);
                Box(wall, c, size, q);
                Collider(c, size, q);
                if (outside) Box(stoneDark, LP(lot, (x0 + x1) * 0.5f, 0.4f, (z0 + z1) * 0.5f), new Vector3(size.x + 0.12f, 0.8f, size.z + 0.12f), q);
            }
            // a small barred window: a dark hole and three iron bars (on the wall at z, facing out along +z or -z)
            void Window(float x, float y, float z, float sz)
            {
                Box(dark, LP(lot, x, y, z), new Vector3(0.8f, 0.5f, 0.04f), q);
                for (int i = -1; i <= 1; i++) Box(bars, LP(lot, x + i * 0.22f, y, z + sz * 0.03f), new Vector3(0.05f, 0.52f, 0.05f), q);
            }

            // ---- walls: the front with the door in the middle
            float dw = 0.6f;
            Seg(-hx, hx, -hz, -hz + t, 0, H, true);
            Seg(-hx, -hx + t, -hz, hz, 0, H, true);
            Seg(hx - t, hx, -hz, hz, 0, H, true);
            Seg(-hx, -dw, hz - t, hz, 0, H, true);
            Seg(dw, hx, hz - t, hz, 0, H, true);
            Seg(-dw, dw, hz - t, hz, 2.4f, H, false);
            Box(stoneDark, LP(lot, 0, 2.5f, hz + 0.06f), new Vector3(dw * 2f + 0.5f, 0.24f, 0.16f), q);   // lintel
            foreach (int s in new[] { -1, 1 }) Window(s * 3.8f, 2.0f, hz + 0.02f, 1f);                      // front windows
            Box(floorM, LP(lot, 0, 0.03f, 0), new Vector3(w - 2f * t, 0.06f, d - 2f * t), q, 0.5f);

            // ---- the cells: a stone wall between them, the bars along zb, a gate in each
            float ix0 = -hx + t, ix1 = hx - t, iz0 = -hz + t, zb = -0.4f, bh = 2.6f;
            Seg(-0.15f, 0.15f, iz0, zb, 0, H, false);                                                        // between the cells
            Seg(ix0, ix1, zb - 0.12f, zb + 0.12f, bh, H, false);                                             // over the bars
            house.Rooms.Add(new HouseInterior.Room { Name = "CELDA", X0 = -hx, X1 = 0f, Z0 = -hz, Z1 = zb });
            house.Rooms.Add(new HouseInterior.Room { Name = "CELDA", X0 = 0f, X1 = hx, Z0 = -hz, Z1 = zb });
            house.Rooms.Add(new HouseInterior.Room { Name = "CALABOZO", X0 = -hx, X1 = hx, Z0 = zb, Z1 = hz });
            const float gw = 1.0f, pitch = 0.17f;
            for (int c = 0; c < 2; c++)
            {
                int side = c == 0 ? -1 : 1;
                float cx0 = c == 0 ? ix0 : 0.15f, cx1 = c == 0 ? -0.15f : ix1, cmid = (cx0 + cx1) * 0.5f;
                float g0 = cmid - gw * 0.5f, g1 = cmid + gw * 0.5f;
                // fixed bars either side of the gate
                foreach (var (a, b) in new[] { (cx0, g0), (g1, cx1) })
                {
                    if (b - a < 0.1f) continue;
                    int n = Mathf.Max(1, Mathf.RoundToInt((b - a) / pitch));
                    for (int i = 0; i <= n; i++) Box(bars, LP(lot, a + (b - a) * i / n, bh * 0.5f, zb), new Vector3(0.05f, bh, 0.05f), q, 0f);
                    foreach (float y in new[] { 0.1f, 1.25f, bh - 0.08f })
                        Box(bars, LP(lot, (a + b) * 0.5f, y, zb), new Vector3(b - a, 0.06f, 0.08f), q, 0f);
                    Collider(LP(lot, (a + b) * 0.5f, bh * 0.5f, zb), new Vector3(b - a, bh, 0.14f), q);
                }
                // the gate: bars on a hinge (it swings into the cell)
                var holder = new GameObject("CellGate" + (c + 1)).transform;
                holder.SetParent(go.transform, false);
                var hinge = new GameObject("GateHinge").transform;
                hinge.SetParent(holder, false);
                hinge.localPosition = new Vector3(g0, 0f, zb);
                var gate = new GameObject("GatePanel");
                gate.transform.SetParent(hinge, false);
                for (int i = 0; i <= 5; i++)
                    PS2Spawn.Box(gate.transform, "Bar", new Vector3(0.05f, bh - 0.12f, 0.05f), new Vector3(0.04f + (gw - 0.08f) * i / 5f, (bh - 0.12f) * 0.5f, 0f), bars);
                foreach (float y in new[] { 0.12f, 1.25f, bh - 0.18f })
                    PS2Spawn.Box(gate.transform, "Band", new Vector3(gw - 0.04f, 0.07f, 0.08f), new Vector3(gw * 0.5f, y, 0f), bars);
                PS2Spawn.Box(gate.transform, "Lock", new Vector3(0.12f, 0.16f, 0.12f), new Vector3(gw - 0.1f, 1.1f, 0f), iron);
                var gc = gate.AddComponent<BoxCollider>();
                gc.center = new Vector3(gw * 0.5f, (bh - 0.12f) * 0.5f, 0f); gc.size = new Vector3(gw - 0.04f, bh - 0.12f, 0.12f);
                LowPolyMesh.BakeAmbient(hinge.gameObject, AmbientSky, AmbientGround);
                var door = holder.gameObject.AddComponent<TownDoor>();
                door.Hinge = hinge; door.Width = gw; door.BaseRot = Quaternion.identity; door.Noun = "LA CELDA";
                house.ExtraDoors.Add(door);

                // inside: a straw pallet along the outer side wall, head to the back wall (you can lie on it)
                float px = c == 0 ? ix0 + 0.55f : ix1 - 0.55f;
                Box(darkWood, LP(lot, px, 0.1f, iz0 + 1.05f), new Vector3(0.9f, 0.2f, 2.0f), q);
                Box(straw, LP(lot, px, 0.24f, iz0 + 1.05f), new Vector3(0.84f, 0.1f, 1.9f), q, 0f);
                Collider(LP(lot, px, 0.14f, iz0 + 1.05f), new Vector3(0.9f, 0.28f, 2.0f), q);
                house.Furniture.Add(new FurnitureSpot(Gladiator.Combat.Fighter.ActLie, new Pose(LP(lot, px, 0.29f, iz0 + 0.83f), q)));
                // a bucket in the other back corner
                float bx = c == 0 ? -0.15f - 0.4f : 0.15f + 0.4f;
                AddMesh(darkWood, LowPolyMesh.Cylinder(0.18f, 0.38f, 8, true, false, 1f, 0.5f), LP(lot, bx, 0f, iz0 + 0.4f), q);
                Box(iron, LP(lot, bx, 0.3f, iz0 + 0.4f), new Vector3(0.38f, 0.04f, 0.38f), q * Quaternion.Euler(0, 45f, 0), 0f);
                // shackles on the back wall: two rings, chains hanging
                foreach (float sx in new[] { -0.5f, 0.5f })
                {
                    Box(iron, LP(lot, cmid + sx, 1.7f, iz0 + 0.04f), new Vector3(0.12f, 0.12f, 0.06f), q, 0f);
                    Box(iron, LP(lot, cmid + sx, 1.35f, iz0 + 0.06f), new Vector3(0.04f, 0.6f, 0.04f), q, 0f);
                    Box(iron, LP(lot, cmid + sx, 1.03f, iz0 + 0.07f), new Vector3(0.16f, 0.08f, 0.08f), q, 0f);
                }
                Window(cmid, 2.6f, -hz - 0.02f, -1f);
                Window(cmid, 2.6f, iz0 + 0.02f, 1f);
                // a dim light in the cell
                var cl = new GameObject("CellLight").AddComponent<Light>();
                cl.transform.SetParent(go.transform, false);
                cl.transform.position = LP(lot, cmid, 2.5f, (iz0 + zb) * 0.5f);
                cl.type = LightType.Point; cl.range = 4.5f; cl.intensity = 0.7f; cl.color = new Color(0.85f, 0.75f, 0.6f);
                cl.shadows = LightShadows.None; cl.enabled = false;
                house.Lights.Add(cl);
                // where a prisoner sits: in the middle of the cell, looking out through the bars
                house.CellSpots.Add(new Pose(LP(lot, cmid + side * 1.9f, 0.05f, zb - 1.0f), q));
            }

            // ---- the jailer's room: a table and a chair, the keys on the wall, a rack of spears, a bench
            float tx = ix1 - 1.4f, tz = 2.4f;
            Box(wood, LP(lot, tx, 0.74f, tz), new Vector3(1.25f, 0.07f, 0.72f), q);
            foreach (float sx in new[] { -0.52f, 0.52f }) foreach (float sz in new[] { -0.28f, 0.28f })
                Box(darkWood, LP(lot, tx + sx, 0.37f, tz + sz), new Vector3(0.07f, 0.74f, 0.07f), q, 0f);
            Collider(LP(lot, tx, 0.4f, tz), new Vector3(1.25f, 0.8f, 0.72f), q);
            Box(clay, LP(lot, tx - 0.3f, 0.86f, tz), new Vector3(0.18f, 0.2f, 0.18f), q * Quaternion.Euler(0, 45f, 0), 0f);
            Box(iron, LP(lot, tx + 0.25f, 0.8f, tz + 0.1f), new Vector3(0.22f, 0.04f, 0.1f), q, 0f);           // a ring of keys
            {
                float cz = tz - 0.62f;
                Box(darkWood, LP(lot, tx, 0.44f, cz), new Vector3(0.4f, 0.06f, 0.4f), q);
                foreach (float lx in new[] { -0.16f, 0.16f }) foreach (float lz in new[] { -0.16f, 0.16f })
                    Box(darkWood, LP(lot, tx + lx, 0.22f, cz + lz), new Vector3(0.05f, 0.44f, 0.05f), q, 0f);
                Box(darkWood, LP(lot, tx, 0.75f, cz - 0.18f), new Vector3(0.4f, 0.55f, 0.05f), q, 0f);
                Collider(LP(lot, tx, 0.4f, cz), new Vector3(0.4f, 0.8f, 0.4f), q);
                house.Furniture.Add(new FurnitureSpot(Gladiator.Combat.Fighter.ActChair, new Pose(LP(lot, tx, 0.02f, cz), q)));
            }
            // keys hanging by the door, a rack of spears on the other wall, a bench under it
            Box(darkWood, LP(lot, dw + 0.5f, 1.6f, hz - t - 0.03f), new Vector3(0.3f, 0.06f, 0.04f), q, 0f);
            foreach (float kx in new[] { -0.08f, 0.04f, 0.12f })
                Box(iron, LP(lot, dw + 0.5f + kx, 1.45f, hz - t - 0.05f), new Vector3(0.03f, 0.18f, 0.02f), q, 0f);
            Box(darkWood, LP(lot, ix0 + 0.1f, 1.8f, 2.4f), new Vector3(0.1f, 0.08f, 1.6f), q, 0f);
            for (int i = 0; i < 3; i++)
            {
                Box(darkWood, LP(lot, ix0 + 0.16f, 1.3f, 1.8f + i * 0.5f), new Vector3(0.04f, 2.2f, 0.04f), q, 0f);
                Box(iron, LP(lot, ix0 + 0.16f, 2.45f, 1.8f + i * 0.5f), new Vector3(0.06f, 0.22f, 0.06f), q * Quaternion.Euler(0, 45f, 0), 0f);
            }
            Box(wood, LP(lot, ix0 + 0.45f, 0.42f, 3.6f), new Vector3(0.45f, 0.08f, 1.6f), q);
            foreach (float bz in new[] { 2.95f, 4.25f }) Box(darkWood, LP(lot, ix0 + 0.45f, 0.19f, bz), new Vector3(0.4f, 0.38f, 0.08f), q, 0f);
            Collider(LP(lot, ix0 + 0.45f, 0.23f, 3.6f), new Vector3(0.45f, 0.46f, 1.6f), q);
            // two lamps in the jailer's room
            foreach (float lx in new[] { -hx * 0.45f, hx * 0.45f })
            {
                var lamp = new GameObject("JailLamp").AddComponent<Light>();
                lamp.transform.SetParent(go.transform, false);
                lamp.transform.position = LP(lot, lx, 2.7f, 2.2f);
                lamp.type = LightType.Point; lamp.range = 6.5f; lamp.intensity = 1.1f; lamp.color = new Color(1f, 0.7f, 0.4f);
                lamp.shadows = LightShadows.None; lamp.enabled = false;
                house.Lights.Add(lamp);
                Box(ember, LP(lot, lx, 2.85f, 2.2f), new Vector3(0.12f, 0.12f, 0.12f), q);
            }

            // ---- the front door: heavy planks with iron bands (it swings inwards)
            var dh = new GameObject("DoorHinge").transform;
            dh.SetParent(go.transform, false);
            dh.localPosition = new Vector3(-dw, 0f, hz - t * 0.5f);
            var panel = PS2Spawn.Box(dh, "DoorPanel", new Vector3(dw * 2f - 0.04f, 2.36f, 0.1f), new Vector3(dw, 1.2f, 0f), darkWood, null, 0.8f);
            foreach (float y in new[] { -0.8f, 0f, 0.8f })
                PS2Spawn.Box(panel.transform, "Band", new Vector3(dw * 2f - 0.08f, 0.1f, 0.13f), new Vector3(0, y, 0f), iron);
            PS2Spawn.Box(panel.transform, "Grille", new Vector3(0.36f, 0.26f, 0.13f), new Vector3(0, 0.45f, 0f), dark);
            panel.AddComponent<BoxCollider>().size = new Vector3(dw * 2f - 0.04f, 2.36f, 0.14f);
            LowPolyMesh.BakeAmbient(dh.gameObject, AmbientSky, AmbientGround);
            var front = go.AddComponent<TownDoor>();
            front.Hinge = dh; front.Width = dw * 2f; front.BaseRot = Quaternion.identity;
            house.Door = front;

            // ---- above: the ceiling and a low tiled roof, hidden while you are inside
            var mine = new Dictionary<Material, MeshBuilder>();
            current = mine;
            Box(wall, LP(lot, 0, H + 0.075f, 0), new Vector3(w, 0.15f, d), q);
            float rise = 1.2f;
            Gable(roofs[lot.RoofTint % roofs.Length], wall, LP(lot, 0, H, 0), q, w + 0.5f, d + 0.5f, rise);
            current = null;
            var upper = new GameObject("Upper_" + lot.Id);
            upper.transform.SetParent(root, false);
            foreach (var kv in mine) PS2Spawn.Mesh(upper.transform, "Top_" + kv.Key.name, kv.Value.Build("JailTop"), Vector3.zero, kv.Key);
            LowPolyMesh.BakeAmbient(upper, AmbientSky, AmbientGround);
            PS2Spawn.BoxCollider(upper.transform, LP(lot, 0, H + 0.2f + rise * 0.5f, 0), new Vector3(w + 0.5f, rise, d + 0.5f), q);
            house.Upper = upper;
            house.Hide.Add(upper);
            Houses.Add(house);
        }

        void Baths(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth;
            Box(stone, LP(lot, 0, 2.2f, 0), new Vector3(w, 4.4f, d), q);
            Box(stoneDark, LP(lot, 0, 0.35f, 0), new Vector3(w + 0.1f, 0.7f, d + 0.1f), q);
            AddMesh(roofs[2], LowPolyMesh.Sphere(Mathf.Min(w, d) * 0.38f, 10, 5, false, 0f), LP(lot, 0, 4.4f, 0), q);
            Box(dark, LP(lot, 0, 1.4f, d * 0.5f + 0.02f), new Vector3(1.8f, 2.8f, 0.06f), q);
            foreach (float s in new[] { -1.4f, 1.4f })
                AddMesh(stone, LowPolyMesh.Cylinder(0.22f, 3.6f, 8, true, false, 1f, 1f / 3f), LP(lot, s, 0, d * 0.5f + 0.4f), q);
            Box(stone, LP(lot, 0, 3.7f, d * 0.5f + 0.4f), new Vector3(3.6f, 0.3f, 0.6f), q);
            Collider(LP(lot, 0, 3.2f, 0), new Vector3(w, 6.4f, d), q);
        }

        void Basilica(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth;
            Box(plaster[0], LP(lot, 0, 2.6f, -0.6f), new Vector3(w, 5.2f, d - 1.6f), q);
            Gable(roofs[1], plaster[0], LP(lot, 0, 5.2f, -0.6f), q, w + 0.5f, d - 1.0f, 1.4f);
            for (int i = 0; i < 5; i++)
                AddMesh(stone, LowPolyMesh.Cylinder(0.24f, 3.8f, 8, true, false, 1f, 1f / 3f), LP(lot, -w * 0.5f + 0.5f + i * (w - 1f) / 4f, 0, d * 0.5f - 0.3f), q);
            Box(stone, LP(lot, 0, 3.9f, d * 0.5f - 0.3f), new Vector3(w, 0.3f, 0.8f), q);
            Box(roofs[1], LP(lot, 0, 4.15f, d * 0.5f - 0.8f), new Vector3(w, 0.15f, 1.8f), q * Quaternion.Euler(-10f, 0, 0), 0.5f);
            Box(dark, LP(lot, 0, 1.3f, d * 0.5f - 1.38f), new Vector3(1.6f, 2.6f, 0.06f), q);
            Collider(LP(lot, 0, 3.3f, -0.6f), new Vector3(w, 6.6f, d - 1.6f), q);
        }

        /// <summary>Adds an existing low-poly mesh (cylinder, dome) into a material batch, placed in the world.</summary>
        void AddMesh(Material m, Mesh mesh, Vector3 pos, Quaternion rot)
        {
            var b = B(m);
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var t = mesh.triangles;
            int start = b.VertexCount;
            for (int i = 0; i < v.Length; i++)
                b.Add(pos + rot * v[i], rot * n[i], uv.Length == v.Length ? uv[i] : Vector2.zero, Color.white);
            for (int i = 0; i < t.Length; i += 3) b.Tri(start + t[i], start + t[i + 1], start + t[i + 2]);
            Object.Destroy(mesh);
        }

        // ------------------------------------------------------------------ plaza furniture
        void BuildFountain()
        {
            Vector3 c = L.Fountain;
            AddMesh(stone, LowPolyMesh.Cylinder(2.4f, 0.75f, 8, true, false, 2f, 1f / 3f), c, Quaternion.identity);
            AddMesh(water, LowPolyMesh.Cylinder(2.1f, 0.02f, 8, true), c + Vector3.up * 0.62f, Quaternion.identity);
            AddMesh(stone, LowPolyMesh.Cylinder(0.32f, 1.9f, 8, true, false, 1f, 1f / 3f), c, Quaternion.identity);
            AddMesh(stone, LowPolyMesh.Cylinder(0.85f, 0.25f, 8), c + Vector3.up * 1.6f, Quaternion.identity);
            AddMesh(water, LowPolyMesh.Cylinder(0.7f, 0.02f, 8), c + Vector3.up * 1.86f, Quaternion.identity);
            var col = new GameObject("FountainCol");
            col.transform.SetParent(colliders, false);
            col.transform.position = c + Vector3.up * 0.5f;
            var cc = col.AddComponent<CapsuleCollider>();
            cc.radius = 2.4f; cc.height = 6f;
        }

        void BuildStall(Vector3 pos, Vector3 facing)
        {
            Quaternion q = Quaternion.LookRotation(facing);
            Vector3 P(float x, float y, float z) => pos + q * new Vector3(x, y, z);
            Box(wood, P(0, 0.9f, 0), new Vector3(2.2f, 0.1f, 0.8f), q);
            Box(darkWood, P(0, 0.45f, 0.38f), new Vector3(2.2f, 0.8f, 0.06f), q);
            foreach (float x in new[] { -1.05f, 1.05f })
                foreach (float z in new[] { -0.45f, 0.4f })
                    Box(wood, P(x, 1.2f, z), new Vector3(0.08f, 2.4f, 0.08f), q);
            for (int i = 0; i < 5; i++)
                Box(i % 2 == 0 ? awningA : awningB, P(-0.88f + i * 0.44f, 2.45f, 0), new Vector3(0.44f, 0.05f, 1.4f), q * Quaternion.Euler(-8f, 0, 0), 0f);
            // goods: baskets of produce in a few colours
            Color[] goods = { new Color(0.8f, 0.2f, 0.12f), new Color(0.9f, 0.7f, 0.2f), new Color(0.35f, 0.55f, 0.2f), new Color(0.5f, 0.3f, 0.5f) };
            for (int i = 0; i < 4; i++)
            {
                Box(straw, P(-0.75f + i * 0.5f, 1.02f, 0), new Vector3(0.38f, 0.14f, 0.38f), q, 0f);
                Box(PS2Materials.Get("goods_" + i, PS2Textures.Cloth, goods[(i + (int)(pos.x * 7f)) & 3]), P(-0.75f + i * 0.5f, 1.12f, 0), new Vector3(0.3f, 0.1f, 0.3f), q, 0f);
            }
            Collider(P(0, 0.5f, 0), new Vector3(2.3f, 1f, 1f), q);
        }

        // ------------------------------------------------------------------ landmarks
        void BuildArena()
        {
            const float R = TownLayout.ArenaRadius;
            float[] ys = { 0f, 5f, 10f, 15f, 18.5f };
            var cb = new MeshBuilder();
            var darkArch = new Color(0.45f, 0.4f, 0.36f);
            for (int s = 0; s < 4; s++)
                cb.AddLathe(new[] { new MeshBuilder.Ring(R, ys[s], 1, 0), new MeshBuilder.Ring(R, ys[s + 1], 1, 0) },
                            40, Vector3.zero, s < 3 ? Color.white : new Color(0.9f, 0.88f, 0.84f), false, 40f, 0f);
            for (int s = 0; s < 3; s++)
                for (int i = 0; i < 40; i++)
                {
                    float a = (i + 0.5f) / 40f * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    cb.AddBox(d * (R + 0.05f) + Vector3.up * (ys[s] + 2.5f), new Vector3(1.6f, 3.2f, 0.1f), Quaternion.LookRotation(d), darkArch);
                }
            var colo = PS2Spawn.Mesh(root, "Colosseum", cb.Build("TownColosseum"), L.ArenaCenter, stone);
            LowPolyMesh.BakeAmbient(colo, AmbientSky, AmbientGround);
            var mc = colo.AddComponent<MeshCollider>();
            mc.sharedMesh = LowPolyMesh.Cylinder(R, 20f, 24);

            // main gate: big dark archway with stone frame and red banners, facing the town
            Quaternion q = Quaternion.LookRotation(L.ArenaFacing);
            Vector3 g = L.ArenaCenter + L.ArenaFacing * (R + 0.3f);
            Box(dark, g + Vector3.up * 2.3f, new Vector3(4.2f, 4.6f, 0.4f), q);
            Box(stone, g + q * new Vector3(-2.6f, 2.8f, 0.2f), new Vector3(1.0f, 5.6f, 0.9f), q);
            Box(stone, g + q * new Vector3(2.6f, 2.8f, 0.2f), new Vector3(1.0f, 5.6f, 0.9f), q);
            Box(stone, g + q * new Vector3(0, 5.2f, 0.2f), new Vector3(6.2f, 1.0f, 0.9f), q);
            foreach (float x in new[] { -4.2f, 4.2f })
                Box(banner, g + q * new Vector3(x, 6.5f, 0.15f), new Vector3(1.0f, 4f, 0.05f), q, 0f);
        }

        void BuildLudus()
        {
            Quaternion q = Quaternion.LookRotation(L.LudusFacing);
            Vector3 c = L.LudusCenter;
            Vector3 P(float x, float y, float z) => c + q * new Vector3(x, y, z);
            float hx = TownLayout.LudusHalfX, hz = TownLayout.LudusHalfZ, h = 4.5f;
            Material wall = plaster[0];
            void Wall(float x, float z, float sx, float sz)
            {
                Box(wall, P(x, h * 0.5f, z), new Vector3(sx, h, sz), q);
                Box(stoneDark, P(x, 0.45f, z), new Vector3(sx + 0.12f, 0.9f, sz + 0.12f), q);
                Box(roofs[0], P(x, h + 0.12f, z), new Vector3(sx + 0.25f, 0.25f, sz + 0.25f), q);
                Collider(P(x, 2.4f, z), new Vector3(sx, 4.8f, sz), q);
            }
            float side = hx - 2f;
            Wall(-(2f + side * 0.5f), hz, side, 1f);  // front, split by the gate
            Wall(2f + side * 0.5f, hz, side, 1f);
            Wall(0, -hz, hx * 2f + 1f, 1f);
            Wall(-hx, 0, 1f, hz * 2f);
            Wall(hx, 0, 1f, hz * 2f);
            // gate: closed wooden doors under a lintel with the red banner
            Box(stone, P(-2.3f, 2.65f, hz), new Vector3(0.6f, 5.3f, 1.4f), q);
            Box(stone, P(2.3f, 2.65f, hz), new Vector3(0.6f, 5.3f, 1.4f), q);
            Box(stone, P(0, 4.7f, hz), new Vector3(5.8f, 1.2f, 1.4f), q);
            Box(banner, P(0, 4.7f, hz + 0.73f), new Vector3(3.6f, 0.8f, 0.05f), q, 0f);
            Box(darkWood, P(-1f, 1.8f, hz), new Vector3(1.95f, 3.6f, 0.25f), q, 0.8f);
            Box(darkWood, P(1f, 1.8f, hz), new Vector3(1.95f, 3.6f, 0.25f), q, 0.8f);
            Collider(P(0, 2.65f, hz), new Vector3(4.6f, 5.3f, 1.4f), q);
            // roofs of the barracks showing over the walls
            Gable(roofs[1], wall, P(-hx + 2.5f, h - 0.4f, 0), q * Quaternion.Euler(0, 90f, 0), hz * 2f - 1f, 4f, 1.2f);
            Gable(roofs[1], wall, P(hx - 2.5f, h - 0.4f, -2f), q * Quaternion.Euler(0, 90f, 0), hz * 2f - 5f, 4f, 1.2f);
        }

        void BuildTownWall()
        {
            var k = L.WallCorners;
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = k[i], b = k[(i + 1) % 4];
                Vector3 mid = (a + b) * 0.5f, dir = b - a;
                Quaternion q = Quaternion.LookRotation(dir.normalized);
                float len = dir.magnitude;
                Box(stone, mid + Vector3.up * 2.75f, new Vector3(1.6f, 5.5f, len + 1.6f), q);
                // crenellations
                for (float s = -len * 0.5f + 1f; s < len * 0.5f; s += 2.4f)
                    Box(stone, mid + q * new Vector3(0, 5.85f, s), new Vector3(1.7f, 0.7f, 1.2f), q);
                Collider(mid + Vector3.up * 3f, new Vector3(1.6f, 6f, len + 1.6f), q);
                // corner tower
                AddMesh(stoneDark, LowPolyMesh.Cylinder(2.6f, 8f, 8, true, false, 2f, 1f / 3f), a, Quaternion.identity);
                AddMesh(roofs[0], LowPolyMesh.Sphere(2.9f, 8, 4, false, 0f), a + Vector3.up * 8f, Quaternion.identity);
            }
        }

        void BuildCypress(Vector3 p)
        {
            var t = new GameObject("Cypress").transform;
            t.SetParent(root, false);
            t.position = p;
            float s = 0.85f + Mathf.Repeat(p.x * 0.37f + p.z * 0.11f, 0.4f);
            PS2Spawn.Mesh(t, "Trunk", LowPolyMesh.Cylinder(0.14f, 0.9f, 6), Vector3.zero, wood);
            var fol = PS2Spawn.Mesh(t, "Foliage", LowPolyMesh.Sphere(1f, 7, 6), new Vector3(0, 0.9f + 2.4f * s, 0), foliage);
            fol.transform.localScale = new Vector3(0.75f, 2.6f * s, 0.75f);
            LowPolyMesh.BakeAmbient(t.gameObject, AmbientSky, AmbientGround);
            PS2Spawn.BoxCollider(colliders, p + Vector3.up * 1f, new Vector3(0.6f, 2f, 0.6f));
        }

        void BuildTorch(Vector3 p)
        {
            Material flame = PS2Materials.Flat(new Color(1f, 0.62f, 0.15f), 0.3f);
            var t = new GameObject("Torch").transform;
            t.SetParent(root, false);
            t.position = p;
            PS2Spawn.Mesh(t, "Pole", LowPolyMesh.Cylinder(0.07f, 2.3f, 6), Vector3.zero, wood);
            PS2Spawn.Mesh(t, "Bowl", LowPolyMesh.Cylinder(0.22f, 0.18f, 6), new Vector3(0, 2.3f, 0), iron);
            var fl = new GameObject("Flame").transform;
            fl.SetParent(t, false);
            fl.localPosition = new Vector3(0, 2.45f, 0);
            PS2Spawn.Mesh(fl, "F", LowPolyMesh.Box(new Vector3(0.24f, 0.4f, 0.24f), new Vector3(0, 0.2f, 0)), Vector3.zero, flame, Quaternion.Euler(0, 45f, 0));
            var l = new GameObject("TorchLight").AddComponent<Light>();
            l.transform.SetParent(t, false);
            l.transform.localPosition = new Vector3(0, 2.8f, 0);
            l.type = LightType.Point; l.range = 8f; l.intensity = 1.4f; l.color = new Color(1f, 0.55f, 0.22f);
            l.shadows = LightShadows.None;
            var tf = t.gameObject.AddComponent<TorchFlicker>();
            tf.Light = l; tf.Flame = fl; tf.BaseIntensity = l.intensity;
            LowPolyMesh.BakeAmbient(t.gameObject, AmbientSky, AmbientGround);
            PS2Spawn.BoxCollider(colliders, p + Vector3.up * 1f, new Vector3(0.3f, 2f, 0.3f));
        }
    }
}
