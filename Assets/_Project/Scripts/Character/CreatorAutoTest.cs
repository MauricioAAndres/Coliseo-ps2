using System.Collections;
using System.IO;
using UnityEngine;

namespace Gladiator.Character
{
    /// <summary>
    /// "-autotest" for the creator: checks the rig is the 30-joint SOMA skeleton, drives every tab, takes
    /// screenshots of extreme and random bodies, then finishes with a fixed gladiator and hands over to the ludus
    /// (where TrainingAutoTest continues the usual 4-day loop with that character).
    /// </summary>
    public class CreatorAutoTest : MonoBehaviour
    {
        CreatorBootstrap boot;

        public void Init(CreatorBootstrap b) { boot = b; }

        static void Log(string s) { Debug.Log("[CREATOR] " + s); }

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "autotest_" + name + ".png");
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log("SCREENSHOT " + path);
        }

        void Describe(string tag)
        {
            var r = boot.Rig;
            var a = boot.Look;
            Log($"{tag}: tris={r.Triangles} H={r.Height:0.00}m fem={a.Femininity:0.00} mus={a.Muscle:0.00} fat={a.Fat:0.00} " +
                $"hair={a.HairStyle} beard={a.Beard} outfit={a.Outfit} armor={a.Armor} helmet={a.Helmet} submeshes={r.Renderer.sharedMesh.subMeshCount}");
        }

        void SetAll(float v)
        {
            var a = boot.Look;
            a.Height = a.Muscle = a.Fat = a.Shoulders = a.LegLength = a.ArmLength = v;
            a.HeadSize = a.Jaw = a.Nose = a.Neck = v;
            boot.Rebuild();
        }

        IEnumerator Start()
        {
            yield return Wait(1.0f);
            var rig = boot.Rig;
            bool soma = rig.Bones.Length == SomaSkeleton.Count;
            for (int i = 0; soma && i < SomaSkeleton.Count; i++) soma &= rig.Bones[i].name == SomaSkeleton.Names[i];
            Log($"RIG joints={rig.Bones.Length} somaNamesMatch={soma} root={rig.Bones[0].name} smrBones={rig.Renderer.bones.Length}");
            Describe("DEFAULT");
            Log($"CLIPS usingMotionBricks={boot.UsingClips} clips={MotionLibrary.Load().Count}");
            yield return Shot("c01_default");

            // Drive the real menu: next tab, move rows, step values.
            var menu = boot.Menu;
            menu.SetTab(0);
            menu.SelectRow(2); menu.Step(1); menu.Step(1); // muscle +10%
            yield return null;
            Describe("MENU_MUSCLE+");

            SetAll(0f);
            yield return Wait(0.3f);
            Describe("ALL_MIN");
            yield return Shot("c02_min");
            SetAll(1f);
            yield return Wait(0.3f);
            Describe("ALL_MAX");
            yield return Shot("c03_max");

            for (int k = 0; k < 3; k++)
            {
                CreatorBootstrap.CopyInto(CharacterAppearance.Random(new System.Random(1234 + k * 77)), boot.Look);
                boot.Rebuild();
                boot.Yaw = 20f + k * 50f;
                yield return Wait(0.4f);
                Describe("RANDOM" + k);
                yield return Shot("c0" + (4 + k) + "_random");
            }

            // Head tab close-up with long hair and beard, then a helmet.
            var look = boot.Look;
            look.Femininity = 0f; look.HairStyle = 5; look.Beard = 3; look.HairColor = 3; look.Helmet = false; look.Nose = 0.8f; look.Jaw = 0.8f;
            boot.Rebuild();
            menu.SetTab(1);
            boot.Yaw = 25f;
            yield return Wait(1.2f);
            Describe("HEAD_HAIR");
            yield return Shot("c07_head");
            boot.PreviewArmor = ArmorSet.FromIds(new[] { "yelmo" });
            look.HelmetCrest = 1; look.Metal = 0;
            boot.Rebuild();
            yield return Wait(0.3f);
            yield return Shot("c08_helmet");
            foreach (var h in new[] { "galea_cuero", "galea_bronce" })
            {
                boot.PreviewArmor = ArmorSet.FromIds(new[] { h });
                boot.Rebuild();
                yield return Wait(0.3f);
                yield return Shot("c08_" + h);
            }

            // Clothes: the three shirts, three pants and both sandals in a few combinations.
            menu.SetTab(2);
            boot.PreviewArmor = null;
            look.HairStyle = 1; look.Beard = 2;
            int[,] combos = { { 1, 0, 0 }, { 2, 1, 1 }, { 3, 2, 0 }, { 0, 2, 1 }, { 4, 3, 1 }, { 4, 4, 0 } };
            for (int k = 0; k < combos.GetLength(0); k++)
            {
                look.Shirt = combos[k, 0]; look.Pants = combos[k, 1]; look.Shoes = combos[k, 2];
                look.ClothColor = 1 + k; look.PantsColor = 4 + k % 3;
                boot.Rebuild();
                boot.Yaw = 25f + k * 30f;
                yield return Wait(0.4f);
                Log($"CLOTHES shirt={CharacterAppearance.Shirts[look.Shirt]} pants={CharacterAppearance.PantsNames[look.Pants]} shoes={CharacterAppearance.ShoesNames[look.Shoes]} tris={boot.Rig.Triangles}");
                yield return Shot($"c15_ropa_{k}");
            }

            // Armour: full light set and full heavy set.
            var light = new System.Collections.Generic.List<string>();
            var heavy = new System.Collections.Generic.List<string>();
            foreach (var p in ArmorLibrary.All) { if (p.Heavy) heavy.Add(p.Id); else if (p.Id != "galea_cuero") light.Add(p.Id); }
            heavy.Add("yelmo");
            look.Shirt = 1; look.Pants = 0; look.Shoes = 1;
            foreach (var (tag, ids) in new[] { ("ligera", light), ("pesada", heavy) })
            {
                boot.PreviewArmor = ArmorSet.FromIds(ids);
                boot.Rebuild();
                foreach (var yaw in new[] { 30f, 200f })
                {
                    boot.Yaw = yaw;
                    yield return Wait(0.4f);
                    yield return Shot($"c16_armadura_{tag}_{(yaw < 100f ? "frente" : "espalda")}");
                }
                Log($"ARMOR {tag}: pieces={boot.PreviewArmor.Count} weight={boot.PreviewArmor.Weight:0.#}kg tris={boot.Rig.Triangles} " +
                    $"head={boot.PreviewArmor.Protection(ArmorZone.Head):0.00} torso={boot.PreviewArmor.Protection(ArmorZone.Torso):0.00} " +
                    $"arms={boot.PreviewArmor.Protection(ArmorZone.Arms):0.00} legs={boot.PreviewArmor.Protection(ArmorZone.Legs):0.00}");
            }
            // One-sided limb armour + the undershirt under a chest piece (no shirt).
            look.Shirt = 0;
            boot.PreviewArmor = ArmorSet.FromIds(new[] { "lorica", "hombreras_hierro", "manica_hierro", "guanteletes", "musleras_cuero", "grebas" },
                                                 new[] { 0, 0, 1, 2, 1, 0, 2, 0 });
            boot.Rebuild();
            boot.Yaw = 20f;
            yield return Wait(0.4f);
            yield return Shot("c18_armadura_un_lado");
            Log($"SIDES weight={boot.PreviewArmor.Weight:0.#}kg arms={boot.PreviewArmor.Protection(ArmorZone.Arms):0.00} tris={boot.Rig.Triangles} submeshes={boot.Rig.Renderer.sharedMesh.subMeshCount}");
            boot.PreviewArmor = null;
            look.Shirt = 1;

            // Animation preview: every MotionBricks clip on the plinth.
            menu.SetTab(0);
            boot.Rebuild();
            menu.SetTab(0);
            for (int i = 0; i < CreatorBootstrap.PreviewNames.Length; i++)
            {
                boot.SetPreviewAnim(i);
                boot.Yaw = i == 3 ? 0f : 70f;
                yield return Wait(0.5f);
                // Head pitch of the face (0 = level, + = up) over one second, after the head layer.
                float pMin = 90f, pMax = -90f;
                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
                {
                    Vector3 face = boot.Rig.Bones[SomaSkeleton.Head].rotation * Vector3.forward;
                    float p = Mathf.Asin(Mathf.Clamp(face.y, -1f, 1f)) * Mathf.Rad2Deg;
                    pMin = Mathf.Min(pMin, p); pMax = Mathf.Max(pMax, p);
                    yield return null;
                }
                Log($"ANIM {CreatorBootstrap.PreviewNames[i]} headPitch {pMin:0}..{pMax:0} deg");
                if (i == 0) continue;
                yield return Shot("c11_anim_" + CreatorBootstrap.PreviewNames[i].ToLowerInvariant());
            }
            // Combat clips on the plinth, captured at their key moment.
            float[] at = { 0f, 0f, 0f, 0f, 0f, 0.5f, 0.42f, 0.42f, 0.55f, 0.3f, 1.1f };
            for (int i = 5; i <= 10; i++)
            {
                boot.SetPreviewAnim(i);
                boot.Yaw = 55f;
                var clipName = new[] { "guard", "sword_light", "sword_heavy", "block", "stagger", "death" }[i - 5];
                var mc = MotionLibrary.Get(clipName);
                float wait = mc == null ? 0.5f : (i == 5 ? 0.6f : mc.Duration * at[i] + 0.02f);
                yield return Wait(wait);
                Log($"COMBAT {CreatorBootstrap.PreviewNames[i]} playing={boot.ActionPlaying ?? "-"}");
                yield return Shot("c12_" + clipName);
                yield return Wait(Mathf.Max(0f, (mc != null ? mc.Duration + 0.8f : 0f) - wait));
            }
            // Movement clips on the plinth, each at its key moments (roll mid-spin, hang, pull...).
            string[] moveClips = { "dodge", "dodge_back", "jump", "fall", "land", "crouch", "crouch", "vault", "climb", "kick", "stunned" };
            for (int i = 11; i < CreatorBootstrap.PreviewNames.Length; i++)
            {
                string cn = moveClips[i - 11];
                var mc = MotionLibrary.Get(cn);
                boot.SetPreviewAnim(0); yield return null;
                boot.SetPreviewAnim(i);
                boot.Yaw = i == 16 || i == 17 ? 70f : 60f;
                float dur = mc != null ? mc.Duration : 1f;
                float[] marks = mc != null && mc.markers != null && mc.markers.Length > 0 && !mc.loop ? mc.markers : new[] { 0.5f };
                float waited = 0f;
                for (int k = 0; k < marks.Length; k++)
                {
                    float until = marks[k] * dur + 0.03f;
                    if (cn == "dodge" && k == 1) until = (marks[0] + marks[1]) * 0.5f * dur; // mid-spin
                    if (until < waited) continue;
                    yield return Wait(until - waited); waited = until;
                    if (k == 0) Log($"MOVE {CreatorBootstrap.PreviewNames[i]} playing={boot.ActionPlaying ?? "-"} clip={(mc != null)}");
                    yield return Shot($"c14_{i - 11}_{CreatorBootstrap.PreviewNames[i].ToLowerInvariant().Replace(' ', '_')}_{k}");
                }
            }

            // Sword swings frame by frame: wind-up, contact, follow-through.
            for (int i = 6; i <= 7; i++)
            {
                var mc = MotionLibrary.Get(i == 6 ? "sword_light" : "sword_heavy");
                if (mc == null) continue;
                boot.SetPreviewAnim(0); yield return null;
                boot.SetPreviewAnim(i);
                boot.Yaw = 35f;
                float m0 = mc.Marker(0, 0.3f), m1 = mc.Marker(1, 0.6f), waited = 0f;
                float[] times = { m0, (m0 + m1) * 0.5f, m1 };
                for (int k = 0; k < 3; k++)
                {
                    float until = times[k] * mc.Duration + 0.03f;
                    yield return Wait(until - waited); waited = until;
                    yield return Shot($"c13_{mc.name}_{k}");
                }
            }
            boot.SetPreviewAnim(0);

            // Typing a name of your own.
            menu.SetTab(2);
            menu.SelectRow(0);
            menu.BeginNameEdit();
            menu.ClearName();
            menu.TypeText("Máximo Décimo");
            yield return Wait(0.3f);
            yield return Shot("c17_nombre");
            menu.EndNameEdit();
            Log($"NAME typed='{boot.Look.Name}' editing={menu.EditingName}");

            // The gladiator that goes on to the ludus.
            menu.SetTab(2);
            var final = new CharacterAppearance
            {
                Name = "Flamma", Femininity = 0.05f, Height = 0.75f, Muscle = 0.9f, Fat = 0.15f, Shoulders = 0.75f,
                LegLength = 0.5f, ArmLength = 0.5f, HeadSize = 0.45f, Jaw = 0.75f, Nose = 0.55f, Neck = 0.8f,
                HairStyle = 1, Beard = 2, HairColor = 1, SkinTone = 3, Outfit = 0, ClothColor = 1, Armor = 3, Metal = 0, Helmet = false,
            };
            CreatorBootstrap.CopyInto(final, boot.Look);
            boot.Rebuild();
            boot.Yaw = 15f;
            menu.SetTab(0);
            yield return Wait(1.2f);
            Describe("FINAL");
            yield return Shot("c09_final");
            menu.ShowConfirm(true);
            yield return Wait(0.4f);
            yield return Shot("c10_confirm");
            menu.ShowConfirm(false);
            Log("FINISH -> ludus");
            boot.Finish();
        }
    }
}
