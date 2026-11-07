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
            BuildLudus();
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
            BuildingCount++;
            switch (lot.Kind)
            {
                case BuildingKind.Garden: Garden(lot); return;
                case BuildingKind.Temple: Temple(lot); return;
                case BuildingKind.Baths: Baths(lot); return;
                case BuildingKind.Forum: Basilica(lot); return;
            }
            if (lot.IsHome) { HomeHouse(lot); return; }
            House(lot);
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
                if (insula)
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
                BuildStairs(lot, ss, H, upperHouse);
            }

            // ---- the top: ceiling of the highest open storey, solid storeys above it, roof + a collider for the camera
            float topBase = stairs ? 2f * H : H;
            var mine = new Dictionary<Material, MeshBuilder>();
            current = mine;
            Box(wall, LP(lot, 0, topBase + 0.075f, 0), new Vector3(w, 0.15f, d), q);
            if (h > topBase + 0.2f) Box(wall, LP(lot, 0, (topBase + 0.15f + h) * 0.5f, 0), new Vector3(w, h - topBase - 0.15f, d), q);
            Gable(roofs[lot.RoofTint % roofs.Length], wall, LP(lot, 0, h, 0), q, w + 0.6f, d + 0.6f, rise);
            for (int f = stairs ? 2 : 1; f < lot.Floors; f++)
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
            if (midGo != null) ground.Hide.Add(midGo);
            Houses.Add(ground);
            if (upperHouse != null)
            {
                upperHouse.Upper = upper;
                upperHouse.Hide.Add(upper);
                Houses.Add(upperHouse);
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
            float dz = hz - 1.3f;                           // side door (first floor), over the stair landing
            float sideX0 = ss > 0 ? hx - t : -hx, sideX1 = ss > 0 ? hx : -hx + t;
            float otherX0 = ss > 0 ? -hx : hx - t, otherX1 = ss > 0 ? -hx + t : hx;
            Seg(-hx, hx, -hz, -hz + t, y0, y1, true);                                   // back
            Seg(otherX0, otherX1, -hz, hz, y0, y1, true);                               // side without the stairs
            if (sideDoor)
            {
                Seg(sideX0, sideX1, -hz, dz - dw, y0, y1, true);                       // stair side, behind the door
                Seg(sideX0, sideX1, dz + dw, hz, y0, y1, true);                        // stair side, in front of the door
                Seg(sideX0, sideX1, dz - dw, dz + dw, y0 + 2.3f, y1, false);           // over the door
                Seg(-hx, hx, hz - t, hz, y0, y1, true);                                 // front (no door)
            }
            else
            {
                Seg(sideX0, sideX1, -hz, hz, y0, y1, true);
                Seg(-hx, dx - dw, hz - t, hz, y0, y1, true);                            // front, left of the door
                Seg(dx + dw, hx, hz - t, hz, y0, y1, true);                             // front, right of the door
                Seg(dx - dw, dx + dw, hz - t, hz, y0 + 2.3f, y1, false);                // over the door
                Box(darkWood, LP(lot, dx, y0 + 2.36f, hz + 0.05f), new Vector3(1.5f, 0.16f, 0.12f), q);
            }

            // rooms: kitchen in front; bedroom + bathroom at the back
            float ix0 = -hx + t, ix1 = hx - t, iz0 = -hz + t, iz1 = hz - t;
            float zs = iz0 + (iz1 - iz0) * 0.45f;
            bool bathRight = (lot.DoorOffset < 0f) ^ (level == 1);
            float xs = bathRight ? ix1 - (ix1 - ix0) * 0.4f : ix0 + (ix1 - ix0) * 0.4f;
            float bedX0 = bathRight ? ix0 : xs, bedX1 = bathRight ? xs : ix1;
            float bathX0 = bathRight ? xs : ix0, bathX1 = bathRight ? ix1 : xs;
            float bedC = (bedX0 + bedX1) * 0.5f, bathC = (bathX0 + bathX1) * 0.5f;
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

            // front windows
            int nwin = level == 0 ? 2 : Mathf.Max(2, Mathf.FloorToInt(w / 2.4f));
            for (int i = 0; i < nwin; i++)
            {
                float x = -w * 0.5f + w * (i + 0.5f) / nwin, y = y0 + (level == 0 ? 2.0f : 1.6f);
                if (level == 0 && Mathf.Abs(x - dx) < 1.4f) continue;
                Box(dark, LP(lot, x, y, hz + 0.02f), new Vector3(0.6f, 0.75f, 0.06f), q);
                Box(darkWood, LP(lot, x - 0.42f, y, hz + 0.05f), new Vector3(0.22f, 0.8f, 0.05f), q);
                Box(darkWood, LP(lot, x + 0.42f, y, hz + 0.05f), new Vector3(0.22f, 0.8f, 0.05f), q);
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
        void BuildStairs(TownLot lot, int ss, float H, HouseInterior upper)
        {
            Quaternion q = lot.Rotation;
            float hx = lot.Width * 0.5f, hz = lot.Depth * 0.5f, sw = 1.15f;
            float x0 = ss * hx, xc = ss * (hx + sw * 0.5f), xo = ss * (hx + sw);
            float zBottom = -hz + 0.3f, zLand = hz - 2.1f, zEnd = hz - 0.5f;
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
            Collider(LP(lot, xc, H + 0.5f, zEnd - 0.05f), new Vector3(sw, 1f, 0.1f), q);                 // end of the landing
            upper.StairBottom = LP(lot, xc, 0.05f, zBottom - 0.6f);
            upper.StairTop = LP(lot, xc, H + 0.05f, hz - 1.3f);
            upper.StairDir = q * Vector3.forward;
        }

        /// <summary>Open shop front: counter, striped awning.</summary>
        void ShopFront(TownLot lot, Material a, Material b)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, hz = lot.Depth * 0.5f;
            float cx = -lot.DoorOffset * w * 0.6f;
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
            var ring = PS2Spawn.Mesh(root, "ShopRing_" + lot.Id, b.Build("ShopRing"), lot.Door, PS2Materials.Flat(new Color(1f, 0.82f, 0.1f), 0.2f));
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Vector3 pole = lot.Door + lot.Right * 1.6f;
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
                float x = lot.Width * 0.5f - 0.4f - (i % 4) * 0.55f, z = hz + 0.4f + (i / 4) * 0.5f;
                Box(clay, LP(lot, x, 0.32f, z), new Vector3(0.32f, 0.64f, 0.32f), q * Quaternion.Euler(0, 45f, 0), 0f);
                Box(clay, LP(lot, x, 0.7f, z), new Vector3(0.16f, 0.14f, 0.16f), q, 0f);
            }
        }

        void Smithy(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, hz = lot.Depth * 0.5f;
            // open shed with an anvil and a forge glowing
            float sx = -lot.DoorOffset * w * 0.6f;
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
            Collider(LP(lot, 0, 0.6f, 0), new Vector3(w, 1.2f, d), q);
        }

        void Temple(TownLot lot)
        {
            Quaternion q = lot.Rotation;
            float w = lot.Width, d = lot.Depth;
            Box(stone, LP(lot, 0, 0.6f, 0), new Vector3(w, 1.2f, d), q);                       // podium
            for (int s = 0; s < 3; s++)                                                         // steps
                Box(stone, LP(lot, 0, 0.2f + s * 0.4f, d * 0.5f + 0.9f - s * 0.3f), new Vector3(w * 0.7f, 0.4f, 0.6f), q);
            Box(plaster[3], LP(lot, 0, 1.2f + 2.6f, -d * 0.15f), new Vector3(w - 1.6f, 5.2f, d * 0.6f), q); // cella
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = LP(lot, -w * 0.5f + 0.7f + i * (w - 1.4f) / 3f, 1.2f, d * 0.5f - 0.7f);
                var cyl = LowPolyMesh.Cylinder(0.32f, 5.2f, 8, true, false, 1f, 1f / 3f);
                AddMesh(stone, cyl, p, q);
                Box(stone, p + Vector3.up * 5.25f, new Vector3(0.8f, 0.2f, 0.8f), q);
            }
            Box(stone, LP(lot, 0, 6.55f, 0.2f), new Vector3(w, 0.5f, d - 0.4f), q);            // entablature
            Gable(roofs[0], stone, LP(lot, 0, 6.8f, 0.2f), q * Quaternion.Euler(0, 90f, 0), d, w + 0.4f, 1.6f); // pediment faces the plaza
            Collider(LP(lot, 0, 4.2f, 0), new Vector3(w, 8.4f, d + 1.4f), q);
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
