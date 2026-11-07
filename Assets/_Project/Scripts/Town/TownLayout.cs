using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>What stands on a lot.</summary>
    public enum BuildingKind { Domus, Insula, Shop, Garden, Temple, Tavern, Smithy, Bakery, Baths, Pottery, Forum }

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
        public int Price;
        /// <summary>World point just outside the front door (where people go in and out).</summary>
        public Vector3 Door => Center + Front * (Depth * 0.5f + 0.9f) + Right * (DoorOffset * Width);
        public Vector3 Right => Vector3.Cross(Vector3.up, Front).normalized;
        public Quaternion Rotation => Quaternion.LookRotation(Front);
        public bool IsHome => Kind == BuildingKind.Domus || Kind == BuildingKind.Insula;
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

            // ---- paving
            AddPaving(Vector3.zero, new Vector2(PlazaHalf * 2f, PlazaHalf * 2f), 0f, true);
            AddPaving(new Vector3(0, 0, (PlazaHalf + MainEnd + 2f) * 0.5f), new Vector2(MainHalfW * 2f, MainEnd + 2f - PlazaHalf), 0f, false);
            AddPaving(new Vector3(0, 0, (-PlazaHalf + SouthEnd) * 0.5f), new Vector2(SouthHalfW * 2f, PlazaHalf - SouthEnd), 0f, false);
            AddPaving(new Vector3((PlazaHalf + CrossEnd) * 0.5f, 0, 0), new Vector2(CrossEnd - PlazaHalf, CrossHalfW * 2f), 0f, false);
            AddPaving(new Vector3(-(PlazaHalf + CrossEnd) * 0.5f, 0, 0), new Vector2(CrossEnd - PlazaHalf, CrossHalfW * 2f), 0f, false);
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
            }

            // ---- street lots (houses, shops, gardens)
            void Street(Vector3 from, Vector3 along, float length, float halfW, float spacing)
            {
                Vector3 side = Vector3.Cross(Vector3.up, along);
                for (float s = spacing * 0.5f; s < length - spacing * 0.4f; s += spacing)
                    foreach (int sign in new[] { -1, 1 })
                    {
                        float depth = 7.5f + (float)rng.NextDouble() * 3f;
                        float width = spacing - 0.8f - (float)rng.NextDouble() * 1.2f;
                        Vector3 c = from + along * s + side * sign * (halfW + 0.8f + depth * 0.5f);
                        double r = rng.NextDouble();
                        var kind = r < 0.42 ? BuildingKind.Domus : r < 0.74 ? BuildingKind.Insula : r < 0.9 ? BuildingKind.Shop : BuildingKind.Garden;
                        AddLot(rng, kind, c, -side * sign, width, depth);
                    }
            }
            Street(new Vector3(0, 0, PlazaHalf + 11f), Vector3.forward, MainEnd - PlazaHalf - 13f, MainHalfW, 9f);
            Street(new Vector3(PlazaHalf + 11f, 0, 0), Vector3.right, CrossEnd - PlazaHalf - 11f, CrossHalfW, 9f);
            Street(new Vector3(-PlazaHalf - 11f, 0, 0), Vector3.left, CrossEnd - PlazaHalf - 11f, CrossHalfW, 9f);
            int southFirst = Lots.Count;
            Street(new Vector3(0, 0, -PlazaHalf - 11f), Vector3.back, -SouthEnd - PlazaHalf - 11f, SouthHalfW, 9f);
            // the second smithy: the last lot of the street to the ludus, by its gate (the first thing you see coming out)
            {
                int best = -1; float far = -1f;
                for (int i = southFirst; i < Lots.Count; i++)
                {
                    float d = (Lots[i].Center - W(Vector3.zero)).sqrMagnitude;
                    if (d > far) { far = d; best = i; }
                }
                if (best >= 0) { Lots[best].Kind = BuildingKind.Smithy; Lots[best].Floors = 1; }
            }

            // ---- back rows: the four quarters behind the streets fill up with houses (more homes for the townsfolk)
            void Fill(float x0, float x1, float z0, float z1)
            {
                for (float x = x0; x <= x1 + 0.01f; x += 11f)
                    for (float z = z0; z <= z1 + 0.01f; z += 11f)
                    {
                        if (rng.NextDouble() < 0.22) continue; // an empty plot here and there
                        Vector3 c = new Vector3(x + ((float)rng.NextDouble() - 0.5f) * 2f, 0, z + ((float)rng.NextDouble() - 0.5f) * 2f);
                        // face the nearer of the two big streets
                        Vector3 front = Mathf.Abs(c.x) < Mathf.Abs(c.z) ? (c.x > 0 ? Vector3.left : Vector3.right)
                                                                         : (c.z > 0 ? Vector3.back : Vector3.forward);
                        double r = rng.NextDouble();
                        var kind = r < 0.55 ? BuildingKind.Domus : r < 0.9 ? BuildingKind.Insula : BuildingKind.Garden;
                        AddLot(rng, kind, c, front, 6.5f + (float)rng.NextDouble() * 2.5f, 6.5f + (float)rng.NextDouble() * 2.5f);
                    }
            }
            Fill(26f, 59f, 27f, 60f);
            Fill(-59f, -26f, 27f, 60f);
            Fill(26f, 59f, -66f, -29f);
            Fill(-59f, -26f, -66f, -29f);

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
                lot.Width -= 1.3f;
                lot.Center -= lot.Right * (lot.StairSide * 0.65f);
            }

            // ---- four empty houses for sale, one in each quarter of the town, away from the plaza
            var inv = Quaternion.Inverse(rot);
            for (int quarter = 0; quarter < 4; quarter++)
            {
                TownLot pick = null; float bestScore = -1f;
                foreach (var lot in Lots)
                {
                    if (lot.Kind != BuildingKind.Domus || lot.ForSale) continue;
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
                string name = home ? (lot.Kind == BuildingKind.Insula ? "Insula " : "Casa ") + (++homes) : KindName(lot.Kind);
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
