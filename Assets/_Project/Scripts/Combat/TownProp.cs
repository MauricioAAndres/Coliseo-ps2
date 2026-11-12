using System;
using System.Collections.Generic;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// A crate or a jar lying about the town: a rigid body you can shove by walking into it, pick up (E), carry in
    /// front of you, put down or throw (attack), and smash with blows. Jars shatter when they land hard.
    /// </summary>
    public class TownProp : MonoBehaviour
    {
        public enum Kind { Crate, Jar }

        public static readonly List<TownProp> All = new List<TownProp>();
        /// <summary>Called when one breaks (who broke it, or null; true if the news came over the net).</summary>
        public static Action<TownProp, Fighter, bool> OnBroken;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); OnBroken = null; }

        public int Id;
        public Kind Type;
        public float Hp;
        public Vector3 Size;
        public Fighter Holder { get; private set; }
        public bool Broken { get; private set; }
        public string Noun => Type == Kind.Crate ? "LA CAJA" : "EL JARRON";

        Rigidbody body;
        Collider col;
        Fighter lastHitter, thrownBy;
        float lastHit = -9f, thrownAt = -9f;
        Material mat;
        Color tint;

        public static TownProp Create(Transform parent, Kind kind, int id, Vector3 pos, float yaw, float scale)
        {
            var go = new GameObject(kind == Kind.Crate ? "Crate_" + id : "Jar_" + id);
            go.transform.SetParent(parent, false);
            var b = new MeshBuilder();
            Vector3 size;
            Material m;
            Color tint;
            if (kind == Kind.Crate)
            {
                float s = 0.6f * scale;
                size = new Vector3(s, s, s);
                Color wood = new Color(0.78f, 0.62f, 0.42f), dark = new Color(0.42f, 0.3f, 0.2f);
                b.AddBox(Vector3.zero, size, Quaternion.identity, wood, 1f);
                float t = 0.07f * scale, h = s * 0.5f, e = h - t * 0.35f;
                foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
                    b.AddBox(new Vector3(sx * e, 0f, sz * e), new Vector3(t, s + 0.01f, t), Quaternion.identity, dark);
                foreach (float sy in new[] { -1f, 1f })
                {
                    foreach (float sz in new[] { -1f, 1f }) b.AddBox(new Vector3(0f, sy * e, sz * e), new Vector3(s + 0.01f, t, t), Quaternion.identity, dark);
                    foreach (float sx in new[] { -1f, 1f }) b.AddBox(new Vector3(sx * e, sy * e, 0f), new Vector3(t, t, s + 0.01f), Quaternion.identity, dark);
                }
                m = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
                tint = wood;
            }
            else
            {
                float k = scale;
                size = new Vector3(0.42f * k, 0.62f * k, 0.42f * k);
                Color clay = Color.white;
                var prof = new[]
                {
                    new MeshBuilder.Ring(0.09f * k, -0.31f * k, 0.7f, -0.7f),
                    new MeshBuilder.Ring(0.19f * k, -0.14f * k, 0.9f, -0.4f),
                    new MeshBuilder.Ring(0.22f * k, 0.02f * k, 1f, 0f),
                    new MeshBuilder.Ring(0.17f * k, 0.16f * k, 0.8f, 0.6f),
                    new MeshBuilder.Ring(0.09f * k, 0.24f * k, 0.9f, 0.4f),
                    new MeshBuilder.Ring(0.12f * k, 0.31f * k, 0.9f, 0.4f),
                };
                b.AddLathe(prof, 8, Vector3.zero, clay);
                b.AddBox(new Vector3(0f, -0.305f * k, 0f), new Vector3(0.14f * k, 0.01f, 0.14f * k), Quaternion.identity, clay);
                b.AddBox(new Vector3(0f, 0.23f * k, 0f), new Vector3(0.15f * k, 0.01f, 0.15f * k), Quaternion.identity, new Color(0.15f, 0.1f, 0.08f));
                m = PS2Materials.Get("clay", PS2Textures.Sand, new Color(0.75f, 0.45f, 0.3f));
                tint = new Color(0.75f, 0.45f, 0.3f);
            }
            PS2Spawn.Mesh(go.transform, "Mesh", b.Build(go.name), Vector3.zero, m);
            go.transform.SetPositionAndRotation(pos + Vector3.up * (size.y * 0.5f + 0.02f), Quaternion.Euler(0f, yaw, 0f));

            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = kind == Kind.Crate ? 22f * scale : 7f * scale;
            rb.linearDamping = 0.6f;
            rb.angularDamping = 2.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var p = go.AddComponent<TownProp>();
            p.Id = id; p.Type = kind; p.Size = size; p.body = rb; p.col = bc; p.mat = m; p.tint = tint;
            p.Hp = kind == Kind.Crate ? 30f : 6f;
            return p;
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); if (Holder != null && Holder.Carried == this) Holder.Carried = null; }

        /// <summary>The nearest one lying free within a range.</summary>
        public static TownProp Nearest(Vector3 p, float range)
        {
            TownProp best = null; float bd = range;
            foreach (var o in All)
            {
                if (o == null || o.Broken || o.Holder != null) continue;
                Vector3 d = o.transform.position - p;
                if (Mathf.Abs(d.y) > 1.3f) continue;
                d.y = 0f;
                float m = d.magnitude - o.Size.x * 0.5f;
                if (m < bd) { bd = m; best = o; }
            }
            return best;
        }

        public static TownProp Find(int id) { foreach (var o in All) if (o != null && o.Id == id) return o; return null; }

        // ------------------------------------------------------------------ blows
        /// <summary>A blow lands on it (one per swing).</summary>
        public void Hit(Fighter by, float damage, Vector3 dir)
        {
            if (Broken || Holder != null) return;
            if (by == lastHitter && Time.time - lastHit < 0.3f) return;
            lastHitter = by; lastHit = Time.time;
            Hp -= damage;
            body.WakeUp();
            body.AddForce((dir + Vector3.up * 0.25f).normalized * Mathf.Min(damage, 25f) * 9f, ForceMode.Impulse);
            body.AddTorque(new Vector3(dir.z, 0f, -dir.x) * 6f, ForceMode.Impulse);
            if (Hp <= 0f) Break(by);
            else if (Audio.AudioManager.Instance != null) Audio.AudioManager.Instance.Play("block", transform.position, 0.7f, 0.1f, 0.7f);
        }

        public void Break(Fighter by, bool fromNet = false)
        {
            if (Broken) return;
            Broken = true;
            if (Holder != null) { Holder.Carried = null; Holder = null; }
            var am = Audio.AudioManager.Instance;
            if (am != null)
            {
                if (Type == Kind.Crate) am.Play("land_hard", transform.position, 1f, 0.1f, 1.25f);
                else am.Play("guard_break", transform.position, 0.8f, 0.1f, 1.5f);
            }
            Debris();
            OnBroken?.Invoke(this, by, fromNet);
            Destroy(gameObject);
        }

        void Debris()
        {
            int n = Type == Kind.Crate ? 7 : 6;
            var root = new GameObject("Debris").transform;
            root.position = transform.position;
            var matD = Type == Kind.Crate ? mat : PS2Materials.Get("clay", PS2Textures.Sand, tint);
            for (int i = 0; i < n; i++)
            {
                Vector3 s = Type == Kind.Crate
                    ? new Vector3(UnityEngine.Random.Range(0.25f, 0.5f), 0.04f, UnityEngine.Random.Range(0.08f, 0.14f))
                    : new Vector3(UnityEngine.Random.Range(0.1f, 0.2f), 0.03f, UnityEngine.Random.Range(0.1f, 0.18f));
                Vector3 off = new Vector3(UnityEngine.Random.Range(-0.2f, 0.2f), UnityEngine.Random.Range(-0.15f, 0.2f), UnityEngine.Random.Range(-0.2f, 0.2f));
                var g = PS2Spawn.Box(root, "Bit", s, off, matD, UnityEngine.Random.rotation);
                g.layer = Ragdoll.Layer; // bits never get in anybody's way
                g.AddComponent<BoxCollider>();
                var rb = g.AddComponent<Rigidbody>();
                rb.mass = 0.6f;
                rb.linearVelocity = (off.normalized + Vector3.up * 1.2f) * UnityEngine.Random.Range(1.5f, 3.5f) + body.linearVelocity * 0.4f;
                rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 9f;
            }
            Destroy(root.gameObject, 6f);
        }

        // ------------------------------------------------------------------ carrying
        public bool PickUp(Fighter f)
        {
            if (Broken || Holder != null || f == null || f.Carried != null || !f.IsAlive || f.KnockedOut || f.Ragdolled || f.InActivity) return false;
            if (f.CurrentWeapon != null && !f.Sheathed) f.SetSheathed(true);
            Holder = f;
            f.Carried = this;
            body.isKinematic = true;
            col.enabled = false;
            if (Audio.AudioManager.Instance != null) Audio.AudioManager.Instance.Play("pickup", transform.position, 0.6f, 0.08f, 0.8f);
            Follow();
            return true;
        }

        /// <summary>Put it down in front of you.</summary>
        public void Drop() { Release(Holder != null ? Holder.transform.forward * 0.6f : Vector3.zero, false); }

        /// <summary>Hurl it forwards.</summary>
        public void Throw()
        {
            var h = Holder;
            if (h == null) return;
            Release(h.transform.forward * (Type == Kind.Crate ? 7f : 9.5f) + Vector3.up * 3.2f, true);
            if (Audio.AudioManager.Instance != null) Audio.AudioManager.Instance.Play("swing_heavy", transform.position, 0.9f, 0.05f, 0.85f);
        }

        void Release(Vector3 velocity, bool thrown)
        {
            var h = Holder;
            if (h == null) return;
            Follow();
            Holder = null;
            if (h.Carried == this) h.Carried = null;
            col.enabled = true;
            body.isKinematic = false;
            body.linearVelocity = velocity;
            if (thrown)
            {
                thrownBy = h; thrownAt = Time.time;
                body.angularVelocity = h.transform.right * 4f;
            }
        }

        void Follow()
        {
            if (Holder == null) return;
            Transform t = Holder.transform;
            transform.SetPositionAndRotation(t.position + Vector3.up * 1.02f + t.forward * (0.46f + Size.z * 0.5f), t.rotation);
        }

        /// <summary>Where a hand holds it (world).</summary>
        public Vector3 GripPoint(bool right)
        {
            return transform.position + transform.right * (right ? 1f : -1f) * (Size.x * 0.5f + 0.02f) - transform.up * (Size.y * 0.12f);
        }

        void LateUpdate()
        {
            if (Holder == null) { if (transform.position.y < -30f) Destroy(gameObject); return; }
            var h = Holder;
            if (!h.IsAlive || h.KnockedOut || h.Ragdolled || h.InActivity || h.State == FighterState.Stagger || h.State == FighterState.Stunned)
            {
                Release(Vector3.zero, false);
                return;
            }
            Follow();
        }

        void OnCollisionEnter(Collision c)
        {
            if (Broken || Holder != null) return;
            float v = c.relativeVelocity.magnitude;
            bool flying = thrownBy != null && Time.time - thrownAt < 2.5f;
            if (flying && v > 3f)
            {
                var f = c.collider.GetComponentInParent<Fighter>();
                if (f != null && f != thrownBy && f.IsAlive && f.TeamId != thrownBy.TeamId)
                {
                    Vector3 dir = f.transform.position - transform.position; dir.y = 0f;
                    f.ReceiveHit(thrownBy, Type == Kind.Crate ? 16f : 10f, AttackType.Heavy, dir.sqrMagnitude > 0.001f ? dir.normalized : thrownBy.transform.forward);
                    Break(thrownBy);
                    return;
                }
            }
            if (Type == Kind.Jar && v > 5.5f) { Break(flying ? thrownBy : null); return; }
            if (Type == Kind.Crate && v > 6f)
            {
                Hp -= v * 2.2f;
                if (Hp <= 0f) Break(flying ? thrownBy : null);
                else if (Audio.AudioManager.Instance != null) Audio.AudioManager.Instance.Play("land", transform.position, 0.8f, 0.1f, 0.8f);
            }
        }
    }

    /// <summary>On every fighter: walking into a crate or a jar shoves it along.</summary>
    public class PropPusher : MonoBehaviour
    {
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var rb = hit.rigidbody;
            if (rb == null || rb.isKinematic || hit.moveDirection.y < -0.3f) return;
            var p = rb.GetComponent<TownProp>();
            if (p == null) return;
            Vector3 d = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (d.sqrMagnitude < 0.01f) return;
            Vector3 cv = hit.controller.velocity; cv.y = 0f;
            float sp = Mathf.Clamp(cv.magnitude * (p.Type == TownProp.Kind.Crate ? 0.75f : 1f), 0.8f, 4.5f);
            Vector3 v = d.normalized * sp;
            rb.WakeUp();
            rb.linearVelocity = new Vector3(v.x, rb.linearVelocity.y, v.z);
        }
    }
}
