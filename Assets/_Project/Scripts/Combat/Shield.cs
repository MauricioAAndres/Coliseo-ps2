using System.Collections.Generic;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// A shield on the left forearm: scutum (tall rectangle), wooden parma (round) or iron clipeus (round).
    /// Its block numbers apply through Fighter.RecomputeEquipment. With a two-handed weapon or a bow it is slung on
    /// the back (OnBack): it gives no block bonus there and only weighs. The body animator places it every frame;
    /// LateUpdate below is the fallback for the procedural rig.
    /// </summary>
    public class Shield : MonoBehaviour
    {
        public ShieldStats Stats { get; private set; }
        public bool OnBack { get; set; }
        public Transform Pivot => pivot;

        Fighter fighter;
        Transform pivot;
        float scale = 1f;
        static readonly Vector3 RestPos = new Vector3(-0.5f, 1.12f, 0.12f);
        static readonly Quaternion RestRot = Quaternion.Euler(0f, -70f, 0f);
        static readonly Vector3 GuardPos = new Vector3(-0.12f, 1.2f, 0.48f);
        static readonly Quaternion GuardRot = Quaternion.Euler(-5f, -8f, 0f);
        static readonly Vector3 BackPos = new Vector3(0f, 1.15f, -0.2f);
        static readonly Quaternion BackRot = Quaternion.Euler(0f, 180f, 0f);

        public static Shield Attach(Fighter f, Color face) => Attach(f, ShieldLibrary.Scutum, face);

        public static Shield Attach(Fighter f, ShieldStats stats, Color face)
        {
            var old = f.GetComponent<Shield>();
            if (old != null) old.Remove();

            var s = f.gameObject.AddComponent<Shield>();
            s.fighter = f;
            s.Stats = stats ?? ShieldLibrary.Scutum;
            Transform body = f.BodyRoot != null ? f.BodyRoot : f.transform;
            var rig = f.GetComponentInChildren<Gladiator.Character.HumanoidRig>();
            s.scale = rig != null ? rig.Scale : 1f;
            s.pivot = new GameObject("Shield").transform;
            s.pivot.SetParent(body, false);
            s.pivot.localPosition = RestPos * s.scale;
            s.pivot.localRotation = RestRot;

            var rs = new List<Renderer>(f.FlashRenderers ?? new Renderer[0]);
            s.BuildVisual(face, rs);
            f.FlashRenderers = rs.ToArray();
            LowPolyMesh.BakeAmbient(s.pivot.gameObject, new Color(0.5f, 0.48f, 0.52f), new Color(0.34f, 0.26f, 0.18f));
            f.EquippedShield = s;
            f.RecomputeEquipment();
            return s;
        }

        /// <summary>Takes the shield off (another one replaces it).</summary>
        public void Remove()
        {
            if (fighter != null && fighter.EquippedShield == this) { fighter.EquippedShield = null; fighter.RecomputeEquipment(); }
            if (pivot != null)
            {
                var keep = new List<Renderer>();
                if (fighter != null && fighter.FlashRenderers != null)
                    foreach (var r in fighter.FlashRenderers) if (r != null && !r.transform.IsChildOf(pivot)) keep.Add(r);
                if (fighter != null) fighter.FlashRenderers = keep.ToArray();
                Destroy(pivot.gameObject);
            }
            Destroy(this);
        }

        void OnDestroy()
        {
            if (fighter != null) fighter.RecomputeEquipment();
        }

        void BuildVisual(Color face, List<Renderer> rs)
        {
            Material bronze = PS2Materials.Get("bronze", PS2Textures.Metal, new Color(0.95f, 0.72f, 0.4f));
            switch (Stats.Shape)
            {
                case ShieldShape.Scutum:
                {
                    Material board = PS2Materials.Get("shield_" + ColorUtility.ToHtmlStringRGB(face), PS2Textures.Cloth, face);
                    rs.Add(PS2Spawn.Box(pivot, "Board", new Vector3(0.58f, 0.86f, 0.06f), Vector3.zero, board).GetComponent<Renderer>());
                    rs.Add(PS2Spawn.Box(pivot, "RimT", new Vector3(0.6f, 0.04f, 0.08f), new Vector3(0, 0.43f, 0), bronze).GetComponent<Renderer>());
                    rs.Add(PS2Spawn.Box(pivot, "RimB", new Vector3(0.6f, 0.04f, 0.08f), new Vector3(0, -0.43f, 0), bronze).GetComponent<Renderer>());
                    rs.Add(PS2Spawn.Box(pivot, "Boss", new Vector3(0.16f, 0.16f, 0.08f), new Vector3(0, 0, 0.05f), bronze).GetComponent<Renderer>());
                    break;
                }
                case ShieldShape.RoundWood:
                {
                    Color paint = Color.Lerp(new Color(0.9f, 0.75f, 0.55f), face, 0.7f);
                    Material wood = PS2Materials.Get("shield_wood_" + ColorUtility.ToHtmlStringRGB(paint), PS2Textures.Wood, paint);
                    Material iron = PS2Materials.Get("metal_dark", PS2Textures.Metal, new Color(0.45f, 0.45f, 0.5f));
                    rs.Add(Disc("Board", 0.31f, 0.05f, 0f, wood));
                    rs.Add(PS2Spawn.Box(pivot, "PlankA", new Vector3(0.6f, 0.03f, 0.055f), new Vector3(0, 0.1f, 0.005f), iron).GetComponent<Renderer>());
                    rs.Add(PS2Spawn.Box(pivot, "PlankB", new Vector3(0.6f, 0.03f, 0.055f), new Vector3(0, -0.1f, 0.005f), iron).GetComponent<Renderer>());
                    rs.Add(Disc("Boss", 0.08f, 0.07f, 0.03f, iron));
                    break;
                }
                case ShieldShape.RoundIron:
                {
                    Material iron = PS2Materials.Get("shield_iron", PS2Textures.Metal, new Color(0.78f, 0.78f, 0.82f));
                    Material dark = PS2Materials.Get("metal_dark", PS2Textures.Metal, new Color(0.45f, 0.45f, 0.5f));
                    Material paint = PS2Materials.Get("shield_paint_" + ColorUtility.ToHtmlStringRGB(face), PS2Textures.Cloth, face);
                    rs.Add(Disc("Rim", 0.3f, 0.045f, -0.005f, dark));
                    rs.Add(Disc("Face", 0.265f, 0.05f, 0.005f, iron));
                    rs.Add(Disc("Paint", 0.2f, 0.052f, 0.006f, paint));
                    rs.Add(Disc("Boss", 0.09f, 0.08f, 0.04f, bronze));
                    break;
                }
            }
        }

        Renderer Disc(string name, float radius, float thickness, float z, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(pivot, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis Y -> facing +Z
            go.transform.localPosition = new Vector3(0f, 0f, z - thickness * 0.5f);
            go.AddComponent<MeshFilter>().sharedMesh = LowPolyMesh.Cylinder(radius, thickness, 10);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // PS3 look only
            mr.receiveShadows = false;
            return mr;
        }

        void LateUpdate()
        {
            if (fighter == null || pivot == null || fighter.State == FighterState.Dead) return;
            bool guard = fighter.State == FighterState.Blocking && !OnBack;
            float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
            Vector3 pos = OnBack ? BackPos : guard ? GuardPos : RestPos;
            Quaternion rot = OnBack ? BackRot : guard ? GuardRot : RestRot;
            pivot.localPosition = Vector3.Lerp(pivot.localPosition, pos * scale, k);
            pivot.localRotation = Quaternion.Slerp(pivot.localRotation, rot, k);
        }
    }
}
