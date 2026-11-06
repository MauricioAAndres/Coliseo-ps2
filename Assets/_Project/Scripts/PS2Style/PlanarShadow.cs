using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.PS2Style
{
    /// <summary>
    /// Character shadow: a second SkinnedMeshRenderer sharing the character's mesh and bones, drawn with
    /// PS2_PlanarShadow (the silhouette flattened on the ground along the light). The ground height under the
    /// character is found by a raycast every frame, so the shadow lands on blocks and platforms too.
    /// Falls back to the old blob if the shader is missing.
    /// </summary>
    public class PlanarShadow : MonoBehaviour
    {
        static Shader shader;
        static Material material;
        static readonly int GroundId = Shader.PropertyToID("_GroundY");

        /// <summary>Light direction used by every planar shadow (world, pointing down).</summary>
        public static Vector3 LightDirection = new Vector3(0.35f, -1f, 0.25f);

        SkinnedMeshRenderer source, shadow;
        Transform root;
        MaterialPropertyBlock mpb;
        readonly List<Collider> own = new List<Collider>();

        public static bool Available
        {
            get
            {
                if (shader == null) shader = Resources.Load<Shader>("Shaders/PS2_PlanarShadow");
                return shader != null && shader.isSupported;
            }
        }

        /// <summary>Adds (or rebinds) the planar shadow of a skinned character. Returns false if unavailable.</summary>
        public static bool Attach(Transform characterRoot, SkinnedMeshRenderer smr)
        {
            if (smr == null || !Available) return false;
            if (material == null)
            {
                material = new Material(shader) { name = "PS2_PlanarShadow (runtime)" };
                material.SetVector("_LightDir", LightDirection);
            }
            var ps = smr.gameObject.GetComponent<PlanarShadow>();
            if (ps == null) ps = smr.gameObject.AddComponent<PlanarShadow>();
            ps.Bind(characterRoot, smr);
            return true;
        }

        void Bind(Transform characterRoot, SkinnedMeshRenderer smr)
        {
            root = characterRoot;
            source = smr;
            mpb = new MaterialPropertyBlock();
            if (shadow == null)
            {
                var go = new GameObject("PlanarShadow");
                go.transform.SetParent(smr.transform.parent, false);
                go.transform.localPosition = smr.transform.localPosition;
                go.transform.localRotation = smr.transform.localRotation;
                shadow = go.AddComponent<SkinnedMeshRenderer>();
            }
            shadow.sharedMesh = smr.sharedMesh;
            shadow.bones = smr.bones;
            shadow.rootBone = smr.rootBone;
            shadow.updateWhenOffscreen = true;
            shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadow.receiveShadows = false;
            shadow.skinnedMotionVectors = false;
            var mats = new Material[smr.sharedMesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            shadow.sharedMaterials = mats;
            own.Clear();
            if (root != null) own.AddRange(root.GetComponentsInChildren<Collider>(true));
        }

        void LateUpdate()
        {
            if (shadow == null || source == null) return;
            bool want = !Gladiator.Core.GameSettings.PS3; // PS3: the sun casts real shadows
            if (shadow.forceRenderingOff == want) shadow.forceRenderingOff = !want;
            if (shadow.enabled != source.enabled || shadow.gameObject.activeSelf != source.gameObject.activeSelf)
            {
                shadow.enabled = source.enabled;
                shadow.gameObject.SetActive(source.gameObject.activeSelf);
            }
            Vector3 from = (root != null ? root.position : transform.position) + Vector3.up * 0.6f;
            float ground = from.y - 0.6f;
            var hits = Physics.RaycastAll(from, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (own.Contains(h.collider) || h.collider.attachedRigidbody != null) continue;
                if (h.distance < best) { best = h.distance; ground = h.point.y; }
            }
            mpb.SetFloat(GroundId, ground);
            shadow.SetPropertyBlock(mpb);
        }

        void OnDestroy()
        {
            if (shadow != null) Destroy(shadow.gameObject);
        }
    }
}
