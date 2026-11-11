using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>What stands on a lot.</summary>
    public enum BuildingKind { Domus, Insula, Shop, Garden, Temple, Tavern, Smithy, Bakery, Baths, Pottery, Forum, Infirmary }

    /// <summary>A named spot the simulation can send citizens to (a home, a workplace, the plaza...).</summary>
    public enum PlaceKind { Home, Work, Plaza, Fountain, Market, ArenaGate, LudusGate, GuardPost }

    /// <summary>One building plot: a rectangle by a street, its front facing the street.</summary>
    public class TownLot
    {
        public int Id;
        public BuildingKind Kind;
        public Vector3 Center;   // world
        public Vector3 Front;    // world, unit, from the building towards its street
        public float Width;      // along the street
        public float Depth;      // away from the street
        public int Floors;
        public int Tint, RoofTint;
        public float DoorOffset; // along the width, -0.5..0.5
        /// <summary>Two-storey homes: the side of the outside staircase (+1 right, -1 left).</summary>
        public int StairSide;
        /// <summary>Empty, for the player to buy (nobody lives there).</summary>
        public bool ForSale;
        /// <summary>Large home: kitchen, hallway, 2 bedrooms, 2 bathrooms and a 2 x 2 m storeroom.</summary>
        public bool Large;
        public int Price;
        /// <summary>Gets a paved path from its door to the street.</summary>
        public bool StreetPath;
        /// <summary>World point just outside the front door (where people go in and out).</summary>
        public Vector3 Door => Center + Front * (Depth * 0.5f + 0.9f) + Right * (DoorOffset * Width);
        public Vector3 Right => Vector3.Cross(Vector3.up, Front).normalized;
        /// <summary>Shops: where you stand to buy (the yellow ring), beside the door so the door stays free.</summary>
        public Vector3 ShopSpot => Door + Right * (DoorOffset >= 0f ? 2.1f : -2.1f);
        public Quaternion Rotation => Quaternion.LookRotation(Front);
        public bool IsHome => Kind == BuildingKind.Domus || Kind == BuildingKind.Insula;
        /// <summary>Shops you walk into through a real door (built like a home inside): store, tavern, smithy, bakery, pottery.</summary>
        public bool IsShop => Kind == BuildingKind.Shop || Kind == BuildingKind.Tavern || Kind == BuildingKind.Smithy ||
                              Kind == BuildingKind.Bakery || Kind == BuildingKind.Pottery;
    }

    /// <summary>A named point for the town simulation.</summary>
    public class TownPlace
    {
        public string Id, Name;
        public PlaceKind Kind;
        public Vector3 Position; // world, on the ground
        public int LotId = -1;
        /// <summary>Where someone standing here looks (guard posts).</summary>
        public Vector3 Facing;
    }

    /// <summary>A paved rectangle (plaza or street).</summary>
    public struct TownPaving
    {
        public Vector3 Center; public Vector2 Size; public float Yaw; public bool Plaza;
    }

    /// <summary>
    /// The town plan as pure data, from a seed. A fixed template (plaza in the middle, a main street to the arena in
    /// the north, a street to the ludus in the south and a cross street east-west, buildings along them) is varied by
    /// the seed: it is rotated (0/90/180/270), maybe mirrored, and every lot gets its own building type, size, colours
    /// and door. The same seed always gives the same town. Nothing here creates GameObjects, so the town simulation
    /// can ask "where is the bakery" without the scene.
    /// </summary>
    public class TownLayout
    {
        public int Seed { get; private set; }
        public float RotationDeg { get; private set; }
        public bool Mirrored { get; private set; }

        public readonly List<TownLot> Lots = new List<TownLot>();
        public readonly List<TownPlace> Places = new List<TownPlace>();
        public readonly List<TownPaving> Paving = new List<TownPaving>();
        /// <summary>Market stalls in the plaza (world position + facing).</summary>
        public readonly List<KeyValuePair<Vector3, Vector3>> Stalls = new List<KeyValuePair<Vector3, Vector3>>();
        /// <summary>Trees (cypresses) in gardens and at the plaza corners.</summary>
        public readonly List<Vector3> Trees = new List<Vector3>();
        /// <summary>Street torches (world).</summary>
        public readonly List<Vector3> Torches = new List<Vector3>();

        public Vector3 Fountain { get; private set; }
        public Vector3 ArenaCenter { get; private set; }
        public Vector3 ArenaGate { get; private set; }     // where you stand to enter the arena
        public Vector3 ArenaFacing { get; private set; }   // from the arena towards the town
        public Vector3 LudusCenter { get; private set; }
        public Vector3 LudusGate { get; private set; }     // where you stand to go back to the ludus
        public Vector3 LudusFacing { get; private set; }   // from the ludus towards the town
        /// <summary>Corners of the town wall (template rectangle, world).</summary>
        public Vector3[] WallCorners { get; private set; }

        // ---- template (local space: arena north +z, ludus south -z)
        public const float PlazaHalf = 12f;
        public const float MainHalfW = 3.5f, CrossHalfW = 3f, SouthHalfW = 3f;
        public const float MainEnd = 70f, CrossEnd = 58f, SouthEnd = -44f;
        public const float ArenaZ = 92f, ArenaRadius = 20f;
        public const float LudusZ = -60f, LudusHalfX = 14f, LudusHalfZ = 12f;
        public const float WallX = 66f, WallZMin = -78f, WallZMax = 118f;
        // the street grid (template space)
        public const float SideX = 30f, SideHalfW = 2.5f, RingX = 63f, RingHalfW = 2f, GridZMin = -46f;
        public static readonly float[] StreetsZ = { -42f, 34f, 66f };
        public const float LotWidth = 9.6f, LotDepth = 10f, LotPitch = 11f, Setback = 1.2f;
        public const float InfirmaryX = SideX + SideHalfW + 1.2f + 5.5f, InfirmaryZ = 78f;
        /// <summary>The infirmary's lot (beds inside: TownBuilder.Infirmary).</summary>
        public TownLot InfirmaryLot { get; private set; }

        Quaternion rot;

        Vector3 W(Vector3 local)
        {
            if (Mirrored) local.x = -local.x;
            return rot * local;
        }

        Vector3 Wd(Vector3 dir) => W(dir).normalized;

        /// <summary>Template point (arena north, ludus south, plaza at the origin) to world.</summary>
        public Vector3 ToWorld(Vector3 local) => W(local); // directions (no translation in the template anyway)

        public static TownLayout Generate(int seed)
        {
            var t = new TownLayout { Seed = seed };
            t.Build();
            return t;
        }

        void Build()
        {
            var rng = new System.Random(Seed);
            RotationDeg = rng.Next(4) * 90f;
            Mirrored = rng.Next(2) == 1;
            rot = Quaternion.Euler(0f, RotationDeg, 0f);

            // ---- paving: the plaza, then a regular grid of streets (seen from above the town is a chequerboard of blocks)
            AddPaving(Vector3.zero, new Vector2(PlazaHalf * 2f, PlazaHalf * 2f), 0f, true);
            // north-south: the main street (ludus gate -> arena), two side streets, and one along each wall
            AddPaving(new Vector3(0, 0, (GridZMin + MainEnd) * 0.5f), new Vector2(MainHalfW * 2f, MainEnd - GridZMin), 0f, false);
            foreach (float x in new[] { -SideX, SideX })
                AddPaving(new Vector3(x, 0, (WallZMin + 2f + WallZMax - 2f) * 0.5f), new Vector2(SideHalfW * 2f, WallZMax - WallZMin - 4f), 0f, false);
            foreach (float x in new[] { -RingX, RingX })
                AddPaving(new Vector3(x, 0, (WallZMin + 2f + WallZMax - 2f) * 0.5f), new Vector2(RingHalfW * 2f, WallZMax - WallZMin - 4f), 0f, false);
            // east-west: the cross street through the plaza and three more
            AddPaving(new Vector3(0, 0, 0), new Vector2(RingX * 2f + RingHalfW * 2f, CrossHalfW * 2f), 0f, false);
            foreach (float z in StreetsZ)
                AddPaving(new Vector3(0, 0, z), new Vector2(RingX * 2f + RingHalfW * 2f, SideHalfW * 2f), 0f, false);
            // forecourt in front of the arena
            AddPaving(new Vector3(0, 0, MainEnd + 1.5f), new Vector2(18f, 5f), 0f, true);

            // ---- the plaza's eight frontages hold the special buildings (shuffled by the seed)
            var specials = new List<BuildingKind> { BuildingKind.Temple, BuildingKind.Tavern, BuildingKind.Smithy, BuildingKind.Bakery,
                                                    BuildingKind.Baths, BuildingKind.Pottery, BuildingKind.Forum, BuildingKind.Shop };
            // two smithies in every town (weapons, armour, shields): this one on the plaza, the other by the ludus gate
            Shuffle(specials, rng);
            // Temple always on the north side (looking down the plaza from the arena road), as in a forum.
            specials.Remove(BuildingKind.Temple);
            specials.Insert(rng.Next(2), BuildingKind.Temple);
            var taken = new List<Rect> { new Rect(-PlazaHalf - 1f, -PlazaHalf - 1f, PlazaHalf * 2f + 2f, PlazaHalf * 2f + 2f) }; // template space
            var plazaSlots = new List<(Vector3 c, Vector3 f)>
            {
                (new Vector3(-8f, 0, PlazaHalf + 5.5f), Vector3.back), (new Vector3(8f, 0, PlazaHalf + 5.5f), Vector3.back),
                (new Vector3(-8f, 0, -PlazaHalf - 5.5f), Vector3.forward), (new Vector3(8f, 0, -PlazaHalf - 5.5f), Vector3.forward),
                (new Vector3(PlazaHalf + 5.5f, 0, 7.5f), Vector3.left), (new Vector3(PlazaHalf + 5.5f, 0, -7.5f), Vector3.left),
                (new Vector3(-PlazaHalf - 5.5f, 0, 7.5f), Vector3.right), (new Vector3(-PlazaHalf - 5.5f, 0, -7.5f), Vector3.right),
            };
            for (int i = 0; i < plazaSlots.Count; i++)
            {
                var kind = specials[i];
                float w = kind == BuildingKind.Temple ? 8f : 7.4f + (float)rng.NextDouble() * 0.6f;
                float d = kind == BuildingKind.Temple ? 9.5f : 8f + (float)rng.NextDouble() * 1.5f;
                AddLot(rng, kind, plazaSlots[i].c, plazaSlots[i].f, w, d);
                bool alongZ = Mathf.Abs(plazaSlots[i].f.z) > 0.5f;
                taken.Add(new Rect(plazaSlots[i].c.x - (alongZ ? w : d) * 0.5f, plazaSlots[i].c.z - (alongZ ? d : w) * 0.5f, alongZ ? w : d, alongZ ? d : w));
            }

            // ---- the infirmary (valetudinarium): a long hall of beds in the block by the arena, facing a side street.
            // Where a beaten gladiator wakes up.
            AddLot(rng, BuildingKind.Infirmary, new Vector3(InfirmaryX, 0, InfirmaryZ), Vector3.left, 13f, 11f);
            Lots[Lots.Count - 1].Floors = 1;
            Lots[Lots.Count - 1].DoorOffset = 0f;
            Lots[Lots.Count - 1].StreetPath = true;
            InfirmaryLot = Lots[Lots.Count - 1];

            // ---- the blocks: between the north-south streets, two rows of equal plots back to back, each row facing
            // its own street; along every street the doors of both sides face each other. Plots are skipped where the
            // plaza (and its special buildings), the arena, the ludus or the infirmary stand.
            bool Blocked(float cx, float cz, float hx, float hz)
            {
                var me = new Rect(cx - hx - 0.6f, cz - hz - 0.6f, hx * 2f + 1.2f, hz * 2f + 1.2f);
                foreach (var t in taken) if (me.Overlaps(t)) return true;                                       // plaza and its buildings
                float nx = Mathf.Clamp(0f, cx - hx, cx + hx), nz = Mathf.Clamp(ArenaZ, cz - hz, cz + hz);
                if (new Vector2(nx, nz - ArenaZ).magnitude < ArenaRadius + 4f) return true;                    // arena
                if (cx + hx > -LudusHalfX - 3f && cx - hx < LudusHalfX + 3f && cz - hz < LudusZ + LudusHalfZ + 3f) return true; // ludus
                if (cx + hx > InfirmaryX - 6.5f && cx - hx < InfirmaryX + 6.5f && cz + hz > InfirmaryZ - 7.5f && cz - hz < InfirmaryZ + 7.5f) return true;
                return false;
            }
            var gridLots = new List<TownLot>();
            var columns = new[] { (MainHalfW, SideX - SideHalfW), (SideX + SideHalfW, RingX - RingHalfW) };
            var rows = new List<(float, float)>();
            {
                var zs = new List<float> { WallZMin + 2f };
                var lines = new List<(float z, float half)> { (0f, CrossHalfW) };
                foreach (float z in StreetsZ) lines.Add((z, SideHalfW));
                lines.Sort((u, v) => u.z.CompareTo(v.z));
                float start = WallZMin + 2f;
                foreach (var l in lines) { rows.Add((start, l.z - l.half)); start = l.z + l.half; }
                rows.Add((start, WallZMax - 2f));
            }
            foreach (int sign in new[] { 1, -1 })
                foreach (var (c0, c1) in columns)
                    foreach (var (z0, z1) in rows)
                    {
                        int n = Mathf.FloorToInt((z1 - z0 - 1f) / LotPitch);
                        if (n <= 0) continue;
                        float first = z0 + (z1 - z0 - n * LotPitch) * 0.5f + LotPitch * 0.5f;
                        // the two rows of this block: one facing the inner street, one facing the outer street
                        foreach (int side in new[] { -1, 1 })
                        {
                            float cxAbs = side < 0 ? c0 + Setback + LotDepth * 0.5f : c1 - Setback - LotDepth * 0.5f;
                            float cx = cxAbs * sign;
                            Vector3 front = side < 0 ? (sign > 0 ? Vector3.left : Vector3.right) : (sign > 0 ? Vector3.right : Vector3.left);
                            bool onMain = side < 0 && Mathf.Approximately(c0, MainHalfW);
                            for (int i = 0; i < n; i++)
                            {
                                float cz = first + i * LotPitch;
                                if (Blocked(cx, cz, LotDepth * 0.5f, LotWidth * 0.5f)) continue;
                                double r = rng.NextDouble();
                                BuildingKind kind = onMain
                                    ? (r < 0.3 ? BuildingKind.Domus : r < 0.58 ? BuildingKind.Insula : r < 0.92 ? BuildingKind.Shop : BuildingKind.Garden)
                                    : (r < 0.6 ? BuildingKind.Domus : r < 0.92 ? BuildingKind.Insula : BuildingKind.Garden);
                                AddLot(rng, kind, new Vector3(cx, 0, cz), front, LotWidth, LotDepth);
                                var lot = Lots[Lots.Count - 1];
                                lot.DoorOffset = 0f;
                                lot.StreetPath = kind != BuildingKind.Garden;
                                if (kind == BuildingKind.Domus && rng.NextDouble() < 0.35) { lot.Large = true; lot.Floors = 1; }
                                gridLots.Add(lot);
                            }
                        }
                    }
            // the second smithy: the plot nearest the ludus gate (the first thing you see coming out)
            {
                TownLot best = null; float bd = float.MaxValue;
                Vector3 gate = W(new Vector3(0, 0, LudusZ + LudusHalfZ + 2.2f));
                foreach (var lot in gridLots)
                {
                    if (lot.Kind == BuildingKind.Garden) continue;
                    float d = (lot.Door - gate).sqrMagnitude;
                    if (d < bd) { bd = d; best = lot; }
                }
                if (best != null) { best.Kind = BuildingKind.Smithy; best.Floors = 1; best.Large = false; }
            }

            // ---- landmarks
            Fountain = W(Vector3.zero);
            ArenaCenter = W(new Vector3(0, 0, ArenaZ));
            ArenaFacing = Wd(Vector3.back);
            ArenaGate = W(new Vector3(0, 0, ArenaZ - ArenaRadius - 2.2f));
            LudusCenter = W(new Vector3(0, 0, LudusZ));
            LudusFacing = Wd(Vector3.forward);
            LudusGate = W(new Vector3(0, 0, LudusZ + LudusHalfZ + 2.2f));
            WallCorners = new[]
            {
                W(new Vector3(-WallX, 0, WallZMin)), W(new Vector3(WallX, 0, WallZMin)),
                W(new Vector3(WallX, 0, WallZMax)), W(new Vector3(-WallX, 0, WallZMax)),
            };

            // market stalls along the plaza's east and west sides, facing in
            foreach (float z in new[] { -6f, 0f, 6f })
            {
                if (rng.NextDouble() < 0.85) Stalls.Add(new KeyValuePair<Vector3, Vector3>(W(new Vector3(-PlazaHalf + 2.2f, 0, z)), Wd(Vector3.right)));
                if (rng.NextDouble() < 0.85) Stalls.Add(new KeyValuePair<Vector3, Vector3>(W(new Vector3(PlazaHalf - 2.2f, 0, z)), Wd(Vector3.left)));
            }
            // cypresses at the plaza's corners and in the corner gardens
            foreach (var c in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
            {
                Trees.Add(W(new Vector3(c.x * (PlazaHalf - 1.2f), 0, c.z * (PlazaHalf - 1.2f))));
                Trees.Add(W(new Vector3(c.x * (PlazaHalf + 5f), 0, c.z * (PlazaHalf + 5f))));
                Trees.Add(W(new Vector3(c.x * (PlazaHalf + 7.5f), 0, c.z * (PlazaHalf + 3.5f))));
            }
            // no cypress inside a building
            Trees.RemoveAll(t => Lots.Exists(l =>
            {
                Vector3 d = t - l.Center;
                return Mathf.Abs(Vector3.Dot(d, l.Right)) < l.Width * 0.5f + 0.8f && Mathf.Abs(Vector3.Dot(d, l.Front)) < l.Depth * 0.5f + 0.8f;
            }));
            // torches: plaza corners + the two gates
            foreach (var c in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, 1) })
                Torches.Add(W(new Vector3(c.x * (PlazaHalf - 3f), 0, c.z * (PlazaHalf - 3f))));
            Torches.Add(W(new Vector3(-5f, 0, ArenaZ - ArenaRadius - 1.2f)));
            Torches.Add(W(new Vector3(5f, 0, ArenaZ - ArenaRadius - 1.2f)));
            Torches.Add(W(new Vector3(-4.5f, 0, LudusZ + LudusHalfZ + 1.2f)));
            Torches.Add(W(new Vector3(4.5f, 0, LudusZ + LudusHalfZ + 1.2f)));

            // ---- named places for the simulation
            Places.Add(new TownPlace { Id = "plaza", Name = "Plaza", Kind = PlaceKind.Plaza, Position = W(new Vector3(0, 0, -6f)) });
            Places.Add(new TownPlace { Id = "fountain", Name = "Fuente", Kind = PlaceKind.Fountain, Position = W(new Vector3(0, 0, 3.4f)) });
            for (int i = 0; i < Stalls.Count; i++)
                Places.Add(new TownPlace { Id = "stall" + i, Name = "Puesto del mercado " + (i + 1), Kind = PlaceKind.Market, Position = Stalls[i].Key + Stalls[i].Value * 1.4f });
            Places.Add(new TownPlace { Id = "arena_gate", Name = "Puerta de la arena", Kind = PlaceKind.ArenaGate, Position = ArenaGate });
            // guard posts: the plaza corners, the ends of the cross street and both sides of the arena forecourt
            void Post(string id, Vector3 local, Vector3 lookLocal) =>
                Places.Add(new TownPlace { Id = id, Name = "Puesto de guardia", Kind = PlaceKind.GuardPost, Position = W(local), Facing = Wd(lookLocal) });
            Post("guard_plaza0", new Vector3(-PlazaHalf + 1.5f, 0, PlazaHalf - 1.5f), new Vector3(1, 0, -1));
            Post("guard_plaza1", new Vector3(PlazaHalf - 1.5f, 0, PlazaHalf - 1.5f), new Vector3(-1, 0, -1));
            Post("guard_plaza2", new Vector3(-PlazaHalf + 1.5f, 0, -PlazaHalf + 1.5f), new Vector3(1, 0, 1));
            Post("guard_plaza3", new Vector3(PlazaHalf - 1.5f, 0, -PlazaHalf + 1.5f), new Vector3(-1, 0, 1));
            Post("guard_east", new Vector3(CrossEnd - 2f, 0, 0), Vector3.left);
            Post("guard_west", new Vector3(-CrossEnd + 2f, 0, 0), Vector3.right);
            Post("guard_forum_l", new Vector3(-7f, 0, MainEnd + 1.5f), Vector3.back);
            Post("guard_forum_r", new Vector3(7f, 0, MainEnd + 1.5f), Vector3.back);
            Places.Add(new TownPlace { Id = "ludus_gate", Name = "Puerta del ludus", Kind = PlaceKind.LudusGate, Position = LudusGate });
            // ---- two-storey homes: an outside staircase along one side (the house gives up 1.3 m of its width for it)
            foreach (var lot in Lots)
            {
                if (!lot.IsHome || lot.Floors < 2) continue;
                lot.StairSide = rng.Next(2) == 0 ? -1 : 1;
                if (lot.Floors >= 3) { lot.Width -= 2.6f; continue; } // stairs on both sides: up to the 1st floor, then the 2nd
                lot.Width -= 1.3f;
                lot.Center -= lot.Right * (lot.StairSide * 0.65f);
            }

            // ---- a paved path from every door across the strip of sand to the street
            foreach (var lot in Lots)
            {
                if (!lot.StreetPath) continue;
                Vector3 face = lot.Center + lot.Front * (lot.Depth * 0.5f) + lot.Right * (lot.DoorOffset * lot.Width);
                float len = Setback + 0.4f;
                Paving.Add(new TownPaving { Center = face + lot.Front * (len * 0.5f), Size = new Vector2(1.8f, len), Yaw = Quaternion.LookRotation(lot.Front).eulerAngles.y, Plaza = true });
            }

            // ---- four empty houses for sale, one in each quarter of the town, away from the plaza
            var inv = Quaternion.Inverse(rot);
            for (int quarter = 0; quarter < 4; quarter++)
            {
                TownLot pick = null; float bestScore = -1f;
                foreach (var lot in Lots)
                {
                    if (lot.Kind != BuildingKind.Domus || lot.ForSale || lot.Large) continue;
                    Vector3 l = inv * lot.Center; if (Mirrored) l.x = -l.x;
                    int qd = (l.x >= 0 ? 1 : 0) + (l.z >= 0 ? 2 : 0);
                    if (qd != quarter) continue;
                    float dist = new Vector2(l.x, l.z).magnitude;
                    if (dist < 22f) continue;
                    float score = dist * (0.6f + (float)rng.NextDouble() * 0.8f);
                    if (score > bestScore) { bestScore = score; pick = lot; }
                }
                if (pick != null) { pick.ForSale = true; pick.Price = 250 + rng.Next(0, 6) * 50; }
            }

            int homes = 0;
            foreach (var lot in Lots)
            {
                if (lot.Kind == BuildingKind.Garden) continue;
                if (lot.ForSale) { homes++; continue; } // empty: nobody lives there
                bool home = lot.IsHome;
                string name = home ? (lot.Kind == BuildingKind.Insula ? "Insula " : lot.Large ? "Casa grande " : "Casa ") + (++homes) : KindName(lot.Kind);
                Places.Add(new TownPlace { Id = (home ? "home" : "work") + lot.Id, Name = name, Kind = home ? PlaceKind.Home : PlaceKind.Work, Position = lot.Door, LotId = lot.Id });
            }
        }

        public static string KindName(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.Temple: return "Templo";
                case BuildingKind.Tavern: return "Taberna";
                case BuildingKind.Smithy: return "Herreria";
                case BuildingKind.Bakery: return "Panaderia";
                case BuildingKind.Baths: return "Termas";
                case BuildingKind.Pottery: return "Alfareria";
                case BuildingKind.Forum: return "Basilica";
                case BuildingKind.Shop: return "Tienda";
                case BuildingKind.Insula: return "Insula";
                case BuildingKind.Garden: return "Huerto";
                case BuildingKind.Infirmary: return "Enfermeria";
                default: return "Casa";
            }
        }

        void AddPaving(Vector3 localCenter, Vector2 size, float yaw, bool plaza)
        {
            Paving.Add(new TownPaving { Center = W(localCenter), Size = size, Yaw = RotationDeg + (Mirrored ? -yaw : yaw), Plaza = plaza });
        }

        void AddLot(System.Random rng, BuildingKind kind, Vector3 localCenter, Vector3 localFront, float width, float depth)
        {
            int floors = kind == BuildingKind.Insula ? 2 + (rng.NextDouble() < 0.35 ? 1 : 0)
                       : kind == BuildingKind.Domus || kind == BuildingKind.Garden ? 1
                       : kind == BuildingKind.Temple || kind == BuildingKind.Baths || kind == BuildingKind.Forum ? 1
                       : rng.NextDouble() < 0.5 ? 2 : 1;
            bool shop = kind == BuildingKind.Shop || kind == BuildingKind.Tavern || kind == BuildingKind.Smithy ||
                        kind == BuildingKind.Bakery || kind == BuildingKind.Pottery;
            if (shop) floors = 1; // shops are one storey you can walk into (the dice above still roll: same towns)
            Lots.Add(new TownLot
            {
                Id = Lots.Count, Kind = kind, Center = W(localCenter), Front = Wd(localFront), Width = width, Depth = depth, Floors = floors,
                Tint = rng.Next(6), RoofTint = rng.Next(4), DoorOffset = ((float)rng.NextDouble() - 0.5f) * 0.5f,
            });
        }

        static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>The first place of a kind (or with this building), for quick lookups.</summary>
        public TownPlace Find(string id) => Places.Find(p => p.Id == id);
    }
}
