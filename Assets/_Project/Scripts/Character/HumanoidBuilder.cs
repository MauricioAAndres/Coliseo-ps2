using System.Collections.Generic;
using Gladiator.PS2Style;
using UnityEngine;
using Ring = Gladiator.Character.SkinnedMeshBuilder.Ring;
using SK = Gladiator.Character.SomaSkeleton;

namespace Gladiator.Character
{
    /// <summary>The built character: SOMA joints + one skinned mesh. Lives under the fighter's body root.</summary>
    public class HumanoidRig : MonoBehaviour
    {
        public CharacterAppearance Appearance;
        public ArmorSet Armor;
        public BodyMetrics Metrics;
        public Transform[] Bones;
        public SkinnedMeshRenderer Renderer;
        public int Triangles;

        public float Height => Metrics.H;
        public float Scale => Metrics.S;
        public Transform Bone(int i) => Bones[i];
    }

    /// <summary>
    /// Procedural low-poly PS2 gladiator (~1.5k triangles) skinned to the 30-joint SOMA skeleton.
    /// Everything is driven by <see cref="CharacterAppearance"/>: proportions, muscle/fat volume, face, hair,
    /// beard, clothes, armour and helmet. Rebuilding takes a couple of milliseconds, so the creator simply
    /// rebuilds the whole character whenever a slider moves.
    /// </summary>
    public static class HumanoidBuilder
    {
        public const int Skin = 0, Cloth = 1, Leather = 2, Metal = 3, Hair = 4, Dark = 5, Crest = 6, Cloth2 = 7, Under = 8;

        const int TorsoSeg = 12, LimbSeg = 8, HeadSeg = 12;

        /// <summary>Builds the character. <paramref name="armor"/> = what it wears (null: the old helmet/manica/greaves fields).</summary>
        public static HumanoidRig Build(Transform parent, CharacterAppearance a, Color sky, Color ground, ArmorSet armor = null)
        {
            a.MigrateClothes();
            armor = armor ?? ArmorSet.FromLegacy(a);
            var m = new BodyMetrics(a);
            var go = new GameObject("Humanoid");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<HumanoidRig>();
            rig.Appearance = a.Clone();
            rig.Armor = armor;
            rig.Metrics = m;
            rig.Bones = SK.Create(go.transform, m.Joints);

            var b = new SkinnedMeshBuilder();
            var ctx = new Ctx { A = a, M = m, B = b, Armor = armor };
            ctx.BuildBody();
            ctx.BuildHead();
            ctx.BuildClothes();

            Mesh mesh = b.Build("Gladiator_" + a.Name, sky, ground, out int[] used);
            var bind = new Matrix4x4[SK.Count];
            for (int i = 0; i < SK.Count; i++)
                bind[i] = rig.Bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;

            var skinGo = new GameObject("Mesh");
            skinGo.transform.SetParent(go.transform, false);
            var smr = skinGo.AddComponent<SkinnedMeshRenderer>();
            // Vertices are in the Humanoid object's space; the renderer sits at the same place.
            smr.sharedMesh = mesh;
            smr.bones = rig.Bones;
            smr.rootBone = rig.Bones[SK.Hips];
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // PS3 look only
            smr.receiveShadows = false;
            smr.skinnedMotionVectors = false;
            armorForMaterials = armor;
            var all = Materials(a);
            var mats = new Material[used.Length];
            for (int i = 0; i < used.Length; i++) mats[i] = all[used[i]];
            smr.sharedMaterials = mats;

            rig.Renderer = smr;
            rig.Triangles = b.TriangleCount;
            return rig;
        }

        static ArmorSet armorForMaterials;

        static Color UnderTint(ArmorSet set)
        {
            bool heavy = set != null && ((set[ArmorSlot.Chest] != null && set[ArmorSlot.Chest].Heavy) ||
                                         (set[ArmorSlot.Shoulders] != null && set[ArmorSlot.Shoulders].Heavy));
            return heavy ? new Color(0.13f, 0.12f, 0.12f) : new Color(0.42f, 0.41f, 0.4f);
        }

        static Material[] Materials(CharacterAppearance a)
        {
            string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
            return new[]
            {
                PS2Materials.Get("skin_" + Hex(a.SkinColor), PS2Textures.Skin, a.SkinColor),
                PS2Materials.Get("cloth_" + Hex(a.ClothTint), PS2Textures.Cloth, a.ClothTint),
                PS2Materials.Get("leather", PS2Textures.Leather, Color.white),
                PS2Materials.Get("metal_" + Hex(a.MetalTint), PS2Textures.Metal, a.MetalTint),
                PS2Materials.Get("hair_" + Hex(a.HairTint), PS2Textures.Cloth, a.HairTint),
                PS2Materials.Flat(new Color(0.09f, 0.06f, 0.05f)),
                PS2Materials.Get("cloth_" + Hex(a.CrestTint), PS2Textures.Cloth, a.CrestTint),
                PS2Materials.Get("cloth_" + Hex(a.PantsTint), PS2Textures.Cloth, a.PantsTint),
                // Undershirt worn under chest/shoulder armour: black under iron, grey under leather.
                PS2Materials.Get("cloth_" + Hex(UnderTint(armorForMaterials)), PS2Textures.Cloth, UnderTint(armorForMaterials)),
            };
        }

        // ======================================================================================
        class Ctx
        {
            public CharacterAppearance A;
            public BodyMetrics M;
            public SkinnedMeshBuilder B;
            public ArmorSet Armor;
            readonly Dictionary<int, List<Ring>> hands = new Dictionary<int, List<Ring>>();
            readonly Dictionary<int, List<Ring>> feet = new Dictionary<int, List<Ring>>();
            ArmorPiece Piece(ArmorSlot s) => Armor != null ? Armor[s] : null;
            int Mat(ArmorPiece p) => p.Heavy ? Metal : Leather;
            readonly List<(float y, Ring r)> torso = new List<(float, Ring)>();
            readonly Dictionary<int, List<Ring>> arms = new Dictionary<int, List<Ring>>();
            readonly Dictionary<int, List<Ring>> legs = new Dictionary<int, List<Ring>>();

            float S => M.S;
            float HS => M.HeadScale;

            // ---------------------------------------------------------------- body
            public void BuildBody()
            {
                float fem = M.Fem, mus = A.Muscle, fat = A.Fat;
                float y0 = M.HipsY, y1 = M.Neck1Y;
                float Ty(float f) => Mathf.Lerp(y0, y1, f);
                float hipBack = S * (0.1f + fem * 0.025f + fat * 0.03f);
                float hipFront = S * (0.09f + fat * 0.04f);
                float backDepth = S * (0.09f + mus * 0.012f);

                void T(float y, float halfW, float front, float back, BW w, float z = 0f) =>
                    torso.Add((y, new Ring(new Vector3(0, y, z), Vector3.right, Vector3.forward, halfW, front, back, w)));

                T(M.LegTopY - 0.07f * S, M.HipHalf * 0.55f, S * 0.07f, S * 0.07f, SK.Hips, -0.005f * S);
                T(M.LegTopY, M.HipHalf, hipFront, hipBack, SK.Hips, -0.01f * S);
                T(Ty(0.18f), M.WaistHalf * 1.05f + fat * 0.03f * S, M.BellyFront, S * 0.085f, new BW(SK.Hips, SK.Spine1, 0.5f));
                T(Ty(0.33f), M.WaistHalf, S * 0.09f + fat * 0.08f * S, S * 0.08f, new BW(SK.Spine1, SK.Spine2, 0.5f));
                T(Ty(0.52f), Mathf.Lerp(M.WaistHalf, M.ChestHalf, 0.7f), S * (0.1f + mus * 0.02f + fat * 0.03f) + fem * 0.02f * S, backDepth, new BW(SK.Spine2, SK.Chest, 0.5f));
                // Bust: grows with a feminine build (complexion slider), from nothing at 35% to full at 100%.
                // It is part of the torso rings, so tunics, shirts and armour (built over the same rings) follow it.
                float bust = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, fem));
                if (bust > 0.01f)
                {
                    T(Ty(0.59f), Mathf.Lerp(M.WaistHalf, M.ChestHalf, 0.85f), M.ChestFront * 0.95f + bust * 0.04f * S, backDepth, new BW(SK.Spine2, SK.Chest, 0.7f));
                    T(Ty(0.665f), M.ChestHalf, M.ChestFront + fem * 0.015f * S + bust * 0.075f * S, backDepth * 1.03f, SK.Chest);
                    T(Ty(0.74f), M.ChestHalf, M.ChestFront + bust * 0.045f * S, backDepth * 1.05f, SK.Chest);
                }
                else
                    T(Ty(0.70f), M.ChestHalf, M.ChestFront + fem * 0.015f * S, backDepth * 1.05f, SK.Chest);
                T(Ty(0.84f), M.ChestHalf + mus * 0.01f * S, M.ChestFront * 0.85f, backDepth, SK.Chest);
                T(Ty(0.93f), Mathf.Max(M.ChestHalf * 0.9f, M.ShoulderHalf * 0.86f), S * 0.068f, S * (0.075f + mus * 0.01f), SK.Chest);
                T(Ty(0.99f), Mathf.Lerp(M.NeckR * 1.5f, M.ShoulderHalf, 0.42f + mus * 0.08f), M.NeckR * 1.3f, M.NeckR * 1.45f, SK.Chest);
                T(Ty(1.04f), M.NeckR * 1.2f, M.NeckR * 1.12f, M.NeckR * 1.2f, new BW(SK.Chest, SK.Neck1, 0.5f));
                var tr = new List<Ring>();
                foreach (var t in torso) tr.Add(t.r);
                B.Loft(tr, TorsoSeg, Skin, true, false);

                // Neck up into the head.
                var neck = new List<Ring>
                {
                    new Ring(new Vector3(0, y1 - 0.02f * S, 0), Vector3.right, Vector3.forward, M.NeckR * 1.1f, M.NeckR, M.NeckR * 1.1f, SK.Neck1),
                    new Ring(M.Joints[SK.Neck2], Vector3.right, Vector3.forward, M.NeckR, M.NeckR * 0.95f, M.NeckR, new BW(SK.Neck1, SK.Neck2, 0.5f)),
                    new Ring(M.Joints[SK.Head] + new Vector3(0, 0.01f, 0), Vector3.right, Vector3.forward, M.NeckR * 0.95f, M.NeckR * 0.9f, M.NeckR, new BW(SK.Neck2, SK.Head, 0.7f)),
                };
                B.Loft(neck, LimbSeg, Skin, false, false);

                for (int side = -1; side <= 1; side += 2) { BuildArm(side); BuildLeg(side); }
            }

            void BuildArm(int side)
            {
                bool left = side < 0;
                int sh = left ? SK.LeftShoulder : SK.RightShoulder, arm = sh + 1, fore = sh + 2, hand = sh + 3;
                Vector3 dir = new Vector3(side, 0, 0);
                Vector3 j0 = M.Joints[arm], j1 = M.Joints[fore], j2 = M.Joints[hand];
                float mus = A.Muscle;
                Ring R(Vector3 c, float ru, float rvF, float rvB, BW w) => new Ring(c, Vector3.up, Vector3.forward, ru, rvF, rvB, w);
                var rings = new List<Ring>
                {
                    R(j0 - dir * 0.05f * S + Vector3.down * 0.01f * S, M.DeltR, M.DeltR * 0.95f, M.DeltR, new BW(sh, arm, 0.6f)),
                    R(j0 + dir * 0.06f * S, M.DeltR, M.DeltR, M.DeltR * 0.95f, arm),
                    R(Vector3.Lerp(j0, j1, 0.5f), M.BicepR * 0.92f, M.BicepR * (1.05f + mus * 0.1f), M.BicepR, arm),
                    R(j1, M.ElbowR, M.ElbowR, M.ElbowR * 1.05f, new BW(arm, fore, 0.5f)),
                    R(Vector3.Lerp(j1, j2, 0.3f), M.ForeR, M.ForeR * 1.05f, M.ForeR, fore),
                    R(j2 - dir * 0.01f * S, M.WristR * 0.8f, M.WristR, M.WristR, new BW(fore, hand, 0.4f)),
                };
                arms[side] = rings;
                B.Loft(rings, LimbSeg, Skin, false, false);

                // Hand: flat paddle (palm down) + thumb.
                var h = new List<Ring>
                {
                    R(j2 - dir * 0.01f * S, 0.022f * S, 0.04f * S, 0.035f * S, hand),
                    R(j2 + dir * 0.09f * S, 0.018f * S, 0.046f * S, 0.04f * S, hand),
                    R(j2 + dir * 0.17f * S, 0.012f * S, 0.04f * S, 0.035f * S, hand),
                };
                hands[side] = h;
                B.Loft(h, 6, Skin, false, true);
                Vector3 thumbEnd = M.Joints[sh + 4];
                Vector3 thumbBase = j2 + dir * 0.02f * S + Vector3.forward * 0.03f * S;
                B.Box((thumbBase + thumbEnd) * 0.5f, new Vector3(0.022f * S, 0.02f * S, Vector3.Distance(thumbBase, thumbEnd) + 0.01f * S),
                    Quaternion.LookRotation(thumbEnd - thumbBase), Skin, hand);
            }

            void BuildLeg(int side)
            {
                bool left = side < 0;
                int leg = left ? SK.LeftLeg : SK.RightLeg, shin = leg + 1, foot = leg + 2, toe = leg + 3;
                Vector3 hip = M.Joints[leg], knee = M.Joints[shin], ankle = M.Joints[foot];
                Ring R(Vector3 c, float r, float front, float back, BW w) => new Ring(c, Vector3.right, Vector3.forward, r, r * front, r * back, w);
                var rings = new List<Ring>
                {
                    R(hip + Vector3.up * 0.03f * S, M.ThighR * 0.95f, 1f, 1f, new BW(SK.Hips, leg, 0.7f)),
                    R(new Vector3(hip.x, M.LegTopY - 0.08f * S, 0), M.ThighR, 1.05f, 1f, leg),
                    R(Vector3.Lerp(hip, knee, 0.55f), M.ThighR * 0.8f, 1.05f, 0.95f, leg),
                    R(knee, M.KneeR, 1f, 1f, new BW(leg, shin, 0.5f)),
                    R(Vector3.Lerp(knee, ankle, 0.3f), M.CalfR, 0.9f, 1.15f, shin),
                    R(Vector3.Lerp(knee, ankle, 0.75f), M.AnkleR * 1.15f, 1f, 1f, shin),
                    R(ankle + Vector3.up * 0.02f * S, M.AnkleR, 1f, 1f, new BW(shin, foot, 0.5f)),
                };
                legs[side] = rings;
                B.Loft(rings, LimbSeg, Skin, false, true);

                // Sandal / caliga: loft heel -> toe, flat sole on the ground.
                float x = ankle.x;
                Ring F(float z, float cy, float halfW, float up, BW w) => new Ring(new Vector3(x, cy, z * S), Vector3.right, Vector3.up, halfW * S, up * S, cy, w);
                var f = new List<Ring>
                {
                    F(-0.06f, 0.045f * S, 0.038f, 0.036f, foot),
                    F(0.01f, 0.05f * S, 0.046f, 0.045f, foot),
                    F(0.11f, 0.03f * S, 0.052f, 0.026f, new BW(foot, toe, 0.5f)),
                    F(0.18f, 0.022f * S, 0.04f, 0.017f, toe),
                };
                feet[side] = f;
                // Bare foot (skin); sandals, caligae or boots are added over it in BuildClothes.
                B.Loft(f, LimbSeg, Skin, true, true);
            }

            // ---------------------------------------------------------------- head
            static readonly float[] HT = { 0f, 0.15f, 0.3f, 0.5f, 0.7f, 0.85f, 0.95f, 1f };
            static readonly float[] HW = { 0.5f, 0.74f, 0.9f, 0.98f, 1f, 0.88f, 0.6f, 0f };
            static readonly float[] HDF = { 0.62f, 0.8f, 0.94f, 1f, 1f, 0.85f, 0.55f, 0f };
            static readonly float[] HDB = { 0.4f, 0.55f, 0.75f, 0.97f, 1.05f, 0.9f, 0.55f, 0f };

            static float Interp(float[] arr, float t)
            {
                if (t <= 0f) return arr[0];
                for (int i = 1; i < HT.Length; i++)
                    if (t <= HT[i]) return Mathf.Lerp(arr[i - 1], arr[i], (t - HT[i - 1]) / (HT[i] - HT[i - 1]));
                return arr[arr.Length - 1];
            }

            Vector3 HeadCenter => new Vector3(0, M.ChinY + M.HeadH * 0.45f, 0.01f * HS);

            /// <summary>Point on the head surface: t = 0 chin .. 1 crown (t &lt; 0 hangs below the chin), a = angle (90 deg = face).</summary>
            Vector3 HeadPoint(float t, float aDeg, float inflate = 1f)
            {
                float tc = Mathf.Clamp01(t);
                float a = aDeg * Mathf.Deg2Rad;
                float jaw = tc < 0.35f ? (A.Jaw - 0.5f) * 0.32f * (1f - tc / 0.35f) : 0f;
                float wf = Interp(HW, tc) + jaw;
                float s = Mathf.Sin(a), c = Mathf.Cos(a);
                float df = s >= 0f ? Interp(HDF, tc) + jaw * 0.5f : Interp(HDB, tc);
                float shrink = t < 0f ? Mathf.Clamp01(1f + t * 1.2f) : 1f;
                float zc = 0.015f * HS + (0.35f - Mathf.Min(tc, 0.35f)) * 0.05f * HS;
                float cy = M.ChinY + M.HeadH * 0.45f;
                float y = cy + (t * M.HeadH - M.HeadH * 0.45f) * inflate;
                return new Vector3(c * M.HeadHalfW * wf * shrink * inflate, y, zc + s * M.HeadHalfD * df * shrink * inflate);
            }

            /// <summary>Patch over the skull from tLow(angle) up to the crown (hair caps, helmet).</summary>
            void Cap(float frontT, float backT, float inflate, int sub, float jitter = 0f)
            {
                const int cols = HeadSeg, rows = 5;
                Vector3 ctr = HeadCenter;
                B.Grid(cols, rows, (c, r) =>
                {
                    float a = c / (float)cols * 360f;
                    float f = (Mathf.Sin(a * Mathf.Deg2Rad) + 1f) * 0.5f;
                    float tLow = Mathf.Lerp(backT, frontT, f * f);
                    float t = Mathf.Lerp(tLow, 1f, r / (float)rows);
                    Vector3 p = HeadPoint(t, a, inflate);
                    if (jitter > 0f && r > 0 && r < rows && c % cols != 0)
                        p += (p - ctr).normalized * jitter * (Mathf.PerlinNoise(c * 1.7f, r * 2.3f) - 0.3f);
                    return p;
                }, (c, r) => SK.Head, ctr, sub);
            }

            void Chain(float frontT, float backT, float inflate, Vector3 boxSize, int count, int sub)
            {
                Vector3 ctr = HeadCenter;
                Vector3 P(float th) => th < 0.5f ? HeadPoint(Mathf.Lerp(frontT, 1f, th * 2f), 90f, inflate)
                                                 : HeadPoint(Mathf.Lerp(1f, backT, (th - 0.5f) * 2f), 270f, inflate);
                for (int i = 0; i < count; i++)
                {
                    float th = (i + 0.5f) / count;
                    Vector3 p = P(th), tan = P(th + 0.02f) - P(th - 0.02f);
                    Vector3 up = (p - ctr).normalized;
                    B.Box(p + up * boxSize.y * 0.4f, boxSize, Quaternion.LookRotation(tan, up), sub, SK.Head);
                }
            }

            public void BuildHead()
            {
                float[] rowsT = { 0f, 0.08f, 0.15f, 0.22f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.78f, 0.85f, 0.92f, 0.97f, 1f };
                Vector3 ctr = HeadCenter;
                B.Grid(HeadSeg, rowsT.Length - 1, (c, r) => HeadPoint(rowsT[r], c / (float)HeadSeg * 360f), (c, r) => SK.Head, ctr, Skin);
                var chin = new List<Vector3>();
                for (int i = 0; i < HeadSeg; i++) chin.Add(HeadPoint(0f, i / (float)HeadSeg * 360f));
                B.Fan(HeadPoint(0f, 90f) * 0.5f + HeadPoint(0f, 270f) * 0.5f, chin, Vector3.down, Skin, SK.Head);

                // Face
                float n = A.Nose;
                float spread = 6f + n * 5f;
                Vector3 apex = HeadPoint(0.38f, 90f) + Vector3.forward * (0.012f + n * 0.022f) * HS + Vector3.down * 0.004f * HS;
                B.Pyramid(HeadPoint(0.34f, 90f - spread), HeadPoint(0.34f, 90f + spread), HeadPoint(0.54f, 90f + spread * 0.6f), HeadPoint(0.54f, 90f - spread * 0.6f), apex, Skin, SK.Head);
                for (int side = -1; side <= 1; side += 2)
                {
                    float ang = 90f - side * 27f;
                    Vector3 e = HeadPoint(0.55f, ang, 0.985f);
                    Quaternion fr = Quaternion.LookRotation(new Vector3(e.x, 0, e.z - ctr.z).normalized);
                    B.Box(e, new Vector3(0.024f, 0.011f, 0.012f) * HS, fr, Dark, SK.Head);
                    Vector3 brow = HeadPoint(0.64f, 90f - side * 25f, 1.02f);
                    B.Box(brow, new Vector3(0.034f, 0.008f, 0.014f) * HS, fr * Quaternion.Euler(0, 0, side * 6f), Hair, SK.Head);
                    Vector3 ear = HeadPoint(0.47f, side > 0 ? 0f : 180f, 0.98f);
                    B.Box(ear, new Vector3(0.014f, 0.05f, 0.032f) * HS, Quaternion.identity, Skin, SK.Head);
                }
                if (A.Beard < 2) B.Box(HeadPoint(0.2f, 90f, 0.99f), new Vector3(0.036f, 0.007f, 0.01f) * HS, Quaternion.identity, Dark, SK.Head);

                // Hair (hidden under a helmet)
                var helmet = Piece(ArmorSlot.Helmet);
                if (helmet == null)
                {
                    switch (A.HairStyle)
                    {
                        case 1: Cap(0.72f, 0.3f, 1.07f, Hair); break;
                        case 2: Cap(0.68f, 0.24f, 1.13f, Hair, 0.014f * HS); break;
                        case 3:
                        {
                            Cap(0.74f, 0.3f, 1.07f, Hair);
                            Vector3 p0 = HeadPoint(0.6f, 270f, 1.07f);
                            Ring TR(Vector3 c, float r) => new Ring(c, Vector3.right, Vector3.forward, r * HS, r * HS, r * HS, SK.Head);
                            B.Loft(new List<Ring>
                            {
                                TR(p0 + new Vector3(0, 0.01f, 0.02f) * HS, 0.022f),
                                TR(p0 + new Vector3(0, -0.06f, -0.035f) * HS, 0.03f),
                                TR(p0 + new Vector3(0, -0.18f, -0.05f) * HS, 0.024f),
                                TR(p0 + new Vector3(0, -0.28f, -0.04f) * HS, 0.01f),
                            }, 6, Hair, true, true);
                            break;
                        }
                        case 4: Chain(0.74f, 0.32f, 1.0f, new Vector3(0.022f, 0.06f, 0.05f) * HS, 7, Hair); break;
                        case 5:
                        {
                            Cap(0.72f, 0.2f, 1.08f, Hair);
                            for (int layer = 0; layer < 2; layer++)
                            {
                                float inf = layer == 0 ? 1.1f : 1.07f;
                                Vector3 inside = layer == 0 ? ctr : ctr + Vector3.back * 2f;
                                B.Grid(8, 3, (c, r) =>
                                {
                                    float a = Mathf.Lerp(200f, 340f, c / 8f);
                                    float t = Mathf.Lerp(0.6f, -0.55f, r / 3f);
                                    Vector3 p = HeadPoint(Mathf.Max(t, 0.05f), a, inf);
                                    if (t < 0.05f) p += Vector3.down * (0.05f - t) * M.HeadH + Vector3.back * (0.05f - t) * 0.04f;
                                    return p;
                                }, (c, r) => SK.Head, inside, Hair);
                            }
                            break;
                        }
                    }
                }

                // Beard (hidden inside the closed helm)
                switch (helmet != null && helmet.Model == 2 ? 0 : A.Beard)
                {
                    case 1:
                        B.Grid(4, 3, (c, r) => HeadPoint(Mathf.Lerp(-0.1f, 0.2f, r / 3f), Mathf.Lerp(60f, 120f, c / 4f), 1.08f), (c, r) => SK.Head, ctr, Hair);
                        break;
                    case 2:
                    case 3:
                    {
                        float low = A.Beard == 2 ? -0.04f : -0.45f;
                        int rows = A.Beard == 2 ? 3 : 5;
                        B.Grid(10, rows, (c, r) =>
                        {
                            float a = Mathf.Lerp(8f, 172f, c / 10f);
                            float tHigh = Mathf.Lerp(0.5f, 0.27f, Mathf.Sin(a * Mathf.Deg2Rad));
                            return HeadPoint(Mathf.Lerp(low, tHigh, r / (float)rows), a, 1.07f);
                        }, (c, r) => SK.Head, ctr, Hair);
                        break;
                    }
                }

                if (helmet != null)
                {
                    if (helmet.Model == 0) BuildLeatherCap(ctr);
                    else if (helmet.Model == 1) BuildHelmet(ctr);
                    else BuildClosedHelm(ctr);
                }
            }

            /// <summary>Light helmet 1: hardened leather cap with a chin strap.</summary>
            void BuildLeatherCap(Vector3 ctr)
            {
                Cap(0.62f, 0.3f, 1.12f, Leather);
                B.Grid(HeadSeg, 1, (c, r) =>
                {
                    float a = c / (float)HeadSeg * 360f;
                    float f = (Mathf.Sin(a * Mathf.Deg2Rad) + 1f) * 0.5f;
                    return HeadPoint(Mathf.Lerp(0.3f, 0.62f, f * f) + r * 0.06f, a, 1.15f);
                }, (c, r) => SK.Head, ctr, Leather);
                for (int side = -1; side <= 1; side += 2)
                    B.Box(HeadPoint(0.2f, 90f - side * 70f, 1.06f), new Vector3(0.01f, 0.12f, 0.02f) * HS, Quaternion.Euler(0, 0, side * 10f), Leather, SK.Head);
            }

            /// <summary>Heavy helmet: closed murmillo helm (face grille, wide brim, tall crest, neck guard).</summary>
            void BuildClosedHelm(Vector3 ctr)
            {
                const float inf = 1.22f;
                Cap(0.04f, 0.12f, inf, Metal);
                B.Grid(HeadSeg, 1, (c, r) =>
                {
                    float a = c / (float)HeadSeg * 360f;
                    float f = (Mathf.Sin(a * Mathf.Deg2Rad) + 1f) * 0.5f;
                    float t = Mathf.Lerp(0.12f, 0.04f, f * f);
                    return HeadPoint(t, a, r == 0 ? inf : inf + 0.4f) + Vector3.down * (r * 0.03f * HS);
                }, (c, r) => SK.Head, ctr + Vector3.down * 1f, Metal);
                // Grille: dark visor with metal bars.
                for (int k = -2; k <= 2; k++)
                {
                    Vector3 p = HeadPoint(0.5f, 90f + k * 11f, inf + 0.03f);
                    B.Box(p, new Vector3(0.008f, 0.16f, 0.012f) * HS, Quaternion.LookRotation(new Vector3(p.x, 0, p.z - ctr.z).normalized), Metal, SK.Head);
                }
                for (int k = 0; k < 2; k++)
                {
                    Vector3 p = HeadPoint(0.42f + k * 0.16f, 90f, inf + 0.035f);
                    B.Box(p, new Vector3(0.13f, 0.008f, 0.012f) * HS, Quaternion.identity, Metal, SK.Head);
                }
                B.Box(HeadPoint(0.5f, 90f, inf - 0.02f), new Vector3(0.12f, 0.15f, 0.01f) * HS, Quaternion.identity, Dark, SK.Head);
                Chain(0.55f, 0.15f, inf, new Vector3(0.04f, 0.16f, 0.07f) * HS, 9, Crest);
            }

            void BuildHelmet(Vector3 ctr)
            {
                const float inf = 1.17f;
                Cap(0.64f, 0.3f, inf, Metal);
                // Brim following the helmet's lower edge.
                B.Grid(HeadSeg, 1, (c, r) =>
                {
                    float a = c / (float)HeadSeg * 360f;
                    float f = (Mathf.Sin(a * Mathf.Deg2Rad) + 1f) * 0.5f;
                    float t = Mathf.Lerp(0.3f, 0.64f, f * f);
                    return HeadPoint(t, a, r == 0 ? inf : inf + 0.25f) + Vector3.down * (r * 0.015f * HS);
                }, (c, r) => SK.Head, ctr + Vector3.down * 1f, Metal);
                Chain(0.66f, 0.3f, inf, new Vector3(0.03f, 0.11f, 0.06f) * HS, 7, Crest);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 g = HeadPoint(0.3f, 90f - side * 72f, 1.12f);
                    B.Box(g, new Vector3(0.012f, 0.08f, 0.06f) * HS, Quaternion.Euler(0, -side * 18f, 0), Metal, SK.Head);
                }
            }

            // ---------------------------------------------------------------- clothes and armour
            Ring TorsoAt(float y)
            {
                if (y <= torso[0].y) return torso[0].r;
                for (int i = 1; i < torso.Count; i++)
                {
                    if (y > torso[i].y) continue;
                    var a = torso[i - 1]; var b = torso[i];
                    float k = (y - a.y) / (b.y - a.y);
                    var r = a.r;
                    r.C = Vector3.Lerp(a.r.C, b.r.C, k);
                    r.RU = Mathf.Lerp(a.r.RU, b.r.RU, k);
                    r.RVPos = Mathf.Lerp(a.r.RVPos, b.r.RVPos, k);
                    r.RVNeg = Mathf.Lerp(a.r.RVNeg, b.r.RVNeg, k);
                    r.W = k < 0.5f ? a.r.W : b.r.W;
                    return r;
                }
                return torso[torso.Count - 1].r;
            }

            List<Ring> TorsoBand(float yFrom, float yTo, int steps, float mul, float add)
            {
                var list = new List<Ring>();
                for (int i = 0; i <= steps; i++) list.Add(TorsoAt(Mathf.Lerp(yFrom, yTo, i / (float)steps)).Inflated(mul, add));
                return list;
            }

            /// <summary>
            /// A skirt (rings from the waist down) whose lower part follows the legs: each vertex on the left half leans
            /// on the left thigh, on the right half on the right thigh, more the lower and the more to the side it is.
            /// So a leg swinging forward carries its side of the skirt with it instead of poking through it.
            /// </summary>
            void SkirtLoft(List<Ring> rings, int mat)
            {
                int cols = TorsoSeg, rows = rings.Count - 1;
                float leftX = M.Joints[SK.LeftLeg].x;
                Vector3 inside = (rings[0].C + rings[rings.Count - 1].C) * 0.5f;
                Vector3 P(int c, int r) => rings[r].Point(c / (float)cols * Mathf.PI * 2f);
                B.Grid(cols, rows, P, (c, r) =>
                {
                    Vector3 v = P(c, r);
                    // waist: hips only; lower down more and more on the thigh, the hem entirely, so the cloth goes
                    // where the knee goes (front and back too: the middle follows the nearer leg)
                    float rowK = r == 0 ? 0f : Mathf.Lerp(0.4f, 1f, (r - 1) / Mathf.Max(1f, rows - 1f));
                    float side = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.5f, Mathf.Abs(v.x - rings[r].C.x) / Mathf.Max(0.001f, rings[r].RU)));
                    float w = rowK * Mathf.Lerp(0.85f, 1f, side);
                    int leg = Mathf.Sign(v.x - rings[r].C.x) == Mathf.Sign(leftX) ? SK.LeftLeg : SK.RightLeg;
                    return w <= 0.01f ? new BW(SK.Hips) : new BW(SK.Hips, leg, w);
                }, inside, mat);
            }

            public void BuildClothes()
            {
                float fem = M.Fem;
                float y0 = M.HipsY, y1 = M.Neck1Y;
                float Ty(float f) => Mathf.Lerp(y0, y1, f);
                float crotch = M.LegTopY - 0.07f * S;
                float add = 0.006f * S;
                Ring hip = TorsoAt(M.LegTopY);
                var chest = Piece(ArmorSlot.Chest);

                // ---- pants (second cloth colour)
                switch (A.Pants)
                {
                    case 1:
                    case 2:
                    {
                        // Bracae: shorts to above the knee, or trousers to the ankle.
                        B.Loft(TorsoBand(crotch, M.LegTopY + 0.06f * S, 2, 1.05f, add), TorsoSeg, Cloth2, true, false);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var lg = legs[side];
                            var rings = new List<Ring> { lg[0].Inflated(1.06f, add), lg[1].Inflated(1.08f, add), lg[2].Inflated(1.1f, add) };
                            if (A.Pants == 1)
                            {
                                Ring knee = lg[3].Inflated(1.1f, add);
                                knee.C = Vector3.Lerp(lg[2].C, lg[3].C, 0.6f);
                                rings.Add(knee);
                            }
                            else
                                for (int i = 3; i <= 6; i++) rings.Add(lg[i].Inflated(i == 6 ? 1.15f : 1.08f, add));
                            B.Loft(rings, LimbSeg, Cloth2, false, false);
                        }
                        break;
                    }
                    case 3:
                    case 4:
                    {
                        // Skirts: a short pleated kilt (above the knee) or a long one (to mid-calf), over a hip wrap.
                        B.Loft(TorsoBand(crotch, M.LegTopY + 0.06f * S, 2, 1.05f, add), TorsoSeg, Cloth2, true, false);
                        float hem = A.Pants == 3 ? M.KneeY + 0.1f * S : Mathf.Lerp(M.KneeY, 0f, 0.45f);
                        Ring k0 = hip.Inflated(1.08f, add); k0.C.y = M.LegTopY + 0.03f * S;
                        Ring k1 = hip.Inflated(1.24f, 0.012f * S); k1.C.y = M.LegTopY - 0.12f * S;
                        Ring k2 = hip.Inflated(A.Pants == 3 ? 1.5f : 1.42f, 0.04f * S); k2.C.y = hem;
                        Ring km = k1; km.C = Vector3.Lerp(k1.C, k2.C, 0.5f); km.RU = Mathf.Lerp(k1.RU, k2.RU, 0.5f);
                        km.RVPos = Mathf.Lerp(k1.RVPos, k2.RVPos, 0.5f); km.RVNeg = Mathf.Lerp(k1.RVNeg, k2.RVNeg, 0.5f);
                        k0.W = k1.W = km.W = k2.W = SK.Hips;
                        SkirtLoft(new List<Ring> { k0, k1, km, k2 }, Cloth2);
                        // Pleat lines.
                        for (int k = 0; k < 8; k++)
                        {
                            float ang = (k + 0.5f) / 8f * Mathf.PI * 2f;
                            Vector3 d = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                            Vector3 OnRing(Ring r, float y) => new Vector3(r.C.x + d.x * (r.RU + 0.004f * S), y,
                                r.C.z + d.z * ((d.z >= 0f ? r.RVPos : r.RVNeg) + 0.004f * S));
                            Vector3 top = OnRing(k1, k1.C.y), bot = OnRing(k2, hem);
                            B.Box((top + bot) * 0.5f, new Vector3(0.008f * S, 0.006f * S, Vector3.Distance(top, bot)), Quaternion.LookRotation(bot - top), Dark, SK.Hips);
                        }
                        break;
                    }
                    default:
                    {
                        // Subligaculum: wrap around the hips + front and back flaps.
                        B.Loft(TorsoBand(crotch, M.LegTopY + 0.05f * S, 2, 1.05f, add), TorsoSeg, Cloth2, true, false);
                        float zf = hip.C.z + hip.RVPos + 0.012f * S;
                        float zb = hip.C.z - hip.RVNeg - 0.012f * S;
                        B.Box(new Vector3(0, M.LegTopY - 0.1f * S, zf), new Vector3(0.15f, 0.22f, 0.012f) * S, Quaternion.Euler(-4f, 0, 0), Cloth2, SK.Hips);
                        B.Box(new Vector3(0, M.LegTopY - 0.08f * S, zb), new Vector3(0.17f, 0.18f, 0.012f) * S, Quaternion.Euler(6f, 0, 0), Cloth2, SK.Hips);
                        break;
                    }
                }

                // ---- shirt (tunic)
                void Skirt(float hem, float flare)
                {
                    Ring sk0 = hip.Inflated(1.07f, add); sk0.C.y = M.LegTopY - 0.02f * S;
                    Ring sk1 = hip.Inflated(1.2f * flare, 0.015f * S); sk1.C.y = M.LegTopY - 0.15f * S;
                    Ring sk2 = hip.Inflated(1.46f * flare, 0.04f * S); sk2.C.y = hem;
                    Ring skm = sk1; skm.C = Vector3.Lerp(sk1.C, sk2.C, 0.5f); skm.RU = Mathf.Lerp(sk1.RU, sk2.RU, 0.5f);
                    skm.RVPos = Mathf.Lerp(sk1.RVPos, sk2.RVPos, 0.5f); skm.RVNeg = Mathf.Lerp(sk1.RVNeg, sk2.RVNeg, 0.5f);
                    sk0.W = sk1.W = skm.W = sk2.W = SK.Hips;
                    SkirtLoft(new List<Ring> { sk0, sk1, skm, sk2 }, Cloth);
                }
                switch (A.Shirt)
                {
                    case 1: // short tunic: short sleeves, skirt to mid-thigh
                        B.Loft(TorsoBand(M.LegTopY - 0.02f * S, Ty(1.03f), 8, 1.05f, add), TorsoSeg, Cloth, false, false);
                        Skirt(M.KneeY + 0.16f * S, 1f);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var ar = arms[side];
                            B.Loft(new List<Ring> { ar[0].Inflated(1.05f, add), ar[1].Inflated(1.18f, add), ar[2].Inflated(1.2f, add) }, LimbSeg, Cloth, false, false);
                        }
                        break;
                    case 2: // exomis: over the left shoulder only, right shoulder and arm bare
                    {
                        B.Loft(TorsoBand(M.LegTopY - 0.02f * S, Ty(0.8f), 6, 1.05f, add), TorsoSeg, Cloth, false, false);
                        Skirt(M.KneeY + 0.2f * S, 0.95f);
                        Vector3 sh = M.Joints[SK.LeftArm];
                        Ring top = TorsoAt(Ty(0.8f));
                        Vector3 front = new Vector3(-top.RU * 0.35f, Ty(0.8f), top.C.z + top.RVPos + 0.01f * S);
                        Vector3 back = new Vector3(-top.RU * 0.35f, Ty(0.8f), top.C.z - top.RVNeg - 0.01f * S);
                        Vector3 over = sh + new Vector3(0.05f * S, 0.04f * S, 0f);
                        void Strap(Vector3 p0, Vector3 p1)
                        {
                            B.Box((p0 + p1) * 0.5f, new Vector3(0.09f * S, 0.012f * S, Vector3.Distance(p0, p1) + 0.02f * S),
                                Quaternion.LookRotation(p1 - p0, Vector3.right), Cloth, SK.Chest);
                        }
                        Strap(front, over);
                        Strap(over, back);
                        break;
                    }
                    case 3: // long tunic: long sleeves to the wrist, skirt to the knee
                        B.Loft(TorsoBand(M.LegTopY - 0.02f * S, Ty(1.03f), 8, 1.05f, add), TorsoSeg, Cloth, false, false);
                        Skirt(M.KneeY - 0.02f * S, 1.05f);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var ar = arms[side];
                            var sl = new List<Ring>();
                            for (int i = 0; i < ar.Count; i++) sl.Add(ar[i].Inflated(i == ar.Count - 1 ? 1.25f : 1.1f, add));
                            B.Loft(sl, LimbSeg, Cloth, false, false);
                        }
                        break;
                    case 4: // tank top: torso to the waist, thin straps, no sleeves
                    {
                        B.Loft(TorsoBand(M.HipsY - 0.02f * S, Ty(0.86f), 6, 1.05f, add), TorsoSeg, Cloth, false, false);
                        Ring top = TorsoAt(Ty(0.86f));
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 sh = M.Joints[side < 0 ? SK.LeftArm : SK.RightArm];
                            float x = side * Mathf.Min(top.RU * 0.55f, Mathf.Abs(sh.x) * 0.55f);
                            Vector3 front = new Vector3(x, Ty(0.86f), top.C.z + top.RVPos + 0.008f * S);
                            Vector3 back = new Vector3(x, Ty(0.86f), top.C.z - top.RVNeg - 0.008f * S);
                            Vector3 over = new Vector3(x, Ty(1.0f) + 0.02f * S, top.C.z);
                            foreach (var (p0, p1) in new[] { (front, over), (over, back) })
                                B.Box((p0 + p1) * 0.5f, new Vector3(0.035f * S, 0.01f * S, Vector3.Distance(p0, p1) + 0.015f * S),
                                    Quaternion.LookRotation(p1 - p0, Vector3.right * side), Cloth, SK.Chest);
                        }
                        break;
                    }
                    default: // bare chest (a band for female gladiators)
                        if (fem > 0.5f && chest == null) B.Loft(TorsoBand(Ty(0.6f), Ty(0.78f), 2, 1.05f, add), TorsoSeg, Cloth, false, false);
                        break;
                }

                // Belt
                float by = M.HipsY + 0.02f * S;
                B.Loft(TorsoBand(by - 0.03f * S, by + 0.03f * S, 1, A.Shirt == 0 ? 1.08f : 1.12f, add * 2f), TorsoSeg, Leather, false, false);

                // ---- shoes (under boots, if any)
                var boots = Piece(ArmorSlot.Boots);
                for (int side = -1; side <= 1; side += 2)
                {
                    var f = feet[side];
                    var lg = legs[side];
                    if (boots != null)
                    {
                        // Boots: the whole foot plus the lower shin.
                        var bf = new List<Ring>();
                        foreach (var r in f) bf.Add(r.Inflated(1.18f, add));
                        B.Loft(bf, LimbSeg, Mat(boots), true, true);
                        B.Loft(new List<Ring> { lg[6].Inflated(1.3f, add * 2f), lg[5].Inflated(1.25f, add * 2f),
                                                Mid(lg[4], lg[5], 0.5f).Inflated(1.2f, add * 2f) }, LimbSeg, Mat(boots), false, false);
                        if (boots.Heavy)
                            B.Box(f[3].C + new Vector3(0, 0.02f * S, -0.01f * S), new Vector3(0.09f, 0.04f, 0.08f) * S, Quaternion.identity, Metal, side < 0 ? SK.LeftToeBase : SK.RightToeBase);
                        continue;
                    }
                    // Sole + straps (sandals); caligae lace up the shin.
                    var sole = new List<Ring>();
                    foreach (var r in f) { var q = r.Inflated(1.08f, add); sole.Add(q); }
                    B.Loft(new List<Ring> { Flat(sole[0]), Flat(sole[1]), Flat(sole[2]), Flat(sole[3]) }, LimbSeg, Leather, true, true);
                    B.Loft(new List<Ring> { Band(f[1], 0.012f), Band(f[1], -0.012f) }, LimbSeg, Leather, false, false);
                    B.Loft(new List<Ring> { lg[6].Inflated(1.12f, add), Mid(lg[6], lg[5], 0.3f).Inflated(1.12f, add) }, LimbSeg, Leather, false, false);
                    if (A.Shoes == 1)
                        for (int k = 1; k <= 3; k++)
                        {
                            Ring r0 = Mid(lg[6], lg[4], k / 3.6f).Inflated(1.12f, add);
                            Ring r1 = Mid(lg[6], lg[4], k / 3.6f + 0.05f).Inflated(1.12f, add);
                            B.Loft(new List<Ring> { r0, r1 }, LimbSeg, Leather, false, false);
                        }
                }

                BuildUndershirt(add);
                BuildArmor(add);
            }

            /// <summary>
            /// Under a chest piece or shoulder guards the bare skin between plates looks wrong: a grey/black undershirt
            /// (body and sleeves to the elbow) goes under them unless a tunic already covers the body.
            /// </summary>
            void BuildUndershirt(float add)
            {
                if (Piece(ArmorSlot.Chest) == null && Piece(ArmorSlot.Shoulders) == null) return;
                if (A.Shirt == 1 || A.Shirt == 3) return; // tunics already cover it
                float y0 = M.HipsY, y1 = M.Neck1Y;
                B.Loft(TorsoBand(M.HipsY - 0.03f * S, Mathf.Lerp(y0, y1, 1.02f), 7, 1.035f, add * 0.6f), TorsoSeg, Under, false, false);
                for (int side = -1; side <= 1; side += 2)
                {
                    var ar = arms[side];
                    B.Loft(new List<Ring> { ar[0].Inflated(1.04f, add * 0.6f), ar[1].Inflated(1.08f, add * 0.6f), ar[2].Inflated(1.1f, add * 0.6f),
                                            ar[3].Inflated(1.14f, add * 0.6f) }, LimbSeg, Under, false, false);
                }
            }

            static Ring Mid(Ring a, Ring b, float k)
            {
                var r = a;
                r.C = Vector3.Lerp(a.C, b.C, k);
                r.RU = Mathf.Lerp(a.RU, b.RU, k);
                r.RVPos = Mathf.Lerp(a.RVPos, b.RVPos, k);
                r.RVNeg = Mathf.Lerp(a.RVNeg, b.RVNeg, k);
                r.W = k < 0.5f ? a.W : b.W;
                return r;
            }

            /// <summary>Foot ring squashed into a thin sole at the bottom.</summary>
            Ring Flat(Ring r)
            {
                var q = r;
                q.RVPos = 0.012f * S; // the foot rings use V = up: keep only a thin slab above the ground
                return q;
            }

            Ring Band(Ring r, float dz)
            {
                var q = r.Inflated(1.12f, 0.004f * S);
                q.C += new Vector3(0, 0, dz * S);
                return q;
            }

            /// <summary>Armour pieces (light = leather, heavy = metal) over the clothes.</summary>
            void BuildArmor(float add)
            {
                float y0 = M.HipsY, y1 = M.Neck1Y;
                float Ty(float f) => Mathf.Lerp(y0, y1, f);

                var chest = Piece(ArmorSlot.Chest);
                if (chest != null)
                {
                    if (!chest.Heavy)
                    {
                        B.Loft(TorsoBand(Ty(0.12f), Ty(0.93f), 6, 1.13f, add * 2f), TorsoSeg, Leather, false, false);
                        B.Loft(TorsoBand(Ty(0.1f), Ty(0.16f), 1, 1.17f, add * 2f), TorsoSeg, Metal, false, false); // bronze trim
                    }
                    else
                    {
                        for (int k = 0; k < 6; k++)
                        {
                            float ya = Mathf.Lerp(Ty(0.1f), Ty(0.62f), k / 6f);
                            B.Loft(TorsoBand(ya, ya + 0.05f * S, 1, 1.15f + k * 0.004f, add * 2f), TorsoSeg, Metal, false, false);
                        }
                        B.Loft(TorsoBand(Ty(0.64f), Ty(0.97f), 3, 1.15f, add * 2f), TorsoSeg, Metal, false, false);
                    }
                }

                var shoulders = Piece(ArmorSlot.Shoulders);
                var armP = Piece(ArmorSlot.Arms);
                var gloves = Piece(ArmorSlot.Gloves);
                for (int side = -1; side <= 1; side += 2)
                {
                    bool left = side < 0;
                    int sh = left ? SK.LeftShoulder : SK.RightShoulder, arm = sh + 1;
                    var ar = arms[side];
                    if (shoulders != null && Armor.Covers(ArmorSlot.Shoulders, side))
                    {
                        Vector3 j0 = M.Joints[arm];
                        int layers = 3;
                        for (int k = 0; k < layers; k++)
                        {
                            float w = shoulders.Heavy ? 0.19f : 0.15f;
                            Vector3 p = j0 + new Vector3(side * (0.0f + k * 0.025f), 0.05f - k * 0.045f, 0f) * S;
                            B.Box(p, new Vector3(w * 0.85f, shoulders.Heavy ? 0.03f : 0.02f, w) * S,
                                Quaternion.Euler(0, 0, -side * (18f + k * 14f)), Mat(shoulders), new BW(sh, arm, 0.75f));
                        }
                    }
                    if (armP != null && Armor.Covers(ArmorSlot.Arms, side))
                    {
                        var rings = new List<Ring>();
                        int from = armP.Heavy ? 1 : 3;
                        for (int i = from; i < ar.Count; i++) rings.Add(ar[i].Inflated(armP.Heavy ? 1.22f : 1.16f, add * 2f));
                        B.Loft(rings, LimbSeg, Mat(armP), false, false);
                        if (armP.Heavy) B.Box(ar[3].C + Vector3.back * 0.035f * S, new Vector3(0.07f, 0.07f, 0.04f) * S, Quaternion.identity, Metal, new BW(arm, arm + 1, 0.5f));
                    }
                    else if (left && armP == null && gloves == null)
                    {
                        // Leather bracer on the shield forearm (default look).
                        B.Loft(new List<Ring> { ar[4].Inflated(1.15f, add), ar[5].Inflated(1.25f, add) }, LimbSeg, Leather, false, false);
                    }
                    if (gloves != null && Armor.Covers(ArmorSlot.Gloves, side))
                    {
                        var h = hands[side];
                        var g = new List<Ring> { ar[5].Inflated(1.45f, add * 2f) };
                        foreach (var r in h) g.Add(r.Inflated(1.2f, add));
                        B.Loft(g, 6, Mat(gloves), false, true);
                    }
                }

                var legP = Piece(ArmorSlot.Legs);
                var ankles = Piece(ArmorSlot.Ankles);
                for (int side = -1; side <= 1; side += 2)
                {
                    var lg = legs[side];
                    int leg = side < 0 ? SK.LeftLeg : SK.RightLeg;
                    if (legP != null && Armor.Covers(ArmorSlot.Legs, side))
                    {
                        B.Loft(new List<Ring> { lg[1].Inflated(1.14f, add * 2f), lg[2].Inflated(1.16f, add * 2f), Mid(lg[2], lg[3], 0.7f).Inflated(1.18f, add * 2f) },
                            LimbSeg, Mat(legP), false, false);
                        if (legP.Heavy)
                            B.Box(lg[3].C + Vector3.forward * (lg[3].RVPos + 0.02f * S), new Vector3(0.08f, 0.09f, 0.035f) * S, Quaternion.identity, Metal, new BW(leg, leg + 1, 0.5f));
                    }
                    if (ankles != null && Armor.Covers(ArmorSlot.Ankles, side))
                    {
                        if (ankles.Heavy)
                            B.Loft(new List<Ring> { lg[3].Inflated(1.2f, add * 2f), lg[4].Inflated(1.15f, add * 2f), lg[5].Inflated(1.3f, add * 2f), lg[6].Inflated(1.35f, add * 2f) },
                                LimbSeg, Metal, false, false);
                        else
                            B.Loft(new List<Ring> { Mid(lg[4], lg[5], 0.6f).Inflated(1.22f, add * 2f), lg[5].Inflated(1.24f, add * 2f), lg[6].Inflated(1.28f, add * 2f) },
                                LimbSeg, Leather, false, false);
                    }
                }
            }
        }
    }
}
