using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// Builds the low-poly weapons (boxes). The grip is at the origin and the blade/head points along local +Z.
    /// Spears and tridents are held a third of the way up the haft. The bow's limbs run along local Y and an arrow
    /// leaves it along +Z.
    /// </summary>
    public static class WeaponFactory
    {
        public static Weapon Create(WeaponStats stats)
        {
            var root = new GameObject("Weapon_" + stats.Name);
            var metal = PS2Materials.Get("metal_" + ColorUtility.ToHtmlStringRGB(stats.Tint), PS2Textures.Metal, stats.Tint);
            var dark = PS2Materials.Get("metal_dark", PS2Textures.Metal, new Color(0.45f, 0.45f, 0.5f));
            var wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            var leather = PS2Materials.Get("leather", PS2Textures.Leather, Color.white);
            var brass = PS2Materials.Get("brass", PS2Textures.Metal, new Color(1f, 0.78f, 0.35f));

            float L = stats.Length;
            float zMin = -0.15f, zMax = L;
            if (stats.Id == "rudis")
            {
                // Training sword: all wood, darker grip.
                metal = PS2Materials.Get("rudis_wood", PS2Textures.Wood, new Color(0.95f, 0.85f, 0.7f));
                brass = PS2Materials.Get("rudis_dark", PS2Textures.Wood, new Color(0.6f, 0.45f, 0.3f));
            }
            switch (stats.Shape)
            {
                case WeaponShape.Sword:
                    Part(root, "Grip", new Vector3(0.05f, 0.05f, 0.2f), new Vector3(0, 0, 0.0f), leather);
                    Part(root, "Pommel", new Vector3(0.08f, 0.08f, 0.06f), new Vector3(0, 0, -0.12f), brass);
                    Part(root, "Guard", new Vector3(0.22f, 0.05f, 0.05f), new Vector3(0, 0, 0.12f), brass);
                    Part(root, "Blade", new Vector3(0.08f, 0.02f, L), new Vector3(0, 0, 0.145f + L * 0.5f), metal);
                    Part(root, "Tip", new Vector3(0.05f, 0.02f, 0.08f), new Vector3(0, 0, 0.145f + L + 0.03f), metal);
                    zMax = L + 0.2f;
                    break;
                case WeaponShape.Greatsword:
                    Part(root, "Grip", new Vector3(0.055f, 0.055f, 0.42f), new Vector3(0, 0, -0.1f), leather);
                    Part(root, "Pommel", new Vector3(0.1f, 0.1f, 0.08f), new Vector3(0, 0, -0.34f), brass);
                    Part(root, "Guard", new Vector3(0.38f, 0.06f, 0.06f), new Vector3(0, 0, 0.14f), dark);
                    Part(root, "Blade", new Vector3(0.1f, 0.025f, L), new Vector3(0, 0, 0.17f + L * 0.5f), metal);
                    Part(root, "Fuller", new Vector3(0.03f, 0.03f, L * 0.7f), new Vector3(0, 0, 0.17f + L * 0.38f), dark);
                    Part(root, "Tip", new Vector3(0.06f, 0.025f, 0.1f), new Vector3(0, 0, 0.17f + L + 0.04f), metal);
                    zMin = -0.38f; zMax = L + 0.25f;
                    break;
                case WeaponShape.Dagger:
                    Part(root, "Grip", new Vector3(0.045f, 0.045f, 0.13f), new Vector3(0, 0, -0.01f), leather);
                    Part(root, "Pommel", new Vector3(0.07f, 0.07f, 0.04f), new Vector3(0, 0, -0.09f), brass);
                    Part(root, "Guard", new Vector3(0.12f, 0.04f, 0.035f), new Vector3(0, 0, 0.07f), brass);
                    Part(root, "Blade", new Vector3(0.06f, 0.018f, L), new Vector3(0, 0, 0.09f + L * 0.5f), metal);
                    Part(root, "Tip", new Vector3(0.03f, 0.018f, 0.05f), new Vector3(0, 0, 0.09f + L + 0.02f), metal);
                    zMin = -0.1f; zMax = L + 0.12f;
                    break;
                case WeaponShape.Axe:
                    Part(root, "Haft", new Vector3(0.05f, 0.05f, L + 0.25f), new Vector3(0, 0, (L + 0.25f) * 0.5f - 0.15f), wood);
                    Part(root, "Head", new Vector3(0.04f, 0.3f, 0.2f), new Vector3(0, 0.1f, L - 0.08f), metal);
                    Part(root, "Edge", new Vector3(0.03f, 0.36f, 0.05f), new Vector3(0, 0.12f, L + 0.04f), metal);
                    break;
                case WeaponShape.GreatAxe:
                    Part(root, "Haft", new Vector3(0.06f, 0.06f, L + 0.5f), new Vector3(0, 0, (L + 0.5f) * 0.5f - 0.45f), wood);
                    Part(root, "Wrap", new Vector3(0.07f, 0.07f, 0.3f), new Vector3(0, 0, -0.2f), leather);
                    Part(root, "HeadA", new Vector3(0.05f, 0.42f, 0.26f), new Vector3(0, 0.16f, L - 0.1f), metal);
                    Part(root, "EdgeA", new Vector3(0.035f, 0.5f, 0.06f), new Vector3(0, 0.18f, L + 0.05f), metal);
                    Part(root, "HeadB", new Vector3(0.05f, 0.3f, 0.2f), new Vector3(0, -0.12f, L - 0.1f), metal);
                    Part(root, "Spike", new Vector3(0.04f, 0.04f, 0.18f), new Vector3(0, 0, L + 0.12f), dark);
                    zMin = -0.45f; zMax = L + 0.22f;
                    break;
                case WeaponShape.Club: // rudis / old club
                    Part(root, "Haft", new Vector3(0.06f, 0.06f, L), new Vector3(0, 0, L * 0.5f - 0.15f), wood);
                    Part(root, "Head", new Vector3(0.17f, 0.17f, 0.26f), new Vector3(0, 0, L - 0.12f), wood);
                    Part(root, "Band", new Vector3(0.19f, 0.19f, 0.05f), new Vector3(0, 0, L - 0.22f), brass);
                    break;
                case WeaponShape.Mace:
                    Part(root, "Haft", new Vector3(0.05f, 0.05f, L), new Vector3(0, 0, L * 0.5f - 0.15f), dark);
                    Part(root, "Wrap", new Vector3(0.06f, 0.06f, 0.16f), new Vector3(0, 0, -0.04f), leather);
                    Part(root, "Head", new Vector3(0.13f, 0.13f, 0.2f), new Vector3(0, 0, L - 0.08f), metal);
                    Part(root, "FlangeX", new Vector3(0.24f, 0.04f, 0.16f), new Vector3(0, 0, L - 0.08f), metal);
                    Part(root, "FlangeY", new Vector3(0.04f, 0.24f, 0.16f), new Vector3(0, 0, L - 0.08f), metal);
                    Part(root, "Cap", new Vector3(0.06f, 0.06f, 0.06f), new Vector3(0, 0, L + 0.04f), metal);
                    zMax = L + 0.08f;
                    break;
                case WeaponShape.Maul:
                    Part(root, "Haft", new Vector3(0.065f, 0.065f, L + 0.45f), new Vector3(0, 0, (L + 0.45f) * 0.5f - 0.45f), wood);
                    Part(root, "Wrap", new Vector3(0.075f, 0.075f, 0.32f), new Vector3(0, 0, -0.22f), leather);
                    Part(root, "Head", new Vector3(0.22f, 0.22f, 0.4f), new Vector3(0, 0, L - 0.05f), metal);
                    Part(root, "BandA", new Vector3(0.25f, 0.25f, 0.05f), new Vector3(0, 0, L - 0.2f), dark);
                    Part(root, "BandB", new Vector3(0.25f, 0.25f, 0.05f), new Vector3(0, 0, L + 0.1f), dark);
                    zMin = -0.45f; zMax = L + 0.18f;
                    break;
                case WeaponShape.Spear:
                case WeaponShape.Trident:
                {
                    float back = L * 0.32f;
                    Part(root, "Haft", new Vector3(0.045f, 0.045f, L), new Vector3(0, 0, L * 0.5f - back), wood);
                    Part(root, "Butt", new Vector3(0.06f, 0.06f, 0.06f), new Vector3(0, 0, -back), dark);
                    float tip = L - back;
                    if (stats.Shape == WeaponShape.Spear)
                    {
                        Part(root, "Socket", new Vector3(0.06f, 0.06f, 0.1f), new Vector3(0, 0, tip + 0.02f), dark);
                        Part(root, "Blade", new Vector3(0.09f, 0.02f, 0.26f), new Vector3(0, 0, tip + 0.18f), metal);
                        Part(root, "Point", new Vector3(0.04f, 0.02f, 0.08f), new Vector3(0, 0, tip + 0.34f), metal);
                        zMax = tip + 0.38f;
                    }
                    else
                    {
                        Part(root, "Bar", new Vector3(0.3f, 0.035f, 0.04f), new Vector3(0, 0, tip + 0.02f), metal);
                        for (int k = -1; k <= 1; k++)
                        {
                            Part(root, "Prong" + k, new Vector3(0.03f, 0.03f, 0.3f), new Vector3(k * 0.13f, 0, tip + 0.17f), metal);
                            Part(root, "Barb" + k, new Vector3(0.05f, 0.025f, 0.05f), new Vector3(k * 0.13f, 0, tip + 0.31f), metal);
                        }
                        zMax = tip + 0.36f;
                    }
                    zMin = -back - 0.05f;
                    break;
                }
                case WeaponShape.Bow:
                {
                    // Recurve made of short boxes along an arc in the Y-Z plane (belly towards -Z, the archer).
                    const int seg = 7;
                    float half = L * 0.5f;
                    for (int i = -seg; i < seg; i++)
                    {
                        float a0 = i / (float)seg, a1 = (i + 1) / (float)seg;
                        Vector3 p0 = BowPoint(a0, half), p1 = BowPoint(a1, half);
                        var part = Part(root, "Limb" + i, new Vector3(0.035f, (p1 - p0).magnitude + 0.01f, 0.04f), Vector3.zero, i == -1 || i == 0 ? leather : wood);
                        part.transform.localPosition = (p0 + p1) * 0.5f;
                        part.transform.localRotation = Quaternion.FromToRotation(Vector3.up, p1 - p0);
                    }
                    Vector3 top = BowPoint(1f, half), bottom = BowPoint(-1f, half);
                    var str = Part(root, "String", new Vector3(0.008f, (top - bottom).magnitude, 0.008f), Vector3.zero,
                        PS2Materials.Get("string", PS2Textures.White, new Color(0.9f, 0.88f, 0.8f)));
                    str.transform.localPosition = (top + bottom) * 0.5f;
                    var nock = new GameObject("Nock").transform; // where a drawn arrow sits
                    nock.SetParent(root.transform, false);
                    nock.localPosition = new Vector3(0, 0, -0.21f);
                    zMin = -0.15f; zMax = 0.25f;
                    break;
                }
            }

            var box = root.AddComponent<BoxCollider>();
            if (stats.Shape == WeaponShape.Bow)
            {
                box.center = Vector3.zero;
                box.size = new Vector3(0.12f, L, 0.3f);
            }
            else
            {
                box.center = new Vector3(0, 0.05f, (zMin + zMax) * 0.5f);
                box.size = new Vector3(0.3f, 0.2f, zMax - zMin + 0.1f);
            }
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = stats.Grip == Grip.TwoHand ? 4f : 2f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 1.5f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var w = root.AddComponent<Weapon>();
            w.Init(stats, rb, box);
            LowPolyMesh.BakeAmbient(root, new Color(0.62f, 0.6f, 0.64f), new Color(0.32f, 0.26f, 0.2f));
            return w;
        }

        static Vector3 BowPoint(float a, float half)
        {
            // a in [-1, 1]: along the bow from bottom to top tip. The grip is at the origin, the limbs bend back to -Z.
            float y = a * half;
            float z = -0.16f * a * a - 0.05f * Mathf.Pow(Mathf.Abs(a), 6f); // grip (origin) leads, tips trail
            return new Vector3(0f, y, z);
        }

        /// <summary>A flying arrow (no physics: the Arrow component moves it).</summary>
        public static GameObject ArrowMesh(Transform parent = null)
        {
            var root = new GameObject("Arrow");
            if (parent != null) root.transform.SetParent(parent, false);
            var wood = PS2Materials.Get("arrow_shaft", PS2Textures.Wood, new Color(0.9f, 0.8f, 0.6f));
            var metal = PS2Materials.Get("metal_dark", PS2Textures.Metal, new Color(0.45f, 0.45f, 0.5f));
            var feather = PS2Materials.Get("feather", PS2Textures.Cloth, new Color(0.85f, 0.2f, 0.12f));
            Part(root, "Shaft", new Vector3(0.018f, 0.018f, 0.75f), new Vector3(0, 0, -0.375f), wood);
            Part(root, "Head", new Vector3(0.04f, 0.012f, 0.08f), new Vector3(0, 0, 0.03f), metal);
            Part(root, "FletchA", new Vector3(0.07f, 0.005f, 0.12f), new Vector3(0, 0, -0.68f), feather);
            Part(root, "FletchB", new Vector3(0.005f, 0.07f, 0.12f), new Vector3(0, 0, -0.68f), feather);
            LowPolyMesh.BakeAmbient(root, new Color(0.62f, 0.6f, 0.64f), new Color(0.32f, 0.26f, 0.2f));
            return root;
        }

        static GameObject Part(GameObject root, string name, Vector3 size, Vector3 center, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = LowPolyMesh.Box(size, center);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // PS3 look only
            mr.receiveShadows = false;
            return go;
        }
    }
}
