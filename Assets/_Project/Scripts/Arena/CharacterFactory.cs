using Gladiator.Character;
using Gladiator.Combat;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Arena
{
    /// <summary>Quick look for NPCs (rivals, merchant): colours + helmet. The body is randomised from the name.</summary>
    public struct GladiatorLook
    {
        public Color Tunic;
        public Color Skin;
        public bool Helmet;
        public Color Crest;
    }

    /// <summary>
    /// Builds a playable/AI fighter around a <see cref="HumanoidRig"/> (SOMA skeleton + skinned low-poly mesh).
    /// The combat hooks are the same as before: BodyRoot (death fall), ArmPivot (procedural sword arm, now
    /// followed by the rig's right arm through IK), WeaponSocket and the blob shadow.
    /// </summary>
    public static class CharacterFactory
    {
        public static readonly Color AmbientSky = new Color(0.5f, 0.48f, 0.52f);
        public static readonly Color AmbientGround = new Color(0.34f, 0.26f, 0.18f);

        /// <summary>NPC from a quick look: deterministic random body from the name, colours from the look.</summary>
        public static Fighter Create(string name, GladiatorLook look, Vector3 position, Quaternion rotation, int team, ArmorSet armor = null)
        {
            return Create(name, FromLook(look, name), position, rotation, team, armor);
        }

        public static CharacterAppearance FromLook(GladiatorLook look, string seedName)
        {
            var a = CharacterAppearance.Random(new System.Random(seedName.GetHashCode()), false);
            a.Name = seedName;
            a.Helmet = look.Helmet;
            a.Outfit = a.Outfit == 2 ? 1 : a.Outfit;
            a.ClothOverride = look.Tunic;
            a.CrestOverride = look.Crest;
            a.SkinTone = Mathf.Min(a.SkinTone, 5);
            return a;
        }

        /// <summary>
        /// Rebuilds a fighter's body with new clothes/armour (the mercator's EQUIPO tab). Bones, mesh and poser are
        /// replaced; weapon sockets and the shield live on the body root, so they stay.
        /// </summary>
        public static void RebuildBody(Fighter f, CharacterAppearance look, ArmorSet armor)
        {
            if (f.Ragdolled) f.Rag.Release(0f);
            var oldRig = f.GetComponentInChildren<HumanoidRig>();
            Transform body = f.BodyRoot != null ? f.BodyRoot : f.transform;
            if (oldRig != null)
            {
                if (oldRig.Renderer != null) Object.Destroy(oldRig.Renderer.sharedMesh);
                oldRig.gameObject.SetActive(false);
                Object.Destroy(oldRig.gameObject);
            }
            HumanoidRig rig = HumanoidBuilder.Build(body, look, AmbientSky, AmbientGround, armor ?? ArmorSet.Empty);
            var poser = f.GetComponent<HumanoidPoser>();
            if (poser != null) poser.Init(rig);
            var rs = new System.Collections.Generic.List<Renderer> { rig.Renderer };
            if (f.FlashRenderers != null)
                foreach (var r in f.FlashRenderers) if (r != null && oldRig != null && r != oldRig.Renderer) rs.Add(r);
            f.FlashRenderers = rs.ToArray();
            f.SetArmor(armor);
            if (PlanarShadow.Attach(f.transform, rig.Renderer) && f.BlobShadow != null && f.IsAlive) f.BlobShadow.SetActive(false);
        }

        public static Fighter Create(string name, CharacterAppearance look, Vector3 position, Quaternion rotation, int team, ArmorSet armor = null)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, rotation);

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            HumanoidRig rig = HumanoidBuilder.Build(body, look, AmbientSky, AmbientGround, armor);
            BodyMetrics m = rig.Metrics;

            var cc = root.AddComponent<CharacterController>();
            cc.height = Mathf.Max(1.5f, m.H + 0.05f);
            cc.radius = 0.4f;
            cc.center = new Vector3(0, cc.height * 0.5f, 0);
            root.AddComponent<PropPusher>(); // walking into crates and jars shoves them
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.04f;
            cc.slopeLimit = 50f;

            root.AddComponent<Health>();
            var f = root.AddComponent<Fighter>();
            f.DisplayName = name;
            f.TeamId = team;

            // Sword-arm pivot at the right shoulder (points along +Z); the weapon socket sits at the fist.
            var pivot = new GameObject("ArmPivotR").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = m.Joints[SomaSkeleton.RightArm];
            var socket = new GameObject("WeaponSocket").transform;
            socket.SetParent(pivot, false);
            socket.localPosition = new Vector3(0, 0, (m.UpperArm + m.ForeArm) * 0.86f);
            var leftSocket = new GameObject("LeftSocket").transform; // placed on the left hand by the poser
            leftSocket.SetParent(body, false);
            leftSocket.localPosition = m.Joints[SomaSkeleton.LeftHand];

            // Blob shadow (unlit dark disc, no real-time shadows on PS2)
            var shadow = new GameObject("BlobShadow");
            shadow.transform.SetParent(root.transform, false);
            shadow.transform.localPosition = new Vector3(0, 0.02f, 0);
            shadow.AddComponent<MeshFilter>().sharedMesh = LowPolyMesh.Disc(0.5f * m.S * (1f + look.Fat * 0.3f), 10);
            var smr = shadow.AddComponent<MeshRenderer>();
            smr.sharedMaterial = PS2Materials.Flat(new Color(0.3f, 0.22f, 0.14f));
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var poser = root.AddComponent<HumanoidPoser>();
            poser.Init(rig);
            poser.Fighter = f;
            poser.ArmPivot = pivot;
            poser.WeaponSocket = socket;
            poser.LeftSocket = leftSocket;

            f.BodyRoot = body;
            f.ArmPivot = pivot;
            f.WeaponSocket = socket;
            f.LeftSocket = leftSocket;
            f.BlobShadow = shadow;
            f.FlashRenderers = new Renderer[] { rig.Renderer };
            f.SetArmor(rig.Armor);
            // Planar shadow of the silhouette (falls back to the blob if the shader is unavailable).
            if (PlanarShadow.Attach(root.transform, rig.Renderer)) shadow.SetActive(false);
            return f;
        }
    }
}
