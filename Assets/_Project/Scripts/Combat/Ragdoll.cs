using System.Collections.Generic;
using Gladiator.Character;
using UnityEngine;
using SK = Gladiator.Character.SomaSkeleton;

namespace Gladiator.Combat
{
    /// <summary>
    /// The body as a rag doll: when a fighter dies or is knocked out, the animation lets go and physics takes the
    /// body - eleven rigid parts (pelvis, chest, head, upper arms, forearms, thighs, shins) held by joints with human
    /// limits, pushed by the blow that felled it. It is built the first time it is needed, in the rest pose (T-pose),
    /// so the joint limits mean the same for everybody.
    ///
    /// While it is active the skeleton is taken out from under the fighter (physics moves it in the world) and the
    /// fighter's root follows the pelvis, so the camera, the shadow, the blood and the network all keep working from
    /// <c>transform.position</c>. The dead freeze where they came to rest after a few seconds; a knocked-out fighter
    /// gets the skeleton back with <see cref="Release"/> and the animator blends up from where the body lay.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class Ragdoll : MonoBehaviour
    {
        /// <summary>Layer of the rag doll parts and of the living fighters' capsules: the two never collide, so nobody trips on the fallen.</summary>
        public const int Layer = 30, FighterLayer = 29;
        /// <summary>For ray casts and overlaps that must not see the fallen (ground checks, camera, arrows...).</summary>
        public static readonly int WorldMask = ~(1 << Layer);
        /// <summary>The scenery only: neither the fallen nor the living.</summary>
        public static readonly int GroundMask = ~((1 << Layer) | (1 << FighterLayer));
        /// <summary>Off = the old canned fall.</summary>
        public static bool Enabled = true;
        /// <summary>How many bodies went limp so far (tests).</summary>
        public static int Activations;
        /// <summary>Tests: track how far knees and elbows bend (degrees; knees bend to +, right elbow to -).</summary>
        public static bool Measure;
        public static float KneeMin, KneeMax, ElbowMin, ElbowMax;

        public bool Active { get; private set; }
        /// <summary>A dead body that came to rest: frozen where it lies.</summary>
        public bool Settled { get; private set; }
        /// <summary>Middle of the fallen body (between pelvis and chest).</summary>
        public Vector3 Center => hips != null && chest != null ? (hips.position + chest.position) * 0.5f : transform.position;
        public Transform Hips => hips;
        public Transform Chest => chest;

        class Part
        {
            public Transform T;
            public Rigidbody Body;
            public readonly List<Collider> Colliders = new List<Collider>();
        }

        readonly List<Part> parts = new List<Part>();
        Fighter fighter;
        HumanoidRig rig, builtFor;
        Transform rigParent, hips, chest;
        Transform shield, shieldBone;
        Vector3 shieldPos;
        Quaternion shieldRot;
        float since;
        static PhysicsMaterial skin;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Setup()
        {
            Physics.IgnoreLayerCollision(Layer, FighterLayer, true);
            Activations = 0;
        }

        /// <summary>The rag doll of a fighter (added on first use). Null if it has no humanoid body.</summary>
        public static Ragdoll For(Fighter f)
        {
            if (f == null) return null;
            var r = f.GetComponent<Ragdoll>();
            if (r == null) { r = f.gameObject.AddComponent<Ragdoll>(); r.fighter = f; }
            return r;
        }

        bool Ensure()
        {
            if (fighter == null) fighter = GetComponent<Fighter>();
            var r = Active && rig != null ? rig : GetComponentInChildren<HumanoidRig>();
            if (r == null || r.Bones == null || r.Metrics == null) return false;
            if (r != builtFor) Build(r);
            return true;
        }

        // ------------------------------------------------------------------ building
        void Build(HumanoidRig r)
        {
            rig = builtFor = r;
            parts.Clear();
            var b = r.Bones;
            var m = r.Metrics;
            var J = m.Joints;
            float S = m.S;
            if (skin == null) skin = new PhysicsMaterial("RagdollSkin") { dynamicFriction = 0.75f, staticFriction = 0.85f, bounciness = 0f };

            // Into the rest pose (every joint with identity rotation) so the limits are measured from the T-pose.
            var rot = new Quaternion[SK.Count];
            var pos = new Vector3[SK.Count];
            for (int i = 0; i < SK.Count; i++)
            {
                rot[i] = b[i].localRotation; pos[i] = b[i].localPosition;
                int p = SK.Parents[i];
                b[i].localRotation = Quaternion.identity;
                b[i].localPosition = J[i] - (p < 0 ? Vector3.zero : J[p]);
            }
            Physics.SyncTransforms();

            Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;
            float tor = m.Neck1Y - m.HipsY;

            var pelvis = Add(b[SK.Hips], 11f);
            Box(pelvis, new Vector3(0f, tor * 0.06f, 0f), new Vector3(m.HipHalf * 1.8f, tor * 0.34f, Mathf.Max(0.17f * S, m.BellyFront * 1.5f)));

            var torso = Add(b[SK.Spine2], 20f);
            float ch = m.Neck1Y - J[SK.Spine2].y;
            Box(torso, new Vector3(0f, ch * 0.5f, 0.01f * S), new Vector3(m.ChestHalf * 1.8f, ch, Mathf.Max(0.17f * S, m.ChestFront * 1.7f)));
            Join(torso, pelvis, X, Z, -20f, 45f, 20f, 20f);

            var head = Add(b[SK.Neck1], 5f);
            Sphere(head, new Vector3(0f, m.ChinY + m.HeadH * 0.5f, 0.01f * S) - J[SK.Neck1], m.HeadH * 0.48f);
            Join(head, torso, X, Z, -35f, 40f, 28f, 30f);

            for (int side = -1; side <= 1; side += 2)
            {
                int sh = side < 0 ? SK.LeftShoulder : SK.RightShoulder;
                // arms lie along +-X in the rest pose: swing forward/back about Y, up/down about Z
                var upper = Add(b[sh + 1], 2.6f);
                Capsule(upper, new Vector3(side * m.UpperArm * 0.5f, 0f, 0f), 0, m.UpperArm + m.BicepR, m.BicepR);
                Join(upper, torso, Y, Z, side > 0 ? -110f : -35f, side > 0 ? 35f : 110f, 85f, 40f);
                var fore = Add(b[sh + 2], 2f);
                float fl = m.ForeArm + m.HandLen * 0.7f;
                Capsule(fore, new Vector3(side * fl * 0.5f, 0f, 0f), 0, fl + m.ForeR, m.ForeR);
                Join(fore, upper, Y, Z, side > 0 ? -140f : 0f, side > 0 ? 0f : 140f, 5f, 5f);

                int leg = side < 0 ? SK.LeftLeg : SK.RightLeg;
                var thigh = Add(b[leg], 8f);
                Vector3 d = J[leg + 1] - J[leg];
                float tr = m.ThighR * 0.9f;
                Capsule(thigh, d * 0.5f, 1, d.magnitude + tr, tr);
                Join(thigh, pelvis, X, Z, -100f, 25f, 35f, 15f);
                var shin = Add(b[leg + 1], 4.5f);
                d = J[leg + 2] - J[leg + 1];
                Capsule(shin, d * 0.5f, 1, d.magnitude + m.CalfR, m.CalfR);
                Box(shin, d + new Vector3(0f, -0.035f * S, 0.06f * S), new Vector3(0.09f, 0.07f, 0.24f) * S);
                Join(shin, thigh, X, Z, 0f, 135f, 4f, 4f);
            }
            Physics.SyncTransforms();

            // back to the pose it had
            for (int i = 0; i < SK.Count; i++) { b[i].localRotation = rot[i]; b[i].localPosition = pos[i]; }

            // a body does not collide with itself: the joint limits keep it human
            var all = new List<Collider>();
            foreach (var p in parts) all.AddRange(p.Colliders);
            for (int i = 0; i < all.Count; i++)
                for (int k = i + 1; k < all.Count; k++) Physics.IgnoreCollision(all[i], all[k], true);

            hips = b[SK.Hips];
            chest = b[SK.Spine2];
            foreach (var p in parts) Sleep(p);
        }

        Part Add(Transform bone, float mass)
        {
            var p = new Part { T = bone };
            bone.gameObject.layer = Layer;
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 1.4f;
            rb.maxAngularVelocity = 14f;
            rb.solverIterations = 14;
            rb.solverVelocityIterations = 4;
            rb.maxDepenetrationVelocity = 3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            p.Body = rb;
            parts.Add(p);
            return p;
        }

        static void Box(Part p, Vector3 center, Vector3 size)
        {
            var c = p.T.gameObject.AddComponent<BoxCollider>();
            c.center = center; c.size = size; c.sharedMaterial = skin;
            p.Colliders.Add(c);
        }

        static void Sphere(Part p, Vector3 center, float radius)
        {
            var c = p.T.gameObject.AddComponent<SphereCollider>();
            c.center = center; c.radius = radius; c.sharedMaterial = skin;
            p.Colliders.Add(c);
        }

        static void Capsule(Part p, Vector3 center, int direction, float height, float radius)
        {
            var c = p.T.gameObject.AddComponent<CapsuleCollider>();
            c.center = center; c.direction = direction; c.height = height; c.radius = radius; c.sharedMaterial = skin;
            p.Colliders.Add(c);
        }

        /// <summary>
        /// A joint at the bone's origin. <paramref name="low"/>..<paramref name="high"/> = how far it turns about
        /// <paramref name="axis"/> as a Transform rotation would read it (the joint counts the other way round);
        /// the two swings are symmetric.
        /// </summary>
        static void Join(Part p, Part parent, Vector3 axis, Vector3 swingAxis, float low, float high, float swing1, float swing2)
        {
            var j = p.T.gameObject.AddComponent<CharacterJoint>();
            j.autoConfigureConnectedAnchor = true;
            j.anchor = Vector3.zero;
            j.axis = axis;
            j.swingAxis = swingAxis;
            j.connectedBody = parent.Body;
            var l = j.lowTwistLimit; l.limit = -high; l.bounciness = 0f; j.lowTwistLimit = l;
            l = j.highTwistLimit; l.limit = -low; l.bounciness = 0f; j.highTwistLimit = l;
            l = j.swing1Limit; l.limit = swing1; l.bounciness = 0f; j.swing1Limit = l;
            l = j.swing2Limit; l.limit = swing2; l.bounciness = 0f; j.swing2Limit = l;
            j.enableCollision = false;
            j.enableProjection = true;
            j.projectionDistance = 0.04f;
            j.projectionAngle = 30f;
            j.enablePreprocessing = false;
        }

        static void Sleep(Part p)
        {
            if (!p.Body.isKinematic) { p.Body.linearVelocity = Vector3.zero; p.Body.angularVelocity = Vector3.zero; }
            p.Body.isKinematic = true;
            p.Body.detectCollisions = false;
            foreach (var c in p.Colliders) c.enabled = false;
        }

        // ------------------------------------------------------------------ going limp
        /// <summary>
        /// The body goes limp where it stands. <paramref name="velocity"/> = what the whole body was doing (m/s);
        /// <paramref name="impulse"/> at <paramref name="point"/> = the blow (N·s; the body weighs about 70 kg).
        /// </summary>
        public bool Activate(Vector3 velocity, Vector3 impulse, Vector3 point)
        {
            if (!Enabled || Active || !Ensure()) return false;
            Activations++;
            Transform rt = rig.transform;
            rigParent = rt.parent;
            rt.SetParent(null, true);
            Part hit = null;
            float best = float.MaxValue;
            foreach (var p in parts)
            {
                foreach (var c in p.Colliders) c.enabled = true;
                p.Body.detectCollisions = true;
                p.Body.isKinematic = false;
                p.Body.linearVelocity = velocity;
                p.Body.angularVelocity = Vector3.zero;
                p.Body.WakeUp();
                float d = (p.Body.worldCenterOfMass - point).sqrMagnitude;
                if (d < best) { best = d; hit = p; }
            }
            if (hit != null && impulse.sqrMagnitude > 0.01f) hit.Body.AddForceAtPosition(impulse, point, ForceMode.Impulse);

            // the shield stays strapped to the forearm (or the back)
            shield = fighter != null && fighter.EquippedShield != null ? fighter.EquippedShield.Pivot : null;
            if (shield != null)
            {
                shieldBone = rig.Bones[fighter.EquippedShield.OnBack ? SK.Chest : SK.LeftForeArm];
                shieldPos = shieldBone.InverseTransformPoint(shield.position);
                shieldRot = Quaternion.Inverse(shieldBone.rotation) * shield.rotation;
            }
            Active = true;
            Settled = false;
            since = Time.time;
            return true;
        }

        /// <summary>
        /// Gives the skeleton back to the animator. The fighter's root is left under the pelvis. Returns the pose the
        /// body had (world rotations per joint and the pelvis position) through the poser, which blends up from it
        /// over <paramref name="blend"/> seconds.
        /// </summary>
        public void Release(float blend)
        {
            if (!Active) return;
            var b = rig.Bones;
            var rots = new Quaternion[SK.Count];
            for (int i = 0; i < SK.Count; i++) rots[i] = b[i].rotation;
            Vector3 hipsAt = hips.position;
            if (!Settled) FollowRoot();
            foreach (var p in parts) Sleep(p);
            Active = false;
            Settled = false;
            Transform rt = rig.transform;
            if (rigParent != null)
            {
                rt.SetParent(rigParent, false);
                rt.localPosition = Vector3.zero;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }
            var poser = GetComponent<HumanoidPoser>();
            if (poser != null && blend > 0f) poser.BlendFrom(rots, hipsAt, blend);
        }

        void FollowRoot()
        {
            Vector3 h = hips.position;
            float y = h.y - 0.12f;
            if (Physics.Raycast(h + Vector3.up * 0.4f, Vector3.down, out RaycastHit g, 4f, GroundMask, QueryTriggerInteraction.Ignore)) y = g.point.y;
            transform.position = new Vector3(h.x, y, h.z);
        }

        void LateUpdate()
        {
            if (!Active || rig == null) return;
            if (!Settled)
            {
                FollowRoot();
                bool dead = fighter != null && fighter.State == FighterState.Dead;
                float t = Time.time - since;
                if (dead && t > 2.5f)
                {
                    bool still = true;
                    foreach (var p in parts) if (!p.Body.IsSleeping() && p.Body.linearVelocity.sqrMagnitude > 0.01f) { still = false; break; }
                    if (still || t > 9f)
                    {
                        // came to rest: the dead need no more physics
                        foreach (var p in parts) Sleep(p);
                        Settled = true;
                    }
                }
                if (float.IsNaN(hips.position.x) || hips.position.y < transform.position.y - 60f) { foreach (var p in parts) Sleep(p); Settled = true; }
            }

            // what is still in the hands goes with the hands
            var b = rig.Bones;
            float S = rig.Metrics.S;
            if (fighter != null)
            {
                if (fighter.CurrentWeapon != null && fighter.WeaponSocket != null)
                {
                    Transform hand = b[SK.RightHand];
                    fighter.WeaponSocket.SetPositionAndRotation(hand.position + hand.rotation * new Vector3(0.07f * S, -0.01f * S, 0f), hand.rotation);
                }
                if (fighter.OffWeapon != null && fighter.LeftSocket != null)
                {
                    Transform hand = b[SK.LeftHand];
                    fighter.LeftSocket.SetPositionAndRotation(Vector3.Lerp(hand.position, b[SK.LeftHandMiddleEnd].position, 0.45f), hand.rotation);
                }
            }
            if (shield != null && shieldBone != null)
                shield.SetPositionAndRotation(shieldBone.TransformPoint(shieldPos), shieldBone.rotation * shieldRot);

            if (Measure && !Settled)
            {
                // knee: the shin against the thigh about the thigh's X; right elbow: the forearm against the upper arm about its Y
                for (int leg = SK.LeftLeg; leg <= SK.RightLeg; leg += SK.RightLeg - SK.LeftLeg)
                {
                    float a = Vector3.SignedAngle(b[leg].rotation * Vector3.down, b[leg + 1].rotation * Vector3.down, b[leg].rotation * Vector3.right);
                    KneeMin = Mathf.Min(KneeMin, a); KneeMax = Mathf.Max(KneeMax, a);
                }
                float e = Vector3.SignedAngle(b[SK.RightArm].rotation * Vector3.right, b[SK.RightForeArm].rotation * Vector3.right, b[SK.RightArm].rotation * Vector3.up);
                ElbowMin = Mathf.Min(ElbowMin, e); ElbowMax = Mathf.Max(ElbowMax, e);
            }
        }

        void OnDisable() { if (Active && rig != null) rig.gameObject.SetActive(false); }
        void OnEnable() { if (Active && rig != null) rig.gameObject.SetActive(true); }
        void OnDestroy() { if (Active && rig != null) Destroy(rig.gameObject); }
    }
}
