using System.Collections.Generic;
using Gladiator.Combat;
using UnityEngine;
using SK = Gladiator.Character.SomaSkeleton;

namespace Gladiator.Character
{
    /// <summary>
    /// Drives the SOMA rig with MotionBricks clips, in layers:
    ///   1. locomotion (idle, walk, run in four directions) blended by the root's real velocity;
    ///   2. combat guard on the upper body (and the stance on the legs when standing) while armed;
    ///   3. crouch (idle / walk) while the fighter crouches;
    ///   4. actions synced to the Fighter state machine: attack clips mapped onto Windup/Active/Recovery
    ///      through their key-pose markers, block, stagger, death, and the movement actions (dodge roll with a
    ///      procedural spin, back-step, jump, fall, landing, vault and climb);
    ///   4. head looking at the opponent.
    /// The weapon then follows the right hand and the shield the left forearm. Training minigames that drive
    /// Fighter.ArmPivot themselves (ExternalArmControl) get the old IK arm. Without clips: procedural fallback.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class HumanoidPoser : MonoBehaviour
    {
        public HumanoidRig Rig;
        public Fighter Fighter;
        public Transform ArmPivot;
        public Transform WeaponSocket;
        /// <summary>Left hand: second weapon or the bow.</summary>
        public Transform LeftSocket;

        Vector3[] restLocal;
        Vector3 lastPos;
        bool hasLast;
        float phase, moveBlend, time;
        Vector3 moveDir = Vector3.forward;
        Transform shield;
        float shieldCheck;

        // ---- combat layers
        MotionClip guardClip, attackLight, attackHeavy, swordLight, swordHeavy, blockClip, staggerClip, deathClip;
        float bladeAlign, leftBladeAlign;
        // ---- arsenal (milestone C5)
        MotionClip guard2h, h2Light, h2Heavy, block2h, spearGuard2, spear2Light, spear2Heavy, spearLight, spearHeavy,
                   dualLeft, dualHeavy, bowGuard, bowShoot;
        MotionClip guardCurrent;
        GameObject nockedArrow;
        MotionClip kickClip, stunnedClip, tauntChest, tauntBeckon, cheerClip;
        MotionClip dodgeClip, dodgeBackClip, jumpClip, fallClip, landClip, crouchClip, crouchWalkClip, vaultClip, climbClip;
        float crouchWeight, crouchTime, crouchWalkTime, rollAngle;
        bool profilesSet;
        /// <summary>Playback speed of the traversal clips (MotionBricks keys are spaced ~0.6 s apart).</summary>
        const float VaultRate = 1.7f, ClimbRate = 1.75f;
        readonly Quaternion[] layer = new Quaternion[SK.Count];
        float guardTime, actionWeight, guardWeight, stateTime;
        FighterState lastState = FighterState.Idle;
        MotionClip lastAction;
        float lastActionT;
        Vector3 socketLocalPos;
        Quaternion socketLocalRot;
        bool socketSaved;
        /// <summary>Name of the action clip playing (for the autotest), or null.</summary>
        public string ActionPlaying { get; private set; }

        /// <summary>When set (creator preview), drives the locomotion blend instead of the measured velocity.</summary>
        public Vector3? PreviewVelocity;
        /// <summary>Which mode is active (for the autotest / HUD).</summary>
        public bool UsingClips { get; private set; }

        // ---- MotionBricks clip state
        MotionClip idle, walkF, runF, runB, runL, runR;
        readonly Quaternion[] retarget = new Quaternion[SK.Count];
        readonly Quaternion[] blend = new Quaternion[SK.Count];
        readonly Quaternion[] sample = new Quaternion[SK.Count];
        readonly List<(MotionClip clip, float weight)> active = new List<(MotionClip, float)>();
        float clipPhase, idleTime, hipsScale = 1f;
        Vector3 smoothVel;

        // ---- head / look layer and turning in place
        /// <summary>Last head look direction (world), for the autotest.</summary>
        public Vector3 LookDirection => lookDir;
        public Fighter LookTarget { get; private set; }
        Vector3 lookDir;
        float lookSearch, lastYaw, yawRate;
        bool hasYaw;
        static readonly int[] NeckChain = { SK.Neck1, SK.Neck2, SK.Head };
        static readonly float[] NeckShare = { 0.3f, 0.45f, 0.85f };

        public void Init(HumanoidRig rig)
        {
            Rig = rig;
            restLocal = new Vector3[SK.Count];
            for (int i = 0; i < SK.Count; i++) restLocal[i] = rig.Bones[i].localPosition;
            hasLast = false;
            InitClips();
        }

        void InitClips()
        {
            UsingClips = false;
            if (!MotionLibrary.Available) return;
            idle = MotionLibrary.Get("idle"); walkF = MotionLibrary.Get("walk_f"); runF = MotionLibrary.Get("run_f");
            runB = MotionLibrary.Get("run_b"); runL = MotionLibrary.Get("run_l"); runR = MotionLibrary.Get("run_r");
            if (idle == null || walkF == null || runF == null) return;
            // Retarget: rotate each of our T-pose bones onto the SOMA bind-pose bone direction. A clip rotation
            // (relative to the bind pose) then applies on top: world = clip * retarget.
            var J = Rig.Metrics.Joints;
            for (int i = 0; i < SK.Count; i++)
            {
                Vector3 rest = RestDir(J, i);
                int c = System.Array.IndexOf(idle.joints, SK.Names[i]);
                Vector3 bind = c >= 0 ? idle.BindDir[c] : rest;
                retarget[i] = Quaternion.FromToRotation(rest, bind);
            }
            hipsScale = J[SK.Hips].y / Mathf.Max(0.1f, idle.bindHipsHeight);
            guardClip = MotionLibrary.Get("guard"); attackLight = MotionLibrary.Get("attack_light");
            attackHeavy = MotionLibrary.Get("attack_heavy");
            swordLight = MotionLibrary.Get("sword_light") ?? attackLight; swordHeavy = MotionLibrary.Get("sword_heavy") ?? attackHeavy; blockClip = MotionLibrary.Get("block");
            staggerClip = MotionLibrary.Get("stagger"); deathClip = MotionLibrary.Get("death");
            guard2h = MotionLibrary.Get("guard_2h"); h2Light = MotionLibrary.Get("h2_light"); h2Heavy = MotionLibrary.Get("h2_heavy");
            block2h = MotionLibrary.Get("block_2h"); spearGuard2 = MotionLibrary.Get("spear_guard_2h");
            spear2Light = MotionLibrary.Get("spear2_light"); spear2Heavy = MotionLibrary.Get("spear2_heavy");
            spearLight = MotionLibrary.Get("spear_light"); spearHeavy = MotionLibrary.Get("spear_heavy");
            dualLeft = MotionLibrary.Get("dual_left"); dualHeavy = MotionLibrary.Get("dual_heavy");
            bowGuard = MotionLibrary.Get("bow_guard"); bowShoot = MotionLibrary.Get("bow_shoot");
            kickClip = MotionLibrary.Get("kick"); stunnedClip = MotionLibrary.Get("stunned");
            tauntChest = MotionLibrary.Get("taunt_chest"); tauntBeckon = MotionLibrary.Get("taunt_beckon"); cheerClip = MotionLibrary.Get("cheer");
            dodgeClip = MotionLibrary.Get("dodge"); dodgeBackClip = MotionLibrary.Get("dodge_back");
            jumpClip = MotionLibrary.Get("jump"); fallClip = MotionLibrary.Get("fall"); landClip = MotionLibrary.Get("land");
            crouchClip = MotionLibrary.Get("crouch"); crouchWalkClip = MotionLibrary.Get("crouch_walk");
            vaultClip = MotionLibrary.Get("vault"); climbClip = MotionLibrary.Get("climb");
            UsingClips = true;
        }

        static Vector3 RestDir(Vector3[] j, int i)
        {
            Vector3 D(int a, int b) => (j[b] - j[a]).normalized;
            switch (i)
            {
                case SK.Hips: return D(SK.Hips, SK.Spine1);
                case SK.Spine1: return D(SK.Spine1, SK.Spine2);
                case SK.Spine2: return D(SK.Spine2, SK.Chest);
                case SK.Chest: return D(SK.Chest, SK.Neck1);
                case SK.Neck1: return D(SK.Neck1, SK.Neck2);
                case SK.Neck2: return D(SK.Neck2, SK.Head);
                case SK.Head: case SK.Jaw: case SK.LeftEye: case SK.RightEye: return Vector3.up;
                case SK.LeftHandThumbEnd: return D(SK.LeftHand, SK.LeftHandThumbEnd);
                case SK.RightHandThumbEnd: return D(SK.RightHand, SK.RightHandThumbEnd);
                case SK.LeftHandMiddleEnd: return D(SK.LeftHand, SK.LeftHandMiddleEnd);
                case SK.RightHandMiddleEnd: return D(SK.RightHand, SK.RightHandMiddleEnd);
                case SK.LeftToeBase: return D(SK.LeftFoot, SK.LeftToeBase);
                case SK.RightToeBase: return D(SK.RightFoot, SK.RightToeBase);
                case SK.LeftHand: return D(SK.LeftHand, SK.LeftHandMiddleEnd);
                case SK.RightHand: return D(SK.RightHand, SK.RightHandMiddleEnd);
                case SK.LeftFoot: return D(SK.LeftFoot, SK.LeftToeBase);
                case SK.RightFoot: return D(SK.RightFoot, SK.RightToeBase);
                default: return D(i, i + 1); // shoulder->arm->forearm->hand, leg->shin->foot
            }
        }

        /// <summary>Blends the clips for a body-space velocity and writes the world rotation of every joint.</summary>
        void PoseFromClips(Vector3 local, float dt, Transform body)
        {
            var b = Rig.Bones;
            smoothVel = Vector3.Lerp(smoothVel, local, 1f - Mathf.Exp(-10f * dt));
            Vector3 v = smoothVel;
            float s = v.magnitude;
            float scale = hipsScale;

            float move = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 0.15f) / 0.9f));
            float cz = s > 1e-3f ? v.z / s : 1f, cx = s > 1e-3f ? v.x / s : 0f;
            float wF = Mathf.Max(0f, cz), wB = runB != null ? Mathf.Max(0f, -cz) : 0f;
            float wR = runR != null ? Mathf.Max(0f, cx) : 0f, wL = runL != null ? Mathf.Max(0f, -cx) : 0f;
            if (runB == null) wF = Mathf.Abs(cz);
            float sum = wF + wB + wR + wL;
            if (sum < 1e-4f) { wF = 1f; sum = 1f; }
            float run = Mathf.Clamp01((s - walkF.Speed * scale) / Mathf.Max(0.1f, (runF.Speed - walkF.Speed) * scale));

            active.Clear();
            void Add(MotionClip c, float w) { if (c != null && w > 1e-3f) active.Add((c, w * move / sum)); }
            Add(walkF, wF * (1f - run));
            Add(runF, wF * run);
            Add(runB, wB);
            Add(runL, wL);
            Add(runR, wR);

            // Shared phase: playback rate from the ground speed (stride scaled to the character's size).
            float rate = 0f, wsum = 0f;
            foreach (var (c, w) in active)
            {
                float clipSpeed = Mathf.Max(0.3f, c.Speed * scale);
                rate += w * Mathf.Clamp(s / clipSpeed, 0.35f, 1.6f) / c.Duration;
                wsum += w;
            }
            if (wsum > 1e-4f) clipPhase = Mathf.Repeat(clipPhase + rate / wsum * dt, 1f);
            idleTime = Mathf.Repeat(idleTime + dt / idle.Duration, 1f);

            float acc = 0f;
            Vector3 hips = Vector3.zero;
            void Mix(MotionClip c, float t, float w)
            {
                c.Sample(t, sample, out Vector3 h);
                float k = acc <= 0f ? 1f : w / (acc + w);
                for (int i = 0; i < SK.Count; i++)
                {
                    Quaternion q = sample[i];
                    if (acc <= 0f) blend[i] = q;
                    else
                    {
                        if (Quaternion.Dot(blend[i], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        blend[i] = Quaternion.Slerp(blend[i], q, k);
                    }
                }
                hips = acc <= 0f ? h : Vector3.Lerp(hips, h, k);
                acc += w;
            }
            for (int i = 0; i < SK.Count; i++) sample[i] = Quaternion.identity;
            Mix(idle, idleTime, Mathf.Max(1e-3f, 1f - move));
            foreach (var (c, w) in active) Mix(c, clipPhase + c.syncPhase, w);

            CombatLayers(ref hips, move, dt);

            Vector3 hipsLocal = restLocal[SK.Hips] + hips * scale;
            Quaternion br = body.rotation;
            if (Mathf.Abs(rollAngle) > 0.01f)
            {
                // Dodge roll: spin the whole posed body forward around a point at belly height.
                Quaternion rl = Quaternion.AngleAxis(rollAngle, Vector3.right);
                Vector3 c = new Vector3(0f, restLocal[SK.Hips].y * 0.5f, 0f);
                hipsLocal = c + rl * (hipsLocal - c);
                br = br * rl;
            }
            b[SK.Hips].localPosition = hipsLocal;
            for (int i = 0; i < SK.Count; i++) b[i].rotation = br * (blend[i] * retarget[i]);
        }

        void LateUpdate()
        {
            if (Rig == null || restLocal == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            time += dt;
            var b = Rig.Bones;
            var M = Rig.Metrics;
            float S = M.S;
            Transform body = Rig.transform.parent != null ? Rig.transform.parent : Rig.transform;

            // ---- locomotion state from the root's real motion
            Vector3 p = transform.position;
            Vector3 vel = hasLast ? (p - lastPos) / dt : Vector3.zero;
            lastPos = p; hasLast = true;
            vel.y = 0f;
            if (vel.sqrMagnitude > 15f * 15f) vel = Vector3.zero; // teleport
            bool dead = Fighter != null && Fighter.State == FighterState.Dead;
            Vector3 local = PreviewVelocity ?? body.InverseTransformDirection(vel);
            local.y = 0f;
            float speed = dead ? 0f : local.magnitude;
            moveBlend = Mathf.MoveTowards(moveBlend, Mathf.Clamp01(speed / 1.2f), dt * 4f);
            if (speed > 0.2f) moveDir = Vector3.Slerp(moveDir, local / speed, 1f - Mathf.Exp(-12f * dt)).normalized;
            float run = Mathf.Clamp01(speed / 5f);
            float cycle = Mathf.Lerp(1.1f, 2.1f, run) * S;
            phase = Mathf.Repeat(phase + Mathf.PI * 2f * speed * dt / cycle, Mathf.PI * 2f);

            // ---- reset to rest
            for (int i = 0; i < SK.Count; i++)
            {
                b[i].localRotation = Quaternion.identity;
                b[i].localPosition = restLocal[i];
            }

            // Turning on the spot: feed small sideways steps into the locomotion blend.
            float yaw = body.eulerAngles.y;
            if (hasYaw) yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastYaw, yaw) / dt, 1f - Mathf.Exp(-10f * dt));
            lastYaw = yaw; hasYaw = true;
            if (!dead && PreviewVelocity == null && local.magnitude < 0.6f && Mathf.Abs(yawRate) > 45f)
                local += new Vector3(Mathf.Sign(yawRate) * Mathf.Clamp01((Mathf.Abs(yawRate) - 45f) / 200f) * 1.4f, 0f, 0f);

            if (UsingClips)
            {
                PoseFromClips(dead ? Vector3.zero : local, dt, body);
                PoseHead(body, dt, dead);
                if (CombatDriven)
                {
                    shield = Fighter != null && Fighter.EquippedShield != null ? Fighter.EquippedShield.Pivot : null;
                    AttachProps(body);
                }
                else
                {
                    RestoreSocket();
                    PoseArms(body, dt, dead, true);
                }
                return;
            }

            // ---- hips and spine
            float bob = 0.012f * S + moveBlend * (0.02f + 0.02f * run) * S * Mathf.Abs(Mathf.Sin(phase));
            b[SK.Hips].localPosition = restLocal[SK.Hips] + Vector3.down * bob;
            float twist = moveBlend * 7f * Mathf.Sin(phase) * Mathf.Sign(Vector3.Dot(moveDir, Vector3.forward) + 0.01f);
            float lean = moveBlend * run * 9f;
            float breath = Mathf.Sin(time * 1.7f) * 1.3f;
            b[SK.Hips].localRotation = Quaternion.Euler(0, -twist * 0.6f, 0);
            b[SK.Spine2].localRotation = Quaternion.Euler(lean * 0.5f, twist * 0.7f, 0);
            b[SK.Chest].localRotation = Quaternion.Euler(lean * 0.5f + breath, twist * 0.5f, 0);
            b[SK.Neck1].localRotation = Quaternion.Euler(-lean * 0.6f - breath * 0.5f, 0, 0);
            b[SK.Head].localRotation = Quaternion.Euler(-lean * 0.3f, 0, 0);

            // ---- legs (IK to procedural foot targets in body space)
            float stride = Mathf.Lerp(0.2f, 0.42f, run) * S * moveBlend;
            for (int side = -1; side <= 1; side += 2)
            {
                int leg = side < 0 ? SK.LeftLeg : SK.RightLeg;
                float ph = phase + (side < 0 ? 0f : Mathf.PI);
                float swing = Mathf.Sin(ph), lift = Mathf.Max(0f, Mathf.Cos(ph));
                Vector3 ankle = M.Joints[leg + 2];
                Vector3 target = ankle + moveDir * (stride * swing) + Vector3.up * (lift * moveBlend * (0.07f + 0.06f * run) * S);
                TwoBone(b[leg], b[leg + 1], b[leg + 2], M.Joints[leg], M.Joints[leg + 1], M.Joints[leg + 2],
                    body.TransformPoint(target), body.TransformDirection(new Vector3(side * 0.1f, 0, 1f)), Vector3.down, Vector3.forward);
                float pitch = -swing * 12f * moveBlend + lift * moveBlend * 10f;
                b[leg + 2].rotation = body.rotation * Quaternion.Euler(pitch, 0, 0);
            }

            PoseArms(body, dt, dead, false);
        }

        static bool IsUpper(int i) => i >= SK.Spine1 && i < SK.LeftLeg;

        bool CombatDriven => (Fighter != null && !Fighter.ExternalArmControl) || (Fighter == null && PreviewAction != null);

        /// <summary>Creator preview of the combat clips: "guard" or an action clip name, played in a loop. Null = off.</summary>
        public string PreviewAction
        {
            get => previewAction;
            set { if (value != previewAction) { previewTime = 0f; actionWeight = 0f; } previewAction = value; }
        }
        string previewAction;
        float previewTime;

        /// <summary>Layers 2 and 3: guard stance while armed, then the action of the current fighter state.</summary>
        void CombatLayers(ref Vector3 hips, float move, float dt)
        {
            ActionPlaying = null;
            if (!CombatDriven) { guardWeight = 0f; actionWeight = 0f; return; }

            bool armed, dead, crouching = false, fullBody = false, airborne = false;
            MotionClip clip = null;
            float t = 0f;
            if (Fighter == null)
            {
                // Creator preview: guard, plus the chosen action looping with a short pause.
                armed = true; dead = PreviewAction == "death";
                previewTime += dt;
                crouching = PreviewAction == "crouch";
                var pc = PreviewAction == "guard" || crouching ? null : MotionLibrary.Get(PreviewAction);
                if (pc != null)
                {
                    float cycle = pc.Duration + 0.8f;
                    float tt = Mathf.Repeat(previewTime, cycle);
                    if (tt < pc.Duration + 0.4f) { clip = pc; t = Mathf.Min(1f, tt / pc.Duration); }
                }
            }
            else
            {
                Fighter.ExternalDeathAnimation = deathClip != null;
                if (!profilesSet) SetTraverseProfiles();
                crouching = Fighter.Crouching;
                FighterState st = Fighter.State;
                if (st != lastState) { stateTime = 0f; lastState = st; }
                stateTime += dt;
                dead = st == FighterState.Dead;
                armed = Fighter.CurrentWeapon != null && !dead;
                switch (st)
                {
                    case FighterState.Windup:
                    case FighterState.Active:
                    case FighterState.Recovery:
                    {
                        bool heavy = Fighter.CurrentAttackType == AttackType.Heavy;
                        clip = AttackClipFor(heavy);
                        if (Fighter.CurrentAttackType == AttackType.Kick && kickClip != null) { clip = kickClip; fullBody = true; }
                        if (clip == null) break;
                        float m0 = clip.Marker(0, 0.33f), m1 = clip.Marker(1, 0.55f), p = Fighter.StatePhase;
                        if (clip == bowShoot && st == FighterState.Windup && Fighter.Charging)
                            t = m0 * Mathf.Clamp01(stateTime / 0.35f); // draw, then hold at full draw while charging
                        else
                            t = st == FighterState.Windup ? m0 * p : st == FighterState.Active ? Mathf.Lerp(m0, m1, p) : Mathf.Lerp(m1, 1f, p);
                        var air = Fighter.AirAttack;
                        if (air != AirAttackKind.None)
                        {
                            fullBody = true;
                            airborne = !Fighter.IsGrounded;
                            // Slam: weapon up while dropping; dive kick: leg out for the whole dive.
                            if (air == AirAttackKind.Slam && st == FighterState.Windup) t = m0 * Mathf.Clamp01(stateTime / 0.15f);
                            if (air == AirAttackKind.DiveKick && st == FighterState.Active) t = Mathf.Lerp(m0, m1, Mathf.Clamp01(stateTime / 0.1f));
                        }
                        break;
                    }
                    case FighterState.Blocking:
                        clip = Fighter.TwoHanded && !Fighter.ActiveStats.IsBow && block2h != null ? block2h : blockClip;
                        if (clip != null)
                        {
                            float m0 = clip.Marker(0, 0.45f);
                            t = stateTime < 0.15f ? m0 * stateTime / 0.15f : Mathf.Min(1f, m0 + (stateTime - 0.15f) / clip.Duration);
                        }
                        break;
                    case FighterState.Stagger:
                        clip = staggerClip;
                        t = Fighter.StatePhase;
                        break;
                    case FighterState.Taunt:
                        clip = Fighter.TauntKind == 0 ? tauntChest : Fighter.TauntKind == 1 ? tauntBeckon : cheerClip;
                        t = Fighter.StatePhase;
                        fullBody = true;
                        break;
                    case FighterState.Stunned:
                        // Knocked back first (stagger), then the dazed sway.
                        fullBody = true;
                        if (stateTime < 0.3f && staggerClip != null) { clip = staggerClip; t = stateTime / 0.3f * 0.45f; }
                        else if (stunnedClip != null) { clip = stunnedClip; t = Mathf.Repeat((stateTime - 0.3f) / stunnedClip.Duration, 1f); }
                        break;
                    case FighterState.Dead:
                        clip = deathClip;
                        t = Mathf.Min(1f, stateTime / (clip != null ? clip.Duration * 0.8f : 1f));
                        break;
                    case FighterState.Dodge:
                        clip = Fighter.DodgeBack ? dodgeBackClip : dodgeClip;
                        t = Fighter.StatePhase * 0.92f;
                        fullBody = true;
                        break;
                    case FighterState.Airborne:
                        airborne = fullBody = true;
                        if (Fighter.Jumped && Fighter.AirTime < 0.55f && jumpClip != null)
                        {
                            clip = jumpClip;
                            float m1 = jumpClip.Marker(1, 0.6f);
                            t = Mathf.Lerp(m1, 1f, Mathf.Clamp01(Fighter.AirTime / 0.3f));
                        }
                        else if (fallClip != null)
                        {
                            clip = fallClip;
                            t = Mathf.Repeat(Fighter.AirTime / fallClip.Duration, 1f);
                        }
                        break;
                    case FighterState.Landing:
                        clip = landClip;
                        fullBody = true;
                        if (clip != null)
                        {
                            float m0 = clip.Marker(0, 0.4f);
                            t = Mathf.Lerp(m0 * 0.75f, Fighter.LastFallHeight > 1.3f ? 1f : m0 + 0.2f, Fighter.StatePhase);
                        }
                        break;
                    case FighterState.Traverse:
                        clip = Fighter.CurrentTraverse == TraverseKind.Vault ? vaultClip : climbClip;
                        t = Fighter.StatePhase;
                        fullBody = true;
                        break;
                }
            }

            // Guard: armed fighters keep the sword ready, and the stance when standing still.
            var gclip = GuardClipFor();
            if (gclip != guardCurrent && guardCurrent != null && guardWeight > 0.5f) guardWeight = 0.5f; // grip change: blend in
            guardCurrent = gclip;
            guardWeight = Mathf.MoveTowards(guardWeight, armed && gclip != null ? 1f : 0f, dt * 4f);
            if (guardWeight > 1e-3f && gclip != null)
            {
                guardTime = Mathf.Repeat(guardTime + dt / gclip.Duration, 1f);
                gclip.Sample(guardTime, layer, out Vector3 gh);
                for (int i = 0; i < SK.Count; i++)
                {
                    if (i == SK.Hips) continue;
                    float w = guardWeight * (IsUpper(i) ? 0.85f : 0.9f * (1f - move));
                    if (i == SK.Head || i == SK.Neck1 || i == SK.Neck2) w *= 0.5f;
                    Mix(i, layer[i], w);
                }
                float hw = guardWeight * (1f - move);
                hips = Vector3.Lerp(hips, new Vector3(hips.x, gh.y, hips.z), hw);
                blend[SK.Hips] = Quaternion.Slerp(blend[SK.Hips], layer[SK.Hips], hw * 0.8f);
            }

            // Crouch: low stance (idle) blended with a crouched walk by the ground speed.
            crouchWeight = Mathf.MoveTowards(crouchWeight, crouching && crouchClip != null ? 1f : 0f, dt * 7f);
            if (crouchWeight > 1e-3f)
            {
                float sp = smoothVel.magnitude;
                float walkW = crouchWalkClip != null ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((sp - 0.1f) / 0.6f)) : 0f;
                crouchTime = Mathf.Repeat(crouchTime + dt / crouchClip.Duration, 1f);
                crouchClip.Sample(crouchTime, layer, out Vector3 ch);
                if (walkW > 1e-3f)
                {
                    float rate = Mathf.Clamp(sp / Mathf.Max(0.3f, crouchWalkClip.Speed * hipsScale), 0.4f, 1.8f);
                    crouchWalkTime = Mathf.Repeat(crouchWalkTime + rate * dt / crouchWalkClip.Duration, 1f);
                    crouchWalkClip.Sample(crouchWalkTime, sample, out Vector3 cwh);
                    for (int i = 0; i < SK.Count; i++)
                    {
                        if (IsUpper(i)) continue; // keep the guard arms of the crouch pose
                        Quaternion q = sample[i];
                        if (Quaternion.Dot(layer[i], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        layer[i] = Quaternion.Slerp(layer[i], q, walkW);
                    }
                    ch = Vector3.Lerp(ch, cwh, walkW);
                }
                for (int i = 0; i < SK.Count; i++) Mix(i, layer[i], crouchWeight);
                hips = Vector3.Lerp(hips, new Vector3(hips.x, ch.y, hips.z), crouchWeight);
            }

            // Dodge roll spin, between the dive and the rise keys.
            rollAngle = 0f;
            if (clip != null && clip == dodgeClip)
            {
                float a = clip.Marker(0, 0.25f), c = clip.Marker(2, 0.75f);
                float k = Mathf.Clamp01((t - a * 0.8f) / Mathf.Max(0.05f, c - a * 0.8f));
                rollAngle = 360f * k * k * (3f - 2f * k);
            }

            // Action
            if (clip != null) { lastAction = clip; lastActionT = t; }
            float target = clip != null ? 1f : 0f;
            actionWeight = Mathf.MoveTowards(actionWeight, target, dt * (target > actionWeight ? 14f : 6f));
            bool swinging = armed && clip != null && (clip == swordLight || clip == swordHeavy || clip == dualHeavy);
            bool spear1h = Fighter != null && Fighter.CurrentWeapon != null && Fighter.ActiveStats.Style == WeaponStyle.Spear && !Fighter.TwoHanded;
            bladeAlign = Mathf.MoveTowards(bladeAlign, spear1h ? 1f : swinging ? 0.6f : 0f, dt * 5f);
            leftBladeAlign = Mathf.MoveTowards(leftBladeAlign, armed && clip != null && (clip == dualLeft || clip == dualHeavy) ? 0.6f : 0f, dt * 5f);
            if (actionWeight <= 1e-3f || lastAction == null) return;
            if (clip == null) { clip = lastAction; t = lastActionT; } // fading out: hold the last frame

            ActionPlaying = clip.name;
            clip.Sample(clip.loop ? t : Mathf.Min(t, 0.999f), layer, out Vector3 ah);
            if (airborne) ah.y = Mathf.Min(ah.y, hips.y); // the jump arc is real; the clip must not add to it
            bool full = dead || fullBody || move < 0.3f;
            for (int i = 0; i < SK.Count; i++)
            {
                float w = actionWeight * (IsUpper(i) || full ? 1f : 0.35f);
                if (!dead && (i == SK.Head || i == SK.Neck2)) w *= 0.6f;
                Mix(i, layer[i], w);
            }
            float hwa = actionWeight * (full ? 1f : 0.3f);
            hips = Vector3.Lerp(hips, ah, hwa);
        }

        /// <summary>Guard stance for what the fighter holds.</summary>
        MotionClip GuardClipFor()
        {
            if (Fighter == null || Fighter.CurrentWeapon == null) return guardClip;
            var w = Fighter.ActiveStats;
            if (w.IsBow) return bowGuard ?? guardClip;
            if (Fighter.TwoHanded) return w.Style == WeaponStyle.Spear ? (spearGuard2 ?? guardClip) : (guard2h ?? guardClip);
            return guardClip;
        }

        /// <summary>Attack clip for the weapon style, the grip and (two weapons) the hand.</summary>
        MotionClip AttackClipFor(bool heavy)
        {
            if (Fighter.CurrentWeapon == null) return heavy ? attackHeavy : attackLight;
            var w = Fighter.ActiveStats;
            switch (w.Style)
            {
                case WeaponStyle.Bow: return bowShoot ?? swordLight;
                case WeaponStyle.Heavy2H: return (heavy ? h2Heavy : h2Light) ?? (heavy ? swordHeavy : swordLight);
                case WeaponStyle.Spear:
                    if (Fighter.TwoHanded) return (heavy ? spear2Heavy : spear2Light) ?? (heavy ? swordHeavy : swordLight);
                    return (heavy ? spearHeavy : spearLight) ?? (heavy ? swordHeavy : swordLight);
                case WeaponStyle.Dagger:
                    if (Fighter.DualWielding && Fighter.AttackHand == 1 && dualLeft != null) return dualLeft;
                    return heavy ? swordHeavy : attackLight; // quick jabs, overhand stab
            }
            if (Fighter.DualWielding)
            {
                if (Fighter.AttackHand == 1 && dualLeft != null) return dualLeft;
                if (Fighter.AttackHand == 2 && dualHeavy != null) return dualHeavy;
            }
            return heavy ? swordHeavy : swordLight;
        }

        /// <summary>Gives the Fighter the vault/climb paths from the clips' contact keys, scaled to this body.</summary>
        void SetTraverseProfiles()
        {
            profilesSet = true;
            TraverseProfile Make(MotionClip c, float rate)
            {
                if (c == null || c.contactY == null || c.markers == null || c.contactY.Length != c.markers.Length) return null;
                var p = new TraverseProfile
                {
                    Duration = c.Duration / rate,
                    Times = (float[])c.markers.Clone(),
                    ContactY = new float[c.contactY.Length],
                    ContactFwd = new float[c.contactY.Length],
                };
                for (int k = 0; k < c.contactY.Length; k++)
                {
                    p.ContactY[k] = c.contactY[k] < 0f ? -1f : c.contactY[k] * hipsScale;
                    p.ContactFwd[k] = c.contactFwd[k] * hipsScale;
                }
                return p;
            }
            Fighter.VaultProfile = Make(vaultClip, VaultRate);
            Fighter.ClimbProfile = Make(climbClip, ClimbRate);
        }

        void Mix(int i, Quaternion q, float w)
        {
            if (w <= 0f) return;
            if (Quaternion.Dot(blend[i], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            blend[i] = Quaternion.Slerp(blend[i], q, w);
        }

        Vector3 GripPoint(int hand, int fingers) => Vector3.Lerp(Rig.Bones[hand].position, Rig.Bones[fingers].position, 0.45f);

        /// <summary>
        /// After the body is posed: weapons into the hands, shield on the forearm or the back, bow and arrow.
        /// Two-handed weapons are aimed from one hand to the other (axes/greatswords: left hand below the right;
        /// spears: left hand in front), so the clips' grips decide where the weapon points.
        /// </summary>
        void AttachProps(Transform body)
        {
            var b = Rig.Bones;
            float S = Rig.Metrics.S;
            WeaponStats main = Fighter != null && Fighter.CurrentWeapon != null ? Fighter.ActiveStats : null;
            bool bow = main != null && main.IsBow;
            Vector3 rGrip = GripPoint(SK.RightHand, SK.RightHandMiddleEnd), lGrip = GripPoint(SK.LeftHand, SK.LeftHandMiddleEnd);
            bool twoHandGrip = main != null && !bow && Fighter.TwoHanded && (rGrip - lGrip).magnitude < 0.8f * S;
            if (WeaponSocket != null && twoHandGrip)
            {
                if (!socketSaved) { socketLocalPos = WeaponSocket.localPosition; socketLocalRot = WeaponSocket.localRotation; socketSaved = true; }
                Vector3 dir = main.Style == WeaponStyle.Spear ? lGrip - rGrip : rGrip - lGrip;
                if (dir.sqrMagnitude > 1e-6f)
                {
                    dir.Normalize();
                    Vector3 up = Vector3.ProjectOnPlane(b[SK.RightHand].rotation * Vector3.up, dir);
                    if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.up, dir);
                    if (up.sqrMagnitude < 1e-4f) up = body.right;
                    WeaponSocket.SetPositionAndRotation(rGrip, Quaternion.LookRotation(dir, up.normalized));
                }
            }
            else if (WeaponSocket != null && !bow)
            {
                if (!socketSaved) { socketLocalPos = WeaponSocket.localPosition; socketLocalRot = WeaponSocket.localRotation; socketSaved = true; }
                Transform hand = b[SK.RightHand];
                // Fist grip: the blade leaves the fist along the thumb side (+Z of the hand at rest).
                // While swinging, the wrist turns the blade towards the forearm line so it carries the arc.
                Quaternion grip = hand.rotation;
                if (bladeAlign > 1e-3f)
                {
                    Vector3 thumb = grip * Vector3.forward;
                    Vector3 fore = (hand.position - b[SK.RightForeArm].position).normalized;
                    grip = Quaternion.FromToRotation(thumb, Vector3.Slerp(thumb, fore, bladeAlign)) * grip;
                }
                WeaponSocket.SetPositionAndRotation(hand.position + hand.rotation * new Vector3(0.07f * S, -0.01f * S, 0f), grip);
            }

            // Left hand: bow, or a second weapon.
            bool drawing = bow && (Fighter.State == FighterState.Windup || (Fighter.State == FighterState.Active && Fighter.StatePhase < 0.5f))
                           && Fighter.CurrentAttackType != AttackType.Kick;
            if (LeftSocket != null && bow)
            {
                Vector3 fwd, up;
                Vector3 fore = (b[SK.LeftHand].position - b[SK.LeftForeArm].position).normalized;
                if (drawing && (lGrip - rGrip).sqrMagnitude > 0.01f)
                {
                    fwd = (lGrip - rGrip).normalized;
                    up = Vector3.ProjectOnPlane(Vector3.up, fwd);
                    up = Quaternion.AngleAxis(-12f, fwd) * (up.sqrMagnitude > 1e-4f ? up.normalized : Vector3.up);
                }
                else
                {
                    Vector3 chestF = b[SK.Chest].rotation * Vector3.forward;
                    fwd = Vector3.ProjectOnPlane(chestF, fore);
                    if (fwd.sqrMagnitude < 1e-4f) fwd = chestF;
                    fwd.Normalize();
                    up = -fore; // limbs along the forearm, bow hanging by the leg
                }
                LeftSocket.SetPositionAndRotation(lGrip, Quaternion.LookRotation(fwd, up));
                if (nockedArrow == null)
                {
                    nockedArrow = Gladiator.Combat.WeaponFactory.ArrowMesh();
                    nockedArrow.name = "NockedArrow";
                }
                nockedArrow.SetActive(drawing);
                if (drawing) nockedArrow.transform.SetPositionAndRotation(lGrip + fwd * 0.06f, Quaternion.LookRotation(fwd, up));
            }
            else
            {
                if (nockedArrow != null) nockedArrow.SetActive(false);
                if (LeftSocket != null && Fighter != null && Fighter.OffWeapon != null)
                {
                    Transform hand = b[SK.LeftHand];
                    Quaternion grip = hand.rotation;
                    if (leftBladeAlign > 1e-3f)
                    {
                        Vector3 thumb = grip * Vector3.forward;
                        Vector3 fore = (hand.position - b[SK.LeftForeArm].position).normalized;
                        grip = Quaternion.FromToRotation(thumb, Vector3.Slerp(thumb, fore, leftBladeAlign)) * grip;
                    }
                    LeftSocket.SetPositionAndRotation(lGrip, grip);
                }
            }

            if (shield != null && Fighter != null && Fighter.EquippedShield != null && Fighter.EquippedShield.OnBack)
            {
                // Slung on the back, face outwards.
                Transform chest = b[SK.Chest];
                Vector3 back = -(chest.rotation * Vector3.forward);
                Vector3 up = chest.rotation * Vector3.up;
                shield.SetPositionAndRotation(chest.position + back * 0.2f * S - up * 0.05f * S, Quaternion.LookRotation(back, up));
            }
            else if (shield != null)
            {
                Vector3 elbow = b[SK.LeftForeArm].position, wrist = b[SK.LeftHand].position;
                Vector3 fore = (wrist - elbow).normalized;
                Vector3 chestFwd = b[SK.Chest].rotation * Vector3.forward;
                Vector3 normal = Vector3.ProjectOnPlane(chestFwd, fore);
                if (normal.sqrMagnitude < 1e-4f) normal = chestFwd;
                normal.Normalize();
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
                if (up.sqrMagnitude < 1e-4f) up = fore;
                shield.SetPositionAndRotation(Vector3.Lerp(elbow, wrist, 0.55f) + normal * 0.07f * S, Quaternion.LookRotation(normal, up.normalized));
            }
        }

        void OnDestroy()
        {
            if (nockedArrow != null) Destroy(nockedArrow);
        }

        void RestoreSocket()
        {
            if (socketSaved && WeaponSocket != null) WeaponSocket.SetLocalPositionAndRotation(socketLocalPos, socketLocalRot);
        }

        /// <summary>
        /// Head layer: the generated clips look down when walking and up when running, so the neck chain is turned
        /// towards a steady gaze: the current opponent (AI target, or the nearest rival for the player), or straight
        /// ahead and slightly down. Limited to +-75 deg of yaw and +-35 deg of pitch from the body.
        /// </summary>
        void PoseHead(Transform body, float dt, bool dead)
        {
            if (dead || ActionPlaying == "death" || rollAngle != 0f || ActionPlaying == "vault" || ActionPlaying == "climb" || ActionPlaying == "land") return;
            var b = Rig.Bones;
            Vector3 headPos = b[SK.Head].position;
            Vector3 fwd = body.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 desired = Quaternion.AngleAxis(6f, Vector3.Cross(Vector3.up, fwd)) * fwd;

            if (Fighter != null && (lookSearch -= dt) <= 0f) { LookTarget = FindLookTarget(); lookSearch = 0.25f; }
            if (LookTarget != null && LookTarget.IsAlive)
            {
                var tr = LookTarget.GetComponentInChildren<HumanoidRig>();
                Vector3 aim = tr != null ? tr.Bones[SK.Head].position : LookTarget.transform.position + Vector3.up * 1.6f;
                Vector3 to = aim - headPos;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.01f)
                {
                    float yawTo = Mathf.Clamp(Vector3.SignedAngle(fwd, flat, Vector3.up), -75f, 75f);
                    float pitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg, -35f, 35f);
                    Vector3 dir = Quaternion.AngleAxis(yawTo, Vector3.up) * fwd;
                    desired = Quaternion.AngleAxis(pitch, Vector3.Cross(Vector3.up, dir)) * dir;
                }
            }
            if (lookDir.sqrMagnitude < 0.5f) lookDir = desired;
            lookDir = Vector3.Slerp(lookDir, desired, 1f - Mathf.Exp(-8f * dt)).normalized;

            for (int k = 0; k < NeckChain.Length; k++)
            {
                Transform t = b[NeckChain[k]];
                Vector3 face = b[SK.Head].rotation * Vector3.forward;
                Quaternion fix = Quaternion.FromToRotation(face, lookDir);
                t.rotation = Quaternion.Slerp(Quaternion.identity, fix, NeckShare[k]) * t.rotation;
            }
        }

        Fighter FindLookTarget()
        {
            var ai = Fighter.GetComponent<EnemyAI>();
            if (ai != null && ai.Target != null && ai.Target.IsAlive) return ai.Target;
            Fighter best = null;
            float bestScore = float.MaxValue;
            Vector3 p = transform.position, fwd = transform.forward;
            foreach (var f in Fighter.All)
            {
                if (f == null || f == Fighter || !f.IsAlive) continue;
                Vector3 to = f.transform.position - p; to.y = 0f;
                float d = to.magnitude;
                bool rival = f.TeamId != Fighter.TeamId;
                if (d > (rival ? 14f : 5f)) continue;
                float score = d - (rival ? 6f : 0f) - Vector3.Dot(fwd, to / Mathf.Max(d, 0.01f)) * 2f;
                if (score < bestScore) { bestScore = score; best = f; }
            }
            return best;
        }

        /// <summary>Arm layer. With clips, the arms keep the clip pose unless they hold something.</summary>
        void PoseArms(Transform body, float dt, bool dead, bool fromClips)
        {
            var b = Rig.Bones;
            var M = Rig.Metrics;
            float S = M.S;
            float run = Mathf.Clamp01(moveBlend * 0.6f);
            float reach = M.UpperArm + M.ForeArm;
            shield = Fighter != null && Fighter.EquippedShield != null ? Fighter.EquippedShield.Pivot : null;
            bool blocking = Fighter != null && Fighter.State == FighterState.Blocking;

            // Left arm: shield grip, guard, or relaxed swing.
            if (!fromClips || (shield != null && !dead) || blocking)
            {
                Vector3 target;
                if (shield != null && !dead)
                {
                    Vector3 chest = b[SK.Chest].position;
                    target = shield.position + (chest - shield.position).normalized * 0.07f * S;
                }
                else
                {
                    float sw = -Mathf.Sin(phase) * moveBlend * (0.1f + 0.12f * run) * S;
                    Vector3 tb = blocking
                        ? new Vector3(-0.08f * S, M.ArmY - 0.1f * S, 0.32f * S)
                        : M.Joints[SK.LeftArm] + new Vector3(0.05f * S, -reach * 0.93f, 0.04f * S) + moveDir * sw + Vector3.up * (run * moveBlend * 0.12f * S);
                    target = body.TransformPoint(tb);
                }
                TwoBone(b[SK.LeftArm], b[SK.LeftForeArm], b[SK.LeftHand], M.Joints[SK.LeftArm], M.Joints[SK.LeftForeArm], M.Joints[SK.LeftHand],
                    target, body.TransformDirection(new Vector3(-0.5f, -0.3f, -1f)), Vector3.left, Vector3.back);
            }

            // Right arm: follows the combat arm pivot (weapon socket) when there is one.
            if (ArmPivot != null && WeaponSocket != null && !dead)
            {
                TwoBone(b[SK.RightArm], b[SK.RightForeArm], b[SK.RightHand], M.Joints[SK.RightArm], M.Joints[SK.RightForeArm], M.Joints[SK.RightHand],
                    WeaponSocket.position, body.TransformDirection(new Vector3(0.6f, -1f, -0.7f)), Vector3.right, Vector3.back);
                b[SK.RightHand].rotation = ArmPivot.rotation * Quaternion.Euler(0, -90f, 0);
            }
            else if (!fromClips)
            {
                float sw = Mathf.Sin(phase) * moveBlend * (0.1f + 0.12f * run) * S;
                Vector3 tb = M.Joints[SK.RightArm] + new Vector3(-0.05f * S, -reach * 0.93f, 0.04f * S) + moveDir * sw;
                TwoBone(b[SK.RightArm], b[SK.RightForeArm], b[SK.RightHand], M.Joints[SK.RightArm], M.Joints[SK.RightForeArm], M.Joints[SK.RightHand],
                    body.TransformPoint(tb), body.TransformDirection(new Vector3(0.5f, -0.3f, -1f)), Vector3.right, Vector3.back);
            }
        }

        /// <summary>Two-bone IK. Rest joint positions give the bone lengths; localAxis is the bone direction and
        /// localBend the side the middle joint bends towards, both in the bone's rest frame.</summary>
        static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 restA, Vector3 restB, Vector3 restC,
                            Vector3 target, Vector3 pole, Vector3 localAxis, Vector3 localBend)
        {
            float l1 = Vector3.Distance(restA, restB), l2 = Vector3.Distance(restB, restC);
            Vector3 s = upper.position;
            Vector3 toT = target - s;
            float d = toT.magnitude;
            Vector3 dir = d > 1e-4f ? toT / d : Vector3.down;
            d = Mathf.Clamp(d, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-4f);
            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir);
            bend.Normalize();
            Vector3 e = s + dir * (l1 * cosA) + bend * (l1 * sinA);
            Vector3 t = s + dir * d;
            Aim(upper, localAxis, e - s, bend, localBend);
            Aim(lower, localAxis, t - e, bend, localBend);
        }

        static void Aim(Transform bone, Vector3 localAxis, Vector3 worldDir, Vector3 worldUp, Vector3 localUp)
        {
            if (worldDir.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.LookRotation(worldDir, worldUp) * Quaternion.Inverse(Quaternion.LookRotation(localAxis, localUp));
        }
    }
}
