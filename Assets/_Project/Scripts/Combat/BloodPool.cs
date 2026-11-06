using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// The fallen stay on the sand until the fight is over (the scene is only left at the end): a dark pool spreads
    /// under each body over a couple of seconds and follows it while it settles.
    /// </summary>
    public class BloodPool : MonoBehaviour
    {
        static Mesh disc;
        Fighter body;
        float t;

        public static void Spawn(Fighter f)
        {
            if (disc == null) disc = LowPolyMesh.Disc(1f, 12);
            var go = new GameObject("BloodPool");
            go.transform.position = f.transform.position + Vector3.up * 0.025f;
            go.transform.localScale = Vector3.one * 0.05f;
            go.AddComponent<MeshFilter>().sharedMesh = disc;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = PS2Materials.Flat(new Color(0.32f, 0.03f, 0.02f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var bp = go.AddComponent<BloodPool>();
            bp.body = f;
        }

        void Update()
        {
            if (t >= 3f) return;
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 3f));
            transform.localScale = new Vector3(0.95f, 1f, 0.75f) * Mathf.Lerp(0.05f, 1f, k);
            if (body != null)
            {
                // Centre it under the torso (the body lies along its facing, roughly).
                Vector3 p = body.transform.position - body.transform.forward * 0.55f;
                p.y = body.transform.position.y + 0.025f;
                transform.SetPositionAndRotation(p, Quaternion.Euler(0, body.transform.eulerAngles.y, 0));
            }
        }
    }
}
