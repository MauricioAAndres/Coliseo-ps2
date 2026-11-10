using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// Props for a fighter's activity: a straw mat to lie on, bread to eat, a pot on a small fire to cook.
    /// Weapons are hidden meanwhile. Added by <see cref="Fighter.TryActivity"/>; cleans up when the fighter gets up.
    /// </summary>
    public class ActivityProps : MonoBehaviour
    {
        Fighter fighter;
        GameObject props;
        Renderer[] hidden;
        Light fireLight;
        int kind = -1;

        public static void For(Fighter f)
        {
            var p = f.GetComponent<ActivityProps>();
            if (p == null) p = f.gameObject.AddComponent<ActivityProps>();
            p.fighter = f;
            p.Begin(f.ActivityKind);
        }

        void Begin(int k)
        {
            Clear();
            kind = k;
            enabled = true;
            // Hide what is held (weapons, bread goes to the hand).
            var list = new System.Collections.Generic.List<Renderer>();
            if (fighter.CurrentWeapon != null) list.AddRange(fighter.CurrentWeapon.GetComponentsInChildren<Renderer>());
            if (fighter.OffWeapon != null) list.AddRange(fighter.OffWeapon.GetComponentsInChildren<Renderer>());
            list.RemoveAll(r => r == null || !r.enabled);
            foreach (var r in list) r.enabled = false;
            hidden = list.ToArray();

            props = new GameObject("ActivityProps");
            Transform root = fighter.transform;
            switch (k)
            {
                case 0: // straw mat under the body (the body lies back from the feet); none in a bed
                    if (fighter.ActivitySkipIn) break;
                    props.transform.SetPositionAndRotation(root.position, root.rotation);
                    Box(props.transform, new Vector3(0f, 0.012f, -0.45f), new Vector3(0.75f, 0.025f, 2.0f), new Color(0.72f, 0.6f, 0.34f));
                    Box(props.transform, new Vector3(0f, 0.05f, -1.28f), new Vector3(0.45f, 0.08f, 0.22f), new Color(0.82f, 0.78f, 0.66f)); // pillow
                    break;
                case 2: // bread in the right hand
                {
                    Transform hand = fighter.WeaponSocket != null ? fighter.WeaponSocket : root;
                    props.transform.SetParent(hand, false);
                    var b = Shape(PrimitiveType.Sphere, props.transform, Vector3.zero, new Vector3(0.12f, 0.07f, 0.08f), new Color(0.72f, 0.45f, 0.18f));
                    b.transform.localPosition = new Vector3(0f, 0.02f, 0.05f);
                    break;
                }
                case 3: // pot on a small fire in front
                {
                    props.transform.SetPositionAndRotation(root.position + root.forward * 0.62f, root.rotation);
                    for (int i = 0; i < 3; i++)
                    {
                        var log = Shape(PrimitiveType.Cylinder, props.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.06f, 0.2f, 0.06f), new Color(0.3f, 0.18f, 0.08f));
                        log.transform.localRotation = Quaternion.Euler(90f, i * 60f, 0f);
                    }
                    Box(props.transform, new Vector3(0f, 0.07f, 0f), new Vector3(0.14f, 0.1f, 0.14f), new Color(1f, 0.55f, 0.1f)).transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                    Shape(PrimitiveType.Cylinder, props.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.34f, 0.1f, 0.34f), new Color(0.22f, 0.2f, 0.19f)); // pot
                    Shape(PrimitiveType.Cylinder, props.transform, new Vector3(0f, 0.351f, 0f), new Vector3(0.28f, 0.005f, 0.28f), new Color(0.55f, 0.35f, 0.15f)); // stew
                    var lgo = new GameObject("Fire");
                    lgo.transform.SetParent(props.transform, false);
                    lgo.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                    fireLight = lgo.AddComponent<Light>();
                    fireLight.type = LightType.Point;
                    fireLight.color = new Color(1f, 0.6f, 0.25f);
                    fireLight.range = 2.5f;
                    fireLight.intensity = 1.5f;
                    break;
                }
            }
        }

        static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color c) => Shape(PrimitiveType.Cube, parent, pos, size, c);

        static GameObject Shape(PrimitiveType type, Transform parent, Vector3 pos, Vector3 size, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = PS2Materials.Flat(c);
            return go;
        }

        void Clear()
        {
            if (props != null) Destroy(props);
            props = null;
            fireLight = null;
            if (hidden != null) foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden = null;
            kind = -1;
        }

        void Update()
        {
            if (fighter == null || fighter.State != FighterState.Activity || fighter.ActivityKind != kind ||
                (fighter.ActivityLeaving && fighter.StatePhase > 0.5f))
            {
                Clear();
                enabled = false;
                return;
            }
            if (fireLight != null) fireLight.intensity = 1.3f + 0.4f * Mathf.PerlinNoise(Time.time * 6f, 0.3f);
        }

        void OnDestroy() => Clear();
    }
}
