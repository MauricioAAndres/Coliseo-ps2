using System.Collections;
using Gladiator.Core;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>Hit-stop and cheap cube "particles" (blood / sparks), PS2 style.</summary>
    public class HitFeedback : MonoBehaviour
    {
        static HitFeedback inst;
        static Mesh cubeMesh;

        static HitFeedback Inst
        {
            get
            {
                if (inst == null) inst = new GameObject("HitFeedback").AddComponent<HitFeedback>();
                return inst;
            }
        }

        public static void HitStop(float duration)
        {
            Inst.StopAllCoroutines();
            Inst.StartCoroutine(Inst.DoHitStop(duration));
        }

        IEnumerator DoHitStop(float d)
        {
            if (GameInput.Paused) yield break;
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(d);
            if (!GameInput.Paused) Time.timeScale = 1f; // the pause menu restores time itself
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            if (inst == this) inst = null;
        }

        public static void Sparks(Vector3 pos, Color color, int count, float speed)
        {
            if (cubeMesh == null) cubeMesh = LowPolyMesh.Box(Vector3.one);
            Material mat = PS2Materials.Flat(color);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Spark");
                go.transform.position = pos + Random.insideUnitSphere * 0.1f;
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * Random.Range(0.05f, 0.11f);
                go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var sp = go.AddComponent<SparkParticle>();
                sp.Velocity = (Random.onUnitSphere + Vector3.up * 0.8f).normalized * speed * Random.Range(0.4f, 1f);
                sp.Life = Random.Range(0.35f, 0.7f);
            }
        }
    }

    public class SparkParticle : MonoBehaviour
    {
        public Vector3 Velocity;
        public float Life = 0.5f;
        float startLife = -1f;
        Vector3 startScale;

        void Start() { startLife = Life; startScale = transform.localScale; }

        void Update()
        {
            float dt = Time.deltaTime;
            Velocity += Physics.gravity * dt;
            Vector3 p = transform.position + Velocity * dt;
            if (p.y < 0.03f) { p.y = 0.03f; Velocity *= 0.3f; Velocity.y = 0f; }
            transform.position = p;
            Life -= dt;
            transform.localScale = startScale * Mathf.Clamp01(Life / Mathf.Max(0.01f, startLife) + 0.3f);
            if (Life <= 0f) Destroy(gameObject);
        }
    }
}
