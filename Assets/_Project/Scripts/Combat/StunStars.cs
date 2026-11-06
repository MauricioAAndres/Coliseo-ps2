using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// Little yellow stars circling over a stunned fighter's head (guard broken by a kick).
    /// Created on demand by <see cref="Fighter"/>; hidden while not stunned.
    /// </summary>
    public class StunStars : MonoBehaviour
    {
        const int Count = 5;
        static Mesh starMesh;
        Transform[] stars;
        Fighter fighter;
        float spin;

        public static StunStars Create(Fighter f)
        {
            var go = new GameObject("StunStars");
            go.transform.SetParent(f.transform, false);
            var s = go.AddComponent<StunStars>();
            s.fighter = f;
            if (starMesh == null) starMesh = LowPolyMesh.Box(new Vector3(0.07f, 0.07f, 0.07f));
            Material mat = PS2Materials.Flat(new Color(1f, 0.88f, 0.25f));
            s.stars = new Transform[Count];
            for (int i = 0; i < Count; i++)
            {
                var star = new GameObject("Star" + i);
                star.transform.SetParent(go.transform, false);
                star.AddComponent<MeshFilter>().sharedMesh = starMesh;
                var mr = star.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                s.stars[i] = star.transform;
            }
            go.SetActive(false);
            return s;
        }

        public void Show(bool on)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        void LateUpdate()
        {
            spin += Time.deltaTime * 260f;
            float h = fighter != null && fighter.Controller != null ? fighter.Controller.height : 1.8f;
            transform.localPosition = new Vector3(0f, h + 0.12f, 0f);
            for (int i = 0; i < Count; i++)
            {
                float a = (spin + i * 360f / Count) * Mathf.Deg2Rad;
                stars[i].localPosition = new Vector3(Mathf.Cos(a) * 0.24f, Mathf.Sin(a * 2f) * 0.03f, Mathf.Sin(a) * 0.24f);
                stars[i].localRotation = Quaternion.Euler(45f, spin * 2f + i * 40f, 45f);
            }
        }
    }
}
