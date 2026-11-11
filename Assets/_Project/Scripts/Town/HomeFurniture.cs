using System.Collections.Generic;
using Gladiator.Combat;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The furniture the carpenter sells for the houses you buy (they come empty): a bed (single, or double for two),
    /// a table with chairs, a kitchen range, a bath and a chest. What each house has is saved per town and per house;
    /// the pieces are built into the house's rooms (same places as in the neighbours' homes). Beds and chairs work with
    /// E like any other; the chest shows your inventory.
    /// </summary>
    public static class HomeFurniture
    {
        public class Item
        {
            public string Id, Name, Blurb;
            public int Price;
        }

        public static readonly Item[] Catalog =
        {
            new Item { Id = "bed", Name = "CAMA SIMPLE", Price = 60, Blurb = "Una cama para el dormitorio: duerme una persona. Dormir pasa las horas y cura." },
            new Item { Id = "bed2", Name = "CAMA DOBLE", Price = 110, Blurb = "Cama ancha con dos almohadas: pueden acostarse dos, una de cada lado." },
            new Item { Id = "table", Name = "MESA Y SILLAS", Price = 70, Blurb = "Una mesa con dos sillas para la cocina: te puedes sentar." },
            new Item { Id = "kitchen", Name = "COCINA", Price = 120, Blurb = "Fogon de obra con horno, olla y repisa: la cocina de la casa." },
            new Item { Id = "bath", Name = "BANO", Price = 90, Blurb = "Banera de piedra y letrina para el cuarto de bano." },
            new Item { Id = "chest", Name = "COFRE", Price = 50, Blurb = "Un cofre con herrajes: abrelo (E) para ver tu inventario." },
            new Item { Id = "shelf", Name = "ESTANTE", Price = 35, Blurb = "Un estante con jarras para la cocina." },
        };

        public static Item ById(string id) { foreach (var i in Catalog) if (i.Id == id) return i; return null; }

        const string Key = "Gladiator.Furniture.";
        static string K(int seed, int lot) => Key + seed + "." + lot;

        public static List<string> Owned(int seed, int lot)
        {
            var list = new List<string>();
            foreach (var part in PlayerPrefs.GetString(K(seed, lot), "").Split(','))
                if (!string.IsNullOrEmpty(part) && ById(part) != null && !list.Contains(part)) list.Add(part);
            return list;
        }

        public static bool Has(int seed, int lot, string id) => Owned(seed, lot).Contains(id);

        public static void Add(int seed, int lot, string id)
        {
            var list = Owned(seed, lot);
            if (list.Contains(id)) return;
            list.Add(id);
            PlayerPrefs.SetString(K(seed, lot), string.Join(",", list));
            PlayerPrefs.Save();
        }

        public static void Clear(int seed, int lot) { PlayerPrefs.DeleteKey(K(seed, lot)); }

        /// <summary>Pieces already standing in a house (so nothing is built twice).</summary>
        class Built : MonoBehaviour { public readonly HashSet<string> Ids = new HashSet<string>(); }

        public static bool IsBuilt(HouseInterior house, string id)
        {
            var b = house != null ? house.GetComponent<Built>() : null;
            return b != null && b.Ids.Contains(id);
        }

        static Material M(string name, Texture2D tex, Color c) => PS2Materials.Get(name, tex, c);

        /// <summary>Builds the pieces of <paramref name="ids"/> that are not in the house yet. Returns how many it built.</summary>
        public static int Apply(HouseInterior house, IEnumerable<string> ids)
        {
            if (house == null || ids == null) return 0;
            var built = house.GetComponent<Built>();
            if (built == null) built = house.gameObject.AddComponent<Built>();
            int n = 0;
            foreach (var id in ids)
            {
                if (built.Ids.Contains(id)) continue;
                if (id == "bed" && built.Ids.Contains("bed2")) continue; // the double bed took its place
                var holder = new GameObject("Furniture_" + id);
                holder.transform.SetParent(house.transform, false);
                holder.transform.localPosition = new Vector3(0f, house.Floor == 0 ? 0.05f : 0.19f, 0f);
                if (!Build(house, id, holder.transform)) { Object.Destroy(holder); continue; }
                if (id == "bed2")
                {
                    // the single bed gives way to the double one
                    var old = house.transform.Find("Furniture_bed");
                    if (old != null) Object.Destroy(old.gameObject);
                    house.Furniture.RemoveAll(f => f.Kind == Fighter.ActLie && f.Tag == "bed");
                }
                LowPolyMesh.BakeAmbient(holder, TownBuilder.AmbientSky, TownBuilder.AmbientGround);
                built.Ids.Add(id);
                n++;
            }
            return n;
        }

        static bool Room(HouseInterior h, string name, out HouseInterior.Room r)
        {
            foreach (var x in h.Rooms) if (x.Name == name) { r = x; return true; }
            r = default;
            return false;
        }

        static GameObject Box(Transform p, Material m, float x, float y, float z, Vector3 size, bool col = false, float yaw = 0f)
        {
            var go = PS2Spawn.Box(p, "P", size, new Vector3(x, y, z), m, yaw != 0f ? Quaternion.Euler(0f, yaw, 0f) : (Quaternion?)null);
            if (col) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        static bool Build(HouseInterior h, string id, Transform t)
        {
            var wood = M("furn_wood", PS2Textures.Wood, new Color(0.62f, 0.44f, 0.26f));
            var dark = M("furn_dark", PS2Textures.Wood, new Color(0.36f, 0.24f, 0.14f));
            var linen = M("furn_linen", PS2Textures.Cloth, new Color(0.9f, 0.85f, 0.7f));
            var blanket = M("furn_blanket", PS2Textures.Cloth, new Color(0.25f, 0.35f, 0.6f));
            var stone = M("furn_stone", PS2Textures.Stone, new Color(0.7f, 0.68f, 0.62f));
            var iron = M("furn_iron", PS2Textures.Metal, new Color(0.3f, 0.3f, 0.32f));
            var clay = M("furn_clay", PS2Textures.Stone, new Color(0.75f, 0.42f, 0.25f));
            var water = M("furn_water", PS2Textures.White, new Color(0.3f, 0.55f, 0.7f));
            var black = M("furn_black", PS2Textures.White, new Color(0.06f, 0.05f, 0.05f));
            var ember = PS2Materials.Flat(new Color(1f, 0.5f, 0.12f), 0.3f);
            float y0 = t.localPosition.y;
            Pose P(float x, float y, float z, Vector3 look) => new Pose(h.transform.TransformPoint(new Vector3(x, y0 + y, z)), h.transform.rotation * Quaternion.LookRotation(look));

            switch (id)
            {
                case "bed":
                case "bed2":
                {
                    if (!Room(h, "HABITACION", out var r)) return false;
                    bool dbl = id == "bed2";
                    float w = dbl ? 1.5f : 0.9f, cx = r.X0 + 0.14f + w * 0.5f, z0 = r.Z0 + 0.02f, bz = z0 + 1.05f;
                    Box(t, dark, cx, 0.2f, bz, new Vector3(w, 0.4f, 2.0f), true);
                    Box(t, dark, cx, 0.55f, z0 + 0.08f, new Vector3(w, 0.7f, 0.08f));
                    Box(t, linen, cx, 0.46f, bz, new Vector3(w - 0.1f, 0.14f, 1.9f));
                    Box(t, blanket, cx, 0.55f, bz + 0.25f, new Vector3(w - 0.06f, 0.05f, 1.3f));
                    foreach (float px in dbl ? new[] { -0.35f, 0.35f } : new[] { 0f })
                    {
                        Box(t, linen, cx + px, 0.58f, z0 + 0.38f, new Vector3(0.5f, 0.1f, 0.32f));
                        h.Furniture.Add(new FurnitureSpot(Fighter.ActLie, P(cx + px, 0.53f, z0 + 0.83f, Vector3.forward)) { Tag = id });
                    }
                    return true;
                }
                case "chest":
                {
                    if (!Room(h, "HABITACION", out var r)) return false;
                    float cx = r.X1 - 0.7f, cz = r.Z0 + 0.42f;
                    Box(t, dark, cx, 0.25f, cz, new Vector3(0.85f, 0.5f, 0.5f), true);
                    Box(t, wood, cx, 0.52f, cz, new Vector3(0.88f, 0.08f, 0.53f));
                    foreach (float e in new[] { -0.3f, 0.3f }) Box(t, iron, cx + e, 0.3f, cz, new Vector3(0.06f, 0.56f, 0.55f));
                    Box(t, iron, cx, 0.38f, cz + 0.26f, new Vector3(0.1f, 0.1f, 0.03f));
                    h.Chests.Add(h.transform.TransformPoint(new Vector3(cx, y0, cz)));
                    return true;
                }
                case "table":
                {
                    if (!Room(h, "COCINA", out var r)) return false;
                    float tx = r.X0 + 1.7f, tz = (r.Z0 + r.Z1) * 0.5f + 0.2f;
                    Box(t, wood, tx, 0.74f, tz, new Vector3(1.25f, 0.07f, 0.72f), true);
                    foreach (float sx in new[] { -0.52f, 0.52f }) foreach (float sz in new[] { -0.28f, 0.28f })
                        Box(t, dark, tx + sx, 0.37f, tz + sz, new Vector3(0.07f, 0.74f, 0.07f));
                    Box(t, clay, tx - 0.25f, 0.84f, tz + 0.05f, new Vector3(0.16f, 0.16f, 0.16f), false, 45f);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float cz = tz + side * 0.62f;
                        Box(t, dark, tx, 0.44f, cz, new Vector3(0.4f, 0.06f, 0.4f));
                        foreach (float lx in new[] { -0.16f, 0.16f }) foreach (float lz in new[] { -0.16f, 0.16f })
                            Box(t, dark, tx + lx, 0.22f, cz + lz, new Vector3(0.05f, 0.44f, 0.05f));
                        Box(t, dark, tx, 0.75f, cz + side * 0.18f, new Vector3(0.4f, 0.55f, 0.05f));
                        var col = new GameObject("ChairCol"); col.transform.SetParent(t, false);
                        col.transform.localPosition = new Vector3(tx, 0.4f, cz);
                        col.AddComponent<BoxCollider>().size = new Vector3(0.4f, 0.8f, 0.4f);
                        h.Furniture.Add(new FurnitureSpot(Fighter.ActChair, P(tx, 0.02f, cz, new Vector3(0, 0, -side))) { Tag = id });
                    }
                    return true;
                }
                case "kitchen":
                {
                    if (!Room(h, "COCINA", out var r)) return false;
                    const float cd = 0.72f;
                    float rx = r.X1 - cd * 0.5f - 0.02f, rz0 = r.Z0 + 0.25f, rz1 = r.Z1 - 0.1f, len = rz1 - rz0, mid = (rz0 + rz1) * 0.5f;
                    Box(t, stone, rx, 0.45f, mid, new Vector3(cd, 0.9f, len), true);
                    Box(t, iron, rx, 0.93f, mid, new Vector3(cd + 0.04f, 0.06f, len + 0.04f));
                    Box(t, black, rx - cd * 0.5f - 0.005f, 0.3f, rz0 + len * 0.35f, new Vector3(0.01f, 0.32f, len * 0.4f));
                    float oz = rz1 - 0.55f;
                    Box(t, stone, rx, 0.75f, oz, new Vector3(cd + 0.06f, 1.5f, 1.05f), true);
                    Box(t, clay, rx, 1.6f, oz, new Vector3(cd - 0.04f, 0.22f, 0.95f));
                    Box(t, black, rx - cd * 0.5f - 0.04f, 0.95f, oz, new Vector3(0.02f, 0.5f, 0.5f));
                    Box(t, ember, rx - cd * 0.5f - 0.045f, 0.78f, oz, new Vector3(0.02f, 0.12f, 0.42f));
                    float fz = rz0 + Mathf.Min(1.2f, len * 0.4f);
                    Box(t, ember, rx, 0.97f, fz, new Vector3(0.45f, 0.03f, 0.45f));
                    Box(t, iron, rx, 1.12f, fz, new Vector3(0.36f, 0.26f, 0.36f), false, 45f);
                    for (int k = 0; k < 2; k++) Box(t, clay, rx + 0.1f, 1.05f, fz + 0.6f + k * 0.34f, new Vector3(0.18f, 0.22f, 0.18f), false, 45f);
                    var li = new GameObject("KitchenLight").AddComponent<Light>();
                    li.transform.SetParent(t, false);
                    li.transform.localPosition = new Vector3(rx, 1.9f, fz);
                    li.type = LightType.Point; li.range = 5.5f; li.intensity = 1.3f; li.color = new Color(1f, 0.6f, 0.28f);
                    li.shadows = LightShadows.None; li.enabled = false;
                    h.Lights.Add(li);
                    return true;
                }
                case "bath":
                {
                    if (!Room(h, "BANO", out var r)) return false;
                    float bw = r.X1 - r.X0, tubW = Mathf.Max(1.4f, bw - 0.5f), tubD = 1.2f;
                    float cx = (r.X0 + r.X1) * 0.5f, cz = r.Z0 + 0.12f + tubD * 0.5f;
                    Box(t, stone, cx, 0.3f, cz - tubD * 0.5f + 0.08f, new Vector3(tubW, 0.6f, 0.16f));
                    Box(t, stone, cx, 0.3f, cz + tubD * 0.5f - 0.08f, new Vector3(tubW, 0.6f, 0.16f));
                    Box(t, stone, cx - tubW * 0.5f + 0.08f, 0.3f, cz, new Vector3(0.16f, 0.6f, tubD));
                    Box(t, stone, cx + tubW * 0.5f - 0.08f, 0.3f, cz, new Vector3(0.16f, 0.6f, tubD));
                    Box(t, stone, cx, 0.05f, cz, new Vector3(tubW, 0.1f, tubD));
                    Box(t, water, cx, 0.42f, cz, new Vector3(tubW - 0.3f, 0.03f, tubD - 0.3f));
                    var col = new GameObject("TubCol"); col.transform.SetParent(t, false);
                    col.transform.localPosition = new Vector3(cx, 0.3f, cz);
                    col.AddComponent<BoxCollider>().size = new Vector3(tubW, 0.6f, tubD);
                    float lx = r.X0 + 0.4f, lz = r.Z1 - 0.75f;
                    if (lz - 0.45f > cz + tubD * 0.5f + 0.1f)
                    {
                        Box(t, stone, lx, 0.22f, lz, new Vector3(0.6f, 0.44f, 0.9f), true);
                        Box(t, black, lx, 0.445f, lz, new Vector3(0.24f, 0.01f, 0.3f));
                    }
                    return true;
                }
                case "shelf":
                {
                    if (!Room(h, "COCINA", out var r)) return false;
                    float sx = r.X0 + 0.22f, sz = r.Z1 - 1.0f, len = 1.2f;
                    for (int k = 0; k < 4; k++) Box(t, wood, sx, 0.15f + k * 0.55f, sz, new Vector3(0.38f, 0.05f, len));
                    foreach (float e in new[] { -len * 0.48f, len * 0.48f }) Box(t, dark, sx, 0.95f, sz + e, new Vector3(0.38f, 1.9f, 0.05f));
                    for (int k = 0; k < 3; k++) for (int j = 0; j < 3; j++)
                        if ((k + j) % 3 != 0) Box(t, clay, sx, 0.32f + k * 0.55f, sz - len * 0.5f + (j + 0.5f) * len / 3f, new Vector3(0.2f, 0.26f, 0.2f), false, 45f);
                    var col = new GameObject("ShelfCol"); col.transform.SetParent(t, false);
                    col.transform.localPosition = new Vector3(sx, 0.95f, sz);
                    col.AddComponent<BoxCollider>().size = new Vector3(0.4f, 1.9f, len + 0.05f);
                    return true;
                }
            }
            return false;
        }
    }
}
