using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Combat
{
    public enum AttackType { Light, Heavy, Kick }
    public enum FighterState { Idle, Windup, Active, Recovery, Blocking, Stagger, Dead, Dodge, Airborne, Landing, Traverse, Stunned, Taunt, Activity, KnockedOut }
    public enum TraverseKind { Vault, Climb }
    /// <summary>Attacks started in the air: light = slash in the air, kick = diving kick, heavy = slam to the ground.</summary>
    public enum AirAttackKind { None, Slash, DiveKick, Slam }

    /// <summary>
    /// Where the root goes during a vault/climb: at each normalized time, the hands touch the wall edge
    /// (ContactY = hand height above the feet, ContactFwd = hands ahead of the root) or, with ContactY &lt; 0,
    /// the feet are already on top. Filled by the body animator from the MotionBricks clip.
    /// </summary>
    public class TraverseProfile
    {
        public float Duration;
        public float[] Times, ContactY, ContactFwd;
    }
    public enum HitOutcome { Hit, Blocked, GuardBreak }

    public struct HitResult
    {
        public Fighter Attacker;
        public Fighter Target;
        public AttackType Type;
        public HitOutcome Outcome;
        public float Damage;
        public bool Disarmed;
        public bool Killed;
        public Vector3 Point;
        /// <summary>Body zone struck, and the share of the damage the armour there stopped.</summary>
        public Gladiator.Character.ArmorZone Zone;
        public float ArmorAbsorbed;
        /// <summary>A power strike (light-light-heavy combo or charged heavy): disarms, breaks and dazes a guard.</summary>
        public bool Power;
    }

    /// <summary>
    /// Shared combat core for the player and every AI gladiator.
    /// Controllers (PlayerController / EnemyAI) only feed intents: Move, SetFacing, TryAttack, SetBlock, TryPickup.
    /// Attacks are a small state machine (Windup -> Active -> Recovery). During Active, Physics.OverlapSphere
    /// + an arc test find victims, each hit at most once per swing.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Health))]
    public class Fighter : MonoBehaviour
    {
        public static readonly List<Fighter> All = new List<Fighter>();
        public static event Action<Fighter, AttackType> AnyAttackStarted;
        public static event Action<HitResult> AnyHitResolved;
        public static event Action<Fighter, Weapon> AnyWeaponChanged;
        public static event Action<Fighter> AnyDied;
        /// <summary>Attacker, evader: the swing went over a crouch or through a dodge.</summary>
        public static event Action<Fighter, Fighter> AnyAttackEvaded;
        public static event Action<Fighter, string> AnyMove;
        /// <summary>The strike itself starts (Windup -> Active): the moment for the swing sound.</summary>
        public static event Action<Fighter, AttackType> AnyAttackSwing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            AnyAttackStarted = null; AnyHitResolved = null; AnyWeaponChanged = null; AnyDied = null; AnyAttackEvaded = null; AnyMove = null; AnyAttackSwing = null; AnyArrowFired = null; AnyWeaponThrown = null;
        }

        public const float PickupRadius = 1.6f;

        [Header("Identity")]
        public string DisplayName = "Gladiador";
        [Tooltip("Fighters with the same TeamId never hurt each other. Free-for-all = unique ids.")]
        public int TeamId;

        [Header("Movement")]
        public float MoveSpeed = 4.6f;
        public float TurnSpeed = 900f;
        public float JumpSpeed = 4.3f;
        public float DodgeSpeed = 8f;
        [Header("Kick (F)")]
        public float KickDamage = 6f;
        public float KickReach = 0.95f;
        [Tooltip("The kick steps in towards a rival in front: at least / at most this many metres.")]
        public float KickLungeMin = 0.35f, KickLungeMax = 1.2f;
        float kickLunge = 3.7f;
        [Tooltip("A kick into a raised guard breaks it and leaves the blocker dazed this long (or until hit).")]
        public float KickStunDuration = 2f;

        [Header("Stat hooks (set by StatEffects / Shield)")]
        public float DamageMultiplier = 1f;
        public float AttackSpeedMultiplier = 1f;
        [Range(0f, 1f)] public float BlockReductionLight = 0.9f;
        [Range(0f, 1f)] public float BlockReductionHeavy = 0.6f;
        [Range(0f, 1f)] public float DisarmResistance = 0f;
        public float GuardBreakStagger = 0.6f;

        [Tooltip("When true the arm pose is driven by another script (training minigames).")]
        public bool ExternalArmControl;

        [Header("Rig")]
        public Transform BodyRoot;
        public Transform ArmPivot;
        public Transform WeaponSocket;
        /// <summary>Left-hand socket: second weapon (dual wielding) or the bow.</summary>
        public Transform LeftSocket;
        public GameObject BlobShadow;
        public Renderer[] FlashRenderers;

        public Health Health { get; private set; }
        public CharacterController Controller { get; private set; }
        /// <summary>Who hit this fighter last (kill credit; set before damage so it is valid inside AnyDied).</summary>
        public Fighter LastAttacker { get; private set; }
        public Weapon CurrentWeapon { get; private set; }
        /// <summary>Second one-handed weapon in the left hand (dual wielding).</summary>
        public Weapon OffWeapon { get; private set; }
        public Shield EquippedShield { get; internal set; }
        /// <summary>What the fighter fights with right now: the weapon in the hand, or fists (also while it is put away).</summary>
        public WeaponStats ActiveStats => WeaponInHand ? CurrentWeapon.Stats : WeaponLibrary.Fists;

        // ---- putting the weapon away
        /// <summary>The weapons are put away: on the belt, or across the back (two-handers, spears, the bow).</summary>
        public bool Sheathed { get; private set; }
        /// <summary>The main weapon is in the hand (false while put away, and until the hand has fetched it).</summary>
        public bool WeaponInHand => CurrentWeapon != null && inHand;
        /// <summary>0..1 through the movement of drawing / putting away; -1 when there is none.</summary>
        public float SheathPhase => sheathT >= 0f ? Mathf.Clamp01(sheathT / sheathDur) : -1f;
        public const float SheathSeconds = 0.7f, QuickDrawSeconds = 0.36f;
        bool inHand = true;
        float sheathT = -1f, sheathDur = SheathSeconds;

        /// <summary>R: puts the weapon away, or draws it.</summary>
        public bool ToggleSheath() => Carried == null && SetSheathed(!Sheathed);

        /// <summary>A torch: wanted lit (the player's key, or night for the townsfolk) and actually held up now.</summary>
        public bool TorchOn, TorchShown;
        public void ToggleTorch() { TorchOn = !TorchOn; AnyMove?.Invoke(this, TorchOn ? "torch_on" : "torch_off"); }
        /// <summary>Attacking with the weapon put away draws it first (off for players: they punch instead).</summary>
        public bool AutoDraw = true;
        /// <summary>The crate or jar carried in both arms (attacking throws it).</summary>
        public TownProp Carried;

        /// <summary>
        /// Puts the weapons away or draws them (a short movement: the hand goes to the belt or over the shoulder; the
        /// weapon changes place half way). <paramref name="instant"/>: no movement (spawning).
        /// </summary>
        public bool SetSheathed(bool sheathe, bool instant = false, float seconds = SheathSeconds)
        {
            if (CurrentWeapon == null) { ResetSheath(); return false; }
            if (instant)
            {
                Sheathed = sheathe; inHand = !sheathe; sheathT = -1f;
                RecomputeEquipment();
                return true;
            }
            if (!IsAlive || sheathe == Sheathed || sheathT >= 0f) return false;
            if (state != FighterState.Idle && state != FighterState.Blocking) return false;
            Sheathed = sheathe;
            sheathT = 0f;
            sheathDur = Mathf.Max(0.1f, seconds);
            AnyMove?.Invoke(this, sheathe ? "sheathe" : "draw");
            return true;
        }

        void ResetSheath() { Sheathed = false; inHand = true; sheathT = -1f; }

        void UpdateSheath(float dt)
        {
            if (sheathT < 0f) return;
            if (CurrentWeapon == null) { ResetSheath(); return; }
            float before = sheathT;
            sheathT += dt;
            if (before < sheathDur * 0.5f && sheathT >= sheathDur * 0.5f) { inHand = !Sheathed; RecomputeEquipment(); }
            if (sheathT >= sheathDur) { sheathT = -1f; if (inHand == Sheathed) { inHand = !Sheathed; RecomputeEquipment(); } }
        }
        /// <summary>Stats of the weapon doing the current attack (the left one in a dual combo).</summary>
        public WeaponStats AttackStats => AttackHand == 1 && OffWeapon != null ? OffWeapon.Stats : ActiveStats;
        public bool DualWielding => OffWeapon != null && WeaponInHand;
        /// <summary>Both hands on the main weapon: two-handers, the bow, and spears with the left hand free.</summary>
        public bool TwoHanded { get; private set; }
        public bool ShieldInArm => EquippedShield != null && !EquippedShield.OnBack;
        /// <summary>0 = right hand, 1 = left hand, 2 = both (dual heavy) for the attack in progress.</summary>
        public int AttackHand { get; private set; }
        /// <summary>Bow draw 0..1 while holding the heavy (charged) shot.</summary>
        public float ChargeLevel { get; private set; }
        public bool Charging => state == FighterState.Windup && attackType == AttackType.Heavy && ActiveStats.IsBow;
        /// <summary>Melee heavy held back (button kept down): after 2 s it becomes a power strike.</summary>
        public bool MeleeCharging => state == FighterState.Windup && attackType == AttackType.Heavy && !ActiveStats.IsBow && chargeHeld;
        /// <summary>
        /// The attack in progress disarms on a hit and breaks + stuns a guard (like a kick): the heavy that ends a
        /// light-light-heavy combo, or a heavy held for 2 s and released.
        /// </summary>
        public bool PowerStrike { get; private set; }
        /// <summary>The power strike came from holding the heavy button (extra damage) rather than the combo.</summary>
        public bool PowerCharged { get; private set; }
        public const float PowerChargeTime = 2f;
        int lightChain;
        float lastLightTime = -10f;
        bool throwing, threwThisSwing;
        public static event Action<Fighter, Weapon> AnyWeaponThrown;
        /// <summary>Movement multiplier from what is carried (two-handers, heavy shields).</summary>
        public float EquipMoveMul { get; private set; } = 1f;

        // ---- armour (milestone C6)
        public Gladiator.Character.ArmorSet Armor { get; private set; } = Gladiator.Character.ArmorSet.Empty;
        /// <summary>0..1: armour weight relative to a full heavy set.</summary>
        public float ArmorLoad => Armor != null ? Armor.Load : 0f;
        /// <summary>Walking/running speed lost to the armour: up to -35% with a full heavy set.</summary>
        public float ArmorMoveMul => 1f - 0.35f * ArmorLoad;

        public void SetArmor(Gladiator.Character.ArmorSet set)
        {
            Armor = set ?? Gladiator.Character.ArmorSet.Empty;
        }
        public static event Action<Fighter, Arrow> AnyArrowFired;
        public FighterState State => state;
        /// <summary>0..1 progress through the current timed state (attack phases, stagger).</summary>
        public float StatePhase => Phase;
        /// <summary>Light or heavy, for the attack in progress.</summary>
        public AttackType CurrentAttackType => attackType;
        /// <summary>Set by the body animator when it plays a death clip (skips the procedural fall).</summary>
        [System.NonSerialized] public bool ExternalDeathAnimation;
        public AttackType CurrentAttack => attackType;
        public bool IsAlive => Health != null && !Health.IsDead;
        /// <summary>The attack in progress was started in the air (None for ground attacks).</summary>
        public AirAttackKind AirAttack { get; private set; }
        [Header("Air attacks")]
        public float DiveKickSpeed = 11f, DiveKickDrop = 3.5f, DiveKickDamageMul = 1.5f;
        public float SlamSpeed = 16f, SlamDamageMul = 1.15f;
        float airAttackTime;
        int airSlashes;
        bool InAir => !IsGrounded && ungrounded > 0.1f;

        public bool IsAttacking => state == FighterState.Windup || state == FighterState.Active || state == FighterState.Recovery;
        public float Radius => Controller != null ? Controller.radius : 0.4f;

        // ---- movement actions
        public bool Crouching { get; private set; }
        public bool IsStunned => state == FighterState.Stunned;
        /// <summary>0 chest beating, 1 "come here", 2 cheer (celebration).</summary>
        public int TauntKind { get; private set; }
        /// <summary>Training partners in the ludus: hits only make them flinch (no damage, no disarm) and theirs never land.</summary>
        [System.NonSerialized] public bool Harmless;
        /// <summary>Hits never take the health below this (0 = off). The town: guards knock you out, they do not kill.</summary>
        [System.NonSerialized] public float MinHealth;
        StunStars stunStars;
        /// <summary>Dodge i-frames: the first 70% of the roll / back-step.</summary>
        public bool Invulnerable => (state == FighterState.Dodge && Phase < 0.7f) || state == FighterState.KnockedOut;
        /// <summary>Out cold on the ground (the town watch beat you): a rag doll until <see cref="WakeUp"/>.</summary>
        public bool KnockedOut => state == FighterState.KnockedOut;
        /// <summary>The body is a rag doll right now (dead, or knocked out).</summary>
        public bool Ragdolled => rag != null && rag.Active;
        public Ragdoll Rag => rag;
        Ragdoll rag;
        Vector3 lastHitPoint;
        public bool DodgeBack { get; private set; }
        /// <summary>Seconds in the air; Jumped = took off on purpose (vs walking off a ledge).</summary>
        public float AirTime { get; private set; }
        public bool Jumped { get; private set; }
        public float LastFallHeight { get; private set; }
        public TraverseKind CurrentTraverse { get; private set; }
        [System.NonSerialized] public TraverseProfile VaultProfile, ClimbProfile;
        /// <summary>Grounded; a disabled controller (scripted minigames) counts as grounded.</summary>
        public bool IsGrounded => Controller != null && (!Controller.enabled || Controller.isGrounded);

        struct Timing { public float Windup, Active, Recovery; }

        FighterState state = FighterState.Idle;
        float stateTimer, stateDuration = 1f;
        AttackType attackType;
        Timing timing;
        bool blockHeld, crouchHeld, chargeHeld;
        float chargeTime, comboExpire;
        int comboIndex;
        float dodgeCooldown, ungrounded, apexY, standHeight = -1f, deathFall;
        Vector3 dodgeDir;
        readonly List<Vector3> trPoints = new List<Vector3>();
        readonly List<float> trTimes = new List<float>();
        Vector3 trFwd;
        AttackType? bufferedAttack;
        float bufferExpire;
        Vector3 moveInput, desiredFacing, knockback, lastHitDir;
        float verticalVelocity;
        readonly HashSet<Fighter> hitThisSwing = new HashSet<Fighter>();
        Quaternion armStartRot;
        float flashAmount;
        MaterialPropertyBlock mpb;
        float deathT;
        Vector3 deathAxis;
        Quaternion bodyStartRot;

        static readonly int FlashId = Shader.PropertyToID("_FlashColor");

        // Arm poses (pitch, yaw, roll) of the right-arm pivot. Weapon points along the pivot's +Z.
        static readonly Vector3 PoseIdle = new Vector3(-30f, 22f, 0f);
        static readonly Vector3 PoseLightBack = new Vector3(-20f, 100f, 0f);
        static readonly Vector3 PoseLightEnd = new Vector3(-5f, -85f, 0f);
        static readonly Vector3 PoseHeavyBack = new Vector3(-140f, 10f, 0f);
        static readonly Vector3 PoseHeavyEnd = new Vector3(50f, -5f, 0f);
        static readonly Vector3 PoseBlock = new Vector3(-12f, -72f, -25f);
        static readonly Vector3 PoseStagger = new Vector3(-75f, 70f, 0f);

        void Awake()
        {
            Health = GetComponent<Health>();
            Controller = GetComponent<CharacterController>();
            Health.Died += Die;
            mpb = new MaterialPropertyBlock();
            gameObject.layer = Ragdoll.FighterLayer; // the living walk through the fallen
        }

        void OnEnable() { if (!spectator && !All.Contains(this)) All.Add(this); }

        bool spectator;
        /// <summary>
        /// Watching from the stands: not in <see cref="All"/>, so AI, aim assist, the camera, lock-on and throws ignore it.
        /// </summary>
        public bool Spectator
        {
            get => spectator;
            set
            {
                spectator = value;
                if (value) All.Remove(this);
                else if (isActiveAndEnabled && !All.Contains(this)) All.Add(this);
            }
        }
        void OnDisable() { All.Remove(this); }

        // ------------------------------------------------------------------ intents
        public void Move(Vector3 worldDir)
        {
            worldDir.y = 0f;
            moveInput = Vector3.ClampMagnitude(worldDir, 1f);
        }

        public void SetFacing(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude > 0.0001f) desiredFacing = worldDir.normalized;
        }

        public void SetBlock(bool held) { blockHeld = held; }
        public void SetCrouch(bool held) { crouchHeld = held; }
        /// <summary>Bow: keep drawing the charged shot while held; release fires.</summary>
        public void SetCharge(bool held) { chargeHeld = held; }

        bool FreeToMove => state == FighterState.Idle || state == FighterState.Blocking || state == FighterState.Recovery;

        /// <summary>Activities (held until the fighter gets up): 0 lie down, 1 sit on the ground, 2 eat, 3 cook,
        /// 4 sit on a chair, 5 pray (kneeling).</summary>
        public const int ActivityKinds = 6;
        public const int ActLie = 0, ActSitGround = 1, ActEat = 2, ActCook = 3, ActChair = 4, ActPray = 5;
        public static readonly string[] ActivityNames = { "ACOSTARSE", "SENTARSE", "COMER", "COCINAR", "SENTARSE", "REZAR" };
        /// <summary>Length of each activity's "in" clip (sitting down...); getting up plays it backwards, a bit faster.</summary>
        static readonly float[] ActivityInTimes = { 2.63f, 1.67f, 1.67f, 0.97f, 1.67f, 1.67f };

        // On a piece of furniture (a bed, a chair, the altar): the controller is off and the body is placed exactly
        // on it; getting up it goes back to the floor next to it.
        Vector3 furnFrom, furnStand;
        Pose furnAt;
        float furnTime;
        /// <summary>In an activity on a piece of furniture (no mat on the floor, the body placed on it).</summary>
        public bool OnFurniture { get; private set; }
        /// <summary>Where the fighter goes when he gets up from the furniture.</summary>
        public Vector3 FurnitureStand => furnStand;
        /// <summary>Where the body is on the furniture (online: the others put their copy of this fighter there too).</summary>
        public Pose FurnitureAt => furnAt;
        public int ActivityKind { get; private set; }
        /// <summary>Getting up: the "in" clip plays backwards (StatePhase 0..1).</summary>
        public bool ActivityLeaving { get; private set; }
        public float ActivityInTime => ActivityInTimes[ActivityKind];
        public bool InActivity => state == FighterState.Activity;

        /// <summary>Walk instead of run (toggle).</summary>
        [System.NonSerialized] public bool WalkMode;
        public const float WalkMul = 0.36f;

        /// <summary>Lie down, sit, eat or cook. Stays until <see cref="StopActivity"/> (or a hit).</summary>
        /// <summary>The activity started already in its loop (woke up lying in a bed): no "in" clip, no props.</summary>
        public bool ActivitySkipIn { get; private set; }

        /// <summary>An activity on a piece of furniture: <paramref name="at"/> is where the body goes (lying: the hips;
        /// on a chair: the seat, at floor height), <paramref name="standAt"/> where he stands when he gets up.</summary>
        public bool TryActivityAt(int kind, Pose at, Vector3 standAt, bool skipIn = false)
        {
            if (!IsAlive || state != FighterState.Idle || !IsGrounded) return false;
            OnFurniture = true;
            furnFrom = transform.position;
            furnAt = at;
            furnStand = standAt;
            furnTime = 0f;
            if (!TryActivity(kind, skipIn)) { OnFurniture = false; return false; }
            if (Controller != null) Controller.enabled = false;
            verticalVelocity = 0f;
            Vector3 fwd = at.rotation * Vector3.forward; fwd.y = 0f;
            SetFacing(fwd);
            transform.rotation = Quaternion.LookRotation(fwd);
            if (skipIn) transform.position = at.position;
            return true;
        }

        void UpdateFurniture(float dt)
        {
            furnTime += dt;
            Vector3 p;
            if (ActivityLeaving)
            {
                // stand up on it, then step down beside it
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, StatePhase));
                p = Vector3.Lerp(furnAt.position, furnStand, k);
            }
            else
            {
                float k = ActivitySkipIn ? 1f : Mathf.SmoothStep(0f, 1f, furnTime / Mathf.Max(0.05f, ActivityInTime * 0.3f));
                p = Vector3.Lerp(furnFrom, furnAt.position, k);
            }
            transform.position = p;
            Vector3 fwd = furnAt.rotation * Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f) { transform.rotation = Quaternion.LookRotation(fwd); SetFacing(fwd); }
        }

        void LeaveFurniture()
        {
            if (!OnFurniture) return;
            OnFurniture = false;
            transform.position = furnStand;
            if (Controller != null && IsAlive) Controller.enabled = true;
        }

        public bool TryActivity(int kind, bool skipIn = false)
        {
            if (!IsAlive || state != FighterState.Idle || !IsGrounded) return false;
            ActivityKind = Mathf.Clamp(kind, 0, ActivityKinds - 1);
            ActivitySkipIn = skipIn;
            ActivityLeaving = false;
            bufferedAttack = null;
            SetState(FighterState.Activity, 1e6f);
            ActivityProps.For(this);
            AnyMove?.Invoke(this, "activity");
            return true;
        }

        /// <summary>Get up from the current activity (plays the "in" clip backwards).</summary>
        public void StopActivity()
        {
            if (state != FighterState.Activity || ActivityLeaving) return;
            ActivityLeaving = true;
            SetState(FighterState.Activity, ActivityInTime * 0.7f);
            AnyMove?.Invoke(this, "activity_end");
        }

        /// <summary>Taunt kinds: 0 beat the chest, 1 beckon, 2 raise the arms (cheer), 3 wave, 4 bow, 5 "no".</summary>
        public const int TauntKinds = 6;
        static readonly float[] TauntTimes = { 2.2f, 2.2f, 2.2f, 3.0f, 3.0f, 2.6f };

        /// <summary>Taunt, celebrate or gesture (see <see cref="TauntKinds"/>). Vulnerable meanwhile; any hit interrupts it.</summary>
        public bool TryTaunt(int kind)
        {
            if (!IsAlive || state != FighterState.Idle || !IsGrounded) return false;
            TauntKind = Mathf.Clamp(kind, 0, TauntKinds - 1);
            bufferedAttack = null;
            SetState(FighterState.Taunt, TauntTimes[TauntKind]);
            AnyMove?.Invoke(this, TauntKind == 2 ? "cheer" : TauntKind >= 3 ? "gesture" : "taunt");
            return true;
        }

        /// <summary>Roll towards worldDir, or back-step when there is no direction. Short i-frames.</summary>
        public bool TryDodge(Vector3 worldDir)
        {
            if (!IsAlive || !FreeToMove || dodgeCooldown > 0f || !IsGrounded) return false;
            worldDir.y = 0f;
            DodgeBack = worldDir.sqrMagnitude < 0.04f;
            dodgeDir = DodgeBack ? -transform.forward : worldDir.normalized;
            if (!DodgeBack) transform.rotation = Quaternion.LookRotation(dodgeDir);
            bufferedAttack = null;
            SetState(FighterState.Dodge, (DodgeBack ? 0.42f : 0.6f) * (1f + 0.25f * ArmorLoad));
            AnyMove?.Invoke(this, DodgeBack ? "dodge_back" : "dodge");
            return true;
        }

        /// <summary>Jump; or, with a wall ahead in worldDir (or the facing), vault onto it / climb it.</summary>
        public bool TryJump(Vector3 worldDir)
        {
            if (!IsAlive || (state != FighterState.Idle && state != FighterState.Blocking) || !IsGrounded) return false;
            worldDir.y = 0f;
            if (TryTraverse(worldDir.sqrMagnitude > 0.04f ? worldDir : transform.forward)) return true;
            verticalVelocity = JumpSpeed * (1f - 0.2f * ArmorLoad);
            Jumped = true;
            EnterAir();
            AnyMove?.Invoke(this, "jump");
            return true;
        }

        void EnterAir()
        {
            AirTime = 0f;
            apexY = transform.position.y;
            SetState(FighterState.Airborne, 0f);
        }

        /// <summary>Looks for a wall in front: 0.45-1.35 m high = vault onto it, up to 2.9 m = climb.</summary>
        public bool TryTraverse(Vector3 dir)
        {
            if (!IsAlive || !FreeToMove || !IsGrounded) return false;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();
            Vector3 feet = transform.position;
            if (!Physics.Raycast(feet + Vector3.up * 0.3f, dir, out RaycastHit wall, Radius + 0.9f, Ragdoll.WorldMask, QueryTriggerInteraction.Ignore)) return false;
            if (wall.collider.GetComponentInParent<Fighter>() != null) return false;
            Vector3 n = -wall.normal; n.y = 0f;
            if (n.sqrMagnitude < 0.25f || Vector3.Angle(n, dir) > 50f) return false;
            n.Normalize();
            Vector3 probe = wall.point + n * 0.3f + Vector3.up * 3.3f;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit top, 3.3f, Ragdoll.WorldMask, QueryTriggerInteraction.Ignore)) return false;
            if (top.normal.y < 0.7f || top.collider.GetComponentInParent<Fighter>() != null) return false;
            float h = top.point.y - feet.y;
            if (h < 0.45f || h > 2.9f) return false;
            // Room to stand on top.
            float ch = standHeight > 0f ? standHeight : Controller.height;
            Vector3 onTop = new Vector3(wall.point.x, top.point.y, wall.point.z) + n * (Radius + 0.25f);
            if (Physics.CheckCapsule(onTop + Vector3.up * (Radius + 0.05f), onTop + Vector3.up * (ch - Radius), Radius * 0.9f, Ragdoll.WorldMask, QueryTriggerInteraction.Ignore))
                return false;

            CurrentTraverse = h <= 1.35f ? TraverseKind.Vault : TraverseKind.Climb;
            TraverseProfile pr = CurrentTraverse == TraverseKind.Vault ? VaultProfile : ClimbProfile;
            if (pr == null) pr = CurrentTraverse == TraverseKind.Vault ? DefaultVault : DefaultClimb;
            Vector3 edge = new Vector3(wall.point.x, top.point.y, wall.point.z);
            trFwd = n;
            trPoints.Clear(); trTimes.Clear();
            trPoints.Add(feet); trTimes.Add(0f);
            for (int k = 0; k < pr.Times.Length; k++)
            {
                Vector3 p;
                if (pr.ContactY[k] < 0f) p = edge + n * (Radius + 0.05f);
                else
                {
                    p = edge - n * Mathf.Max(0.15f, pr.ContactFwd[k]);
                    p.y = Mathf.Max(feet.y, top.point.y - pr.ContactY[k]);
                }
                trPoints.Add(p); trTimes.Add(pr.Times[k]);
            }
            trPoints.Add(edge + n * (Radius + 0.3f)); trTimes.Add(1f);
            Crouching = false;
            ApplyCrouchShape();
            Controller.enabled = false;
            transform.rotation = Quaternion.LookRotation(n);
            bufferedAttack = null;
            SetState(FighterState.Traverse, pr.Duration * (1f + 0.45f * ArmorLoad));
            AnyMove?.Invoke(this, CurrentTraverse == TraverseKind.Vault ? "vault" : "climb");
            return true;
        }

        static readonly TraverseProfile DefaultVault = new TraverseProfile
            { Duration = 1.0f, Times = new[] { 0.3f, 0.55f, 0.8f }, ContactY = new[] { 0.9f, 0.7f, -1f }, ContactFwd = new[] { 0.6f, 0.35f, 0f } };
        static readonly TraverseProfile DefaultClimb = new TraverseProfile
            { Duration = 1.8f, Times = new[] { 0.15f, 0.3f, 0.5f, 0.7f, 0.85f }, ContactY = new[] { 1.9f, 2.0f, 1.5f, 0.7f, -1f }, ContactFwd = new[] { 0.2f, 0.2f, 0.45f, 0.35f, 0f } };

        void UpdateTraverse()
        {
            float t = Phase;
            int i = 0;
            while (i < trTimes.Count - 2 && t > trTimes[i + 1]) i++;
            float a = trTimes[i], b = trTimes[i + 1];
            float k = b > a ? Mathf.Clamp01((t - a) / (b - a)) : 1f;
            k = k * k * (3f - 2f * k);
            transform.SetPositionAndRotation(Vector3.Lerp(trPoints[i], trPoints[i + 1], k), Quaternion.LookRotation(trFwd));
            if (stateTimer <= 0f)
            {
                transform.position = trPoints[trPoints.Count - 1];
                SetState(FighterState.Idle, 0f);
            }
        }

        void ApplyCrouchShape()
        {
            if (Controller == null) return;
            if (standHeight < 0f) standHeight = Controller.height;
            float h = Crouching ? standHeight * 0.62f : standHeight;
            if (Mathf.Approximately(Controller.height, h)) return;
            Controller.height = h;
            Controller.center = new Vector3(0f, h * 0.5f, 0f);
        }

        public bool TryAttack(AttackType type)
        {
            if (!IsAlive) return false;
            if (Carried != null) { Carried.Throw(); return false; } // arms full: the blow hurls what you carry
            // the weapon is put away: the blow draws it first (quickly) and then lands. Not the player: with the weapon
            // put away he fights with his fists, and draws only when he says so (R)
            if (AutoDraw && type != AttackType.Kick && CurrentWeapon != null && (state == FighterState.Idle || state == FighterState.Blocking) && !InAir)
            {
                if (Sheathed && sheathT < 0f) SetSheathed(false, false, QuickDrawSeconds);
                if (sheathT >= 0f)
                {
                    if (Sheathed) return false; // still putting it away
                    bufferedAttack = type;
                    bufferExpire = Time.time + (sheathDur - sheathT) + 0.3f;
                    return true;
                }
            }
            if (state == FighterState.Airborne || ((state == FighterState.Idle || state == FighterState.Blocking) && InAir)) return TryAirAttack(type);
            if (state == FighterState.Idle || state == FighterState.Blocking)
            {
                StartAttack(type);
                return true;
            }
            // Input buffer: pressing slightly early chains into the next swing.
            if (state == FighterState.Recovery || state == FighterState.Active)
            {
                bufferedAttack = type;
                bufferExpire = Time.time + 0.3f;
            }
            return false;
        }

        /// <summary>
        /// In the air: light = a slash in the air (a short hang), kick (F) = a diving kick down and forward that hits on
        /// the way (breaks guards like a kick), heavy = drop straight to the ground and land a normal heavy blow.
        /// </summary>
        bool TryAirAttack(AttackType type)
        {
            if (type == AttackType.Heavy && ActiveStats.IsBow) return false;
            bufferedAttack = null;
            StartAttack(type, type == AttackType.Light ? AirAttackKind.Slash : type == AttackType.Kick ? AirAttackKind.DiveKick : AirAttackKind.Slam);
            return true;
        }

        /// <summary>
        /// Picks up a weapon from the ground. A one-hander goes to the free left hand when the right one already holds a
        /// one-hander and there is no shield (dual wielding); otherwise it replaces the main weapon.
        /// </summary>
        public bool TryPickup(Weapon w)
        {
            if (w == null || !w.CanBePickedUp || !IsAlive) return false;
            if (state != FighterState.Idle && state != FighterState.Blocking) return false;
            Vector3 d = w.transform.position - transform.position;
            d.y = 0f;
            if (d.magnitude > PickupRadius + 0.3f) return false;
            if (CurrentWeapon != null && OffWeapon == null && EquippedShield == null &&
                CurrentWeapon.Stats.CanDualWield && w.Stats.CanDualWield)
            {
                EquipOff(w);
                return true;
            }
            if (CurrentWeapon != null) DropWeapon(transform.forward * 1.5f + Vector3.up * 2.5f, false);
            Equip(w);
            return true;
        }

        /// <summary>Main (right) hand. A two-hander or a bow makes the left hand let go of a second weapon.</summary>
        public void Equip(Weapon w)
        {
            if (w == null) return;
            if (CurrentWeapon != null && CurrentWeapon != w) DropWeapon(Vector3.up * 2f, false);
            if (OffWeapon != null && (w.Stats.Grip == Grip.TwoHand || w.Stats.IsBow)) DropOff(Vector3.up * 2f + transform.right);
            CurrentWeapon = w;
            ResetSheath(); // a weapon just taken is in the hand
            // The bow is held in the left hand (the right one draws the string).
            Transform socket = w.Stats.IsBow && LeftSocket != null ? LeftSocket : WeaponSocket;
            w.AttachTo(this, socket != null ? socket : transform);
            RecomputeEquipment();
            AnyWeaponChanged?.Invoke(this, w);
        }

        /// <summary>Left hand: a second one-handed weapon (needs a one-hander in the right hand and no shield).</summary>
        public bool EquipOff(Weapon w)
        {
            if (w == null || CurrentWeapon == null || !w.Stats.CanDualWield || !CurrentWeapon.Stats.CanDualWield || EquippedShield != null) return false;
            if (OffWeapon != null && OffWeapon != w) DropOff(Vector3.up * 2f);
            OffWeapon = w;
            w.AttachTo(this, LeftSocket != null ? LeftSocket : (WeaponSocket != null ? WeaponSocket : transform));
            RecomputeEquipment();
            AnyWeaponChanged?.Invoke(this, w);
            return true;
        }

        /// <summary>G tap: let go of the main weapon at your feet (a second weapon moves to the right hand).</summary>
        public bool DropAtFeet()
        {
            if (!IsAlive || CurrentWeapon == null || !(FreeToMove || state == FighterState.Airborne)) return false;
            DropWeapon(transform.forward * 0.8f + Vector3.up * 1.5f);
            AnyMove?.Invoke(this, "drop");
            return true;
        }

        /// <summary>G held: throw the main weapon forward (a short swing, then it flies, spinning, and hurts whoever it hits).</summary>
        public bool TryThrow()
        {
            if (!IsAlive || !WeaponInHand || sheathT >= 0f || (state != FighterState.Idle && state != FighterState.Blocking)) return false;
            throwing = true;
            StartAttack(AttackType.Light);
            return true;
        }

        void ReleaseThrow()
        {
            var w = CurrentWeapon;
            if (w == null) return;
            Vector3 dir = transform.forward;
            // Aim at a rival roughly ahead.
            Fighter best = null;
            float bestA = 12f;
            foreach (var f in All)
            {
                if (f == this || !f.IsAlive || f.TeamId == TeamId) continue;
                Vector3 to = f.transform.position - transform.position;
                if (to.magnitude > 18f) continue;
                float a = Vector3.Angle(dir, new Vector3(to.x, 0f, to.z));
                if (a < bestA) { bestA = a; best = f; }
            }
            Vector3 from = transform.position + Vector3.up * (Controller != null ? Controller.height * 0.75f : 1.35f) + dir * 0.6f;
            Vector3 v = dir * 17f + Vector3.up * 2.2f;
            if (best != null)
            {
                Vector3 to = best.transform.position + Vector3.up * 1.1f - from;
                float t = new Vector3(to.x, 0f, to.z).magnitude / 17f;
                v = new Vector3(to.x, 0f, to.z).normalized * 17f + Vector3.up * (to.y / Mathf.Max(0.05f, t) + 0.5f * 9.81f * t);
            }
            float dmg = w.Stats.LightDamage * 1.3f * DamageMultiplier;
            DropWeapon(Vector3.zero);
            w.transform.position = from;
            w.Throw(this, v, dmg);
            AnyWeaponThrown?.Invoke(this, w);
            AnyMove?.Invoke(this, "throw");
        }

        /// <summary>Drops the main weapon. A second weapon then moves to the right hand (unless the caller re-equips).</summary>
        public void DropWeapon(Vector3 velocity) => DropWeapon(velocity, true);

        void DropWeapon(Vector3 velocity, bool promoteOff)
        {
            var w = CurrentWeapon;
            if (w == null) return;
            CurrentWeapon = null;
            w.Drop(velocity);
            if (promoteOff && OffWeapon != null)
            {
                var o = OffWeapon;
                OffWeapon = null;
                CurrentWeapon = o;
                o.AttachTo(this, WeaponSocket != null ? WeaponSocket : transform);
            }
            if (CurrentWeapon == null) ResetSheath();
            RecomputeEquipment();
            AnyWeaponChanged?.Invoke(this, CurrentWeapon);
        }

        /// <summary>Removes (destroys) everything carried: weapons and shield. Used to swap loadouts.</summary>
        public void UnequipAll()
        {
            if (OffWeapon != null) { var o = OffWeapon; OffWeapon = null; Destroy(o.gameObject); }
            if (CurrentWeapon != null) { var w = CurrentWeapon; CurrentWeapon = null; Destroy(w.gameObject); }
            if (EquippedShield != null) EquippedShield.Remove();
            ResetSheath();
            RecomputeEquipment();
        }

        void DropOff(Vector3 velocity)
        {
            var w = OffWeapon;
            if (w == null) return;
            OffWeapon = null;
            w.Drop(velocity);
            RecomputeEquipment();
        }

        /// <summary>
        /// Works out the grip and its numbers from what is carried:
        ///  - a shield on the arm gives its block values; with a two-hander or a bow it hangs on the back (no block
        ///    bonus, still weighs);
        ///  - spears/tridents go two-handed when the left hand is free;
        ///  - without a shield the weapon parries: two-handers a bit better, two weapons a bit worse, the bow badly.
        /// </summary>
        public void RecomputeEquipment()
        {
            WeaponStats main = WeaponInHand ? CurrentWeapon.Stats : null; // put away: empty hands
            bool needsBothHands = main != null && (main.Grip == Grip.TwoHand || main.IsBow);
            if (EquippedShield != null) EquippedShield.OnBack = needsBothHands;
            bool shieldArm = ShieldInArm;
            TwoHanded = main != null && (main.Grip == Grip.TwoHand || main.IsBow ||
                                         (main.Grip == Grip.Versatile && OffWeapon == null && !shieldArm));
            if (main == null) TwoHanded = false;
            EquipMoveMul = (CurrentWeapon != null ? CurrentWeapon.Stats.MoveMul : 1f) * (OffWeapon != null ? OffWeapon.Stats.MoveMul : 1f)
                           * (EquippedShield != null ? EquippedShield.Stats.MoveMul : 1f);
            if (shieldArm)
            {
                var st = EquippedShield.Stats;
                BlockReductionLight = st.BlockLight;
                BlockReductionHeavy = st.BlockHeavy;
                GuardBreakStagger = st.GuardBreakStagger;
                DisarmResistance = st.DisarmResistance;
            }
            else
            {
                if (main != null && main.IsBow) { BlockReductionLight = 0.6f; BlockReductionHeavy = 0.3f; }
                else if (TwoHanded) { BlockReductionLight = 0.92f; BlockReductionHeavy = 0.68f; }
                else if (OffWeapon != null && main != null) { BlockReductionLight = 0.88f; BlockReductionHeavy = 0.55f; }
                else { BlockReductionLight = 0.9f; BlockReductionHeavy = 0.6f; }
                GuardBreakStagger = 0.6f;
                DisarmResistance = TwoHanded && main != null && !main.IsBow ? 0.35f : 0f;
            }
        }

        // ------------------------------------------------------------------ core loop
        void Update()
        {
            float dt = Time.deltaTime;
            if (state == FighterState.Dead)
            {
                AnimateDeath(dt);
                return;
            }
            if (state == FighterState.KnockedOut)
            {
                stateTimer -= dt;
                if (stateTimer <= 0f) WakeUp(0.5f);
                UpdateFlash(dt);
                moveInput = Vector3.zero;
                return;
            }

            stateTimer -= dt;
            dodgeCooldown -= dt;
            switch (state)
            {
                case FighterState.Dodge:
                    if (stateTimer <= 0f) { SetState(FighterState.Idle, 0f); dodgeCooldown = 0.25f; }
                    break;
                case FighterState.Landing:
                case FighterState.Stunned:
                case FighterState.Taunt:
                    if (stateTimer <= 0f) SetState(FighterState.Idle, 0f);
                    break;
                case FighterState.Activity:
                    if (ActivityLeaving && stateTimer <= 0f) { ActivityLeaving = false; SetState(FighterState.Idle, 0f); }
                    else if (OnFurniture) UpdateFurniture(dt);
                    break;
                case FighterState.Traverse:
                    UpdateTraverse();
                    UpdateFlash(dt);
                    moveInput = Vector3.zero;
                    return;
                case FighterState.Airborne:
                    AirTime += dt;
                    apexY = Mathf.Max(apexY, transform.position.y);
                    if (IsGrounded && AirTime > 0.08f && verticalVelocity <= 0f)
                    {
                        LastFallHeight = apexY - transform.position.y;
                        Jumped = false;
                        bool hard = LastFallHeight > 1.3f;
                        SetState(FighterState.Landing, hard ? 0.45f : 0.16f);
                        AnyMove?.Invoke(this, hard ? "land_hard" : "land");
                    }
                    break;
                case FighterState.Windup:
                    airAttackTime += dt;
                    if (AirAttack != AirAttackKind.None)
                    {
                        if (AirAttack == AirAttackKind.Slam)
                        {
                            if (IsGrounded && airAttackTime > 0.03f) { stateTimer = 0f; AnyMove?.Invoke(this, "land_hard"); }
                            else verticalVelocity = Mathf.Min(verticalVelocity, -SlamSpeed);
                        }
                        else if (AirAttack == AirAttackKind.DiveKick) verticalVelocity = Mathf.Max(verticalVelocity, 0f); // chamber: hang
                    }
                    else if (Charging)
                    {
                        // Charged shot: keep drawing while the button is held (max 1.3 s); release fires.
                        chargeTime += dt;
                        ChargeLevel = Mathf.Clamp01(chargeTime / 1.0f);
                        if (chargeHeld && chargeTime < 1.3f) stateTimer = Mathf.Max(stateTimer, 0.0001f);
                    }
                    else if (attackType == AttackType.Heavy && chargeHeld && !ActiveStats.IsBow)
                    {
                        // Melee heavy held: stay wound up; after 2 s it is a power strike (disarms / breaks guards).
                        chargeTime += dt;
                        ChargeLevel = Mathf.Clamp01(chargeTime / PowerChargeTime);
                        if (chargeTime < 4f) stateTimer = Mathf.Max(stateTimer, 0.0001f);
                        if (chargeTime >= PowerChargeTime && !PowerCharged)
                        {
                            PowerCharged = true;
                            PowerStrike = true;
                            AnyMove?.Invoke(this, "power_ready");
                        }
                    }
                    if (stateTimer <= 0f) SetState(FighterState.Active, timing.Active);
                    break;
                case FighterState.Active:
                    if (AirAttack == AirAttackKind.DiveKick)
                    {
                        knockback = transform.forward * DiveKickSpeed;
                        verticalVelocity = Mathf.Min(verticalVelocity, -DiveKickDrop);
                        DetectHits();
                        if (IsGrounded && stateDuration - stateTimer > 0.05f)
                        {
                            knockback = transform.forward * 3f;
                            AnyMove?.Invoke(this, "land_hard");
                            SetState(FighterState.Recovery, timing.Recovery);
                        }
                        else if (stateTimer <= 0f) SetState(FighterState.Recovery, timing.Recovery);
                        break;
                    }
                    if ((!ActiveStats.IsBow || attackType == AttackType.Kick) && !threwThisSwing) DetectHits();
                    if (stateTimer <= 0f && state == FighterState.Active) SetState(FighterState.Recovery, timing.Recovery);
                    break;
                case FighterState.Recovery:
                case FighterState.Stagger:
                    if (stateTimer <= 0f) SetState(FighterState.Idle, 0f);
                    break;
            }

            // Walking off a ledge.
            ungrounded = IsGrounded ? 0f : ungrounded + dt;
            if (IsGrounded) airSlashes = 0;
            if (FreeToMove && ungrounded > 0.15f && verticalVelocity < -1f) { Jumped = false; EnterAir(); }

            bool canCrouch = (state == FighterState.Idle || state == FighterState.Blocking) && IsGrounded;
            if (Crouching != (crouchHeld && canCrouch))
            {
                Crouching = crouchHeld && canCrouch;
                ApplyCrouchShape();
            }

            if (state == FighterState.Idle && blockHeld) SetState(FighterState.Blocking, 0f);
            else if (state == FighterState.Blocking && !blockHeld) SetState(FighterState.Idle, 0f);

            UpdateSheath(dt);
            if ((state == FighterState.Idle || state == FighterState.Blocking) && bufferedAttack.HasValue && sheathT < 0f)
            {
                var b = bufferedAttack.Value;
                bufferedAttack = null;
                if (Time.time <= bufferExpire) { if (InAir) TryAirAttack(b); else StartAttack(b); }
            }

            Locomotion(dt);
            AnimateArm(dt);
            UpdateFlash(dt);
            moveInput = Vector3.zero;
        }

        void SetState(FighterState s, float duration)
        {
            if (state == FighterState.Traverse && s != FighterState.Traverse && Controller != null) Controller.enabled = true;
            if (state == FighterState.Activity && s != FighterState.Activity && OnFurniture) LeaveFurniture();
            state = s;
            if (s != FighterState.Windup && s != FighterState.Active && s != FighterState.Recovery) AirAttack = AirAttackKind.None;
            if (s == FighterState.Stunned && stunStars == null) stunStars = StunStars.Create(this);
            if (stunStars != null) stunStars.Show(s == FighterState.Stunned);
            stateTimer = duration;
            stateDuration = Mathf.Max(duration, 0.0001f);
            if (s == FighterState.Active)
            {
                AnyAttackSwing?.Invoke(this, attackType);
                hitThisSwing.Clear();
                threwThisSwing = false;
                if (throwing) { throwing = false; threwThisSwing = true; ReleaseThrow(); return; }
                if (ActiveStats.IsBow && attackType != AttackType.Kick) { FireArrow(); return; }
                if (AirAttack == AirAttackKind.DiveKick) { verticalVelocity = -DiveKickDrop; AnyMove?.Invoke(this, "dive"); return; }
                // Small lunge; the kick steps in as far as it needs to reach the target (see StartAttack).
                knockback += transform.forward * (attackType == AttackType.Light ? 2.5f : attackType == AttackType.Kick ? kickLunge : 4f);
            }
        }

        float Phase => Mathf.Clamp01(1f - stateTimer / stateDuration);

        Timing GetTiming(AttackType t)
        {
            if (t == AttackType.Kick)
            {
                // The kick does not depend on the weapon, only on the fighter's speed.
                float k = Mathf.Max(0.1f, AttackSpeedMultiplier);
                return new Timing { Windup = 0.2f / k, Active = 0.12f / k, Recovery = 0.32f / k };
            }
            if (ActiveStats.IsBow)
            {
                float b = Mathf.Max(0.1f, AttackSpeedMultiplier);
                return t == AttackType.Light
                    ? new Timing { Windup = 0.24f / b, Active = 0.08f, Recovery = 0.22f / b }
                    : new Timing { Windup = 0.35f / b, Active = 0.08f, Recovery = 0.3f / b };
            }
            float s = Mathf.Max(0.1f, AttackStats.Speed * AttackSpeedMultiplier * (DualWielding && t == AttackType.Light ? 1.15f : 1f));
            return t == AttackType.Light
                ? new Timing { Windup = 0.16f / s, Active = 0.12f / s, Recovery = 0.22f / s }
                : new Timing { Windup = 0.42f / s, Active = 0.14f / s, Recovery = 0.45f / s };
        }

        /// <summary>
        /// Kick: turn a little towards the nearest rival in front (within 2.8 m, 75 deg) and set the step-in so the
        /// foot arrives at kick range: up to KickLungeMax metres. With nobody close, a fixed short step.
        /// Knockback decays at 20 m/s^2, so a start speed v covers v^2 / 40 metres.
        /// </summary>
        void PrepareKickLunge()
        {
            Fighter best = null;
            float bestD = 2.8f;
            foreach (var f in All)
            {
                if (f == this || !f.IsAlive || f.TeamId == TeamId) continue;
                Vector3 to = f.transform.position - transform.position;
                if (Mathf.Abs(to.y) > 1.2f) continue;
                to.y = 0f;
                float d = to.magnitude;
                if (d < bestD && Vector3.Angle(transform.forward, to) < 75f) { bestD = d; best = f; }
            }
            float step = KickLungeMin;
            if (best != null)
            {
                Vector3 to = best.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    Quaternion look = Quaternion.LookRotation(to);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 40f);
                    desiredFacing = to.normalized;
                }
                float gap = bestD - Radius - best.Radius - KickReach * 0.6f;
                step = Mathf.Clamp(gap, KickLungeMin, KickLungeMax);
            }
            kickLunge = Mathf.Sqrt(2f * 20f * step);
            knockback += transform.forward * (kickLunge * 0.35f); // start moving in during the chamber
        }

        /// <summary>
        /// Looses an arrow from chest height along the facing; a rival inside a narrow cone ahead gets aimed at
        /// (chest). Quick shot: light damage, slower. Charged: up to heavy damage and much faster; a full draw
        /// counts as a heavy blow (breaks guards).
        /// </summary>
        void FireArrow()
        {
            var bow = ActiveStats;
            float charge = attackType == AttackType.Heavy ? ChargeLevel : 0f;
            Vector3 from = transform.position + Vector3.up * (Controller != null ? Controller.height * 0.78f : 1.4f) + transform.forward * 0.55f;
            Vector3 dir = transform.forward;
            Fighter best = null;
            float bestAngle = 7f;
            foreach (var f in All)
            {
                if (f == this || !f.IsAlive || f.TeamId == TeamId) continue;
                Vector3 to = f.transform.position + Vector3.up * 1.2f - from;
                if (to.magnitude > 35f) continue;
                float a = Vector3.Angle(new Vector3(dir.x, 0f, dir.z), new Vector3(to.x, 0f, to.z));
                if (a < bestAngle) { bestAngle = a; best = f; }
            }
            float speed = Mathf.Lerp(22f, 40f, charge);
            if (best != null)
            {
                Vector3 to = best.transform.position + Vector3.up * 1.2f - from;
                float t = to.magnitude / speed;
                to.y += 0.5f * 3.5f * t * t; // compensate the arrow's drop
                dir = to.normalized;
            }
            float dmg = Mathf.Lerp(bow.LightDamage, bow.HeavyDamage, charge) * DamageMultiplier;
            var arrow = Arrow.Fire(this, from, dir, speed, dmg, charge >= 0.95f ? AttackType.Heavy : AttackType.Light);
            AnyArrowFired?.Invoke(this, arrow);
        }

        void StartAttack(AttackType type, AirAttackKind air = AirAttackKind.None)
        {
            attackType = type;
            AirAttack = air;
            airAttackTime = 0f;
            // (heavy armour slows the recovery after every attack: see the end of this method)
            // Two weapons: light attacks alternate right / left while the combo is kept going; heavy uses both.
            AttackHand = 0;
            if (DualWielding && type == AttackType.Light)
            {
                comboIndex = Time.time <= comboExpire ? comboIndex + 1 : 0;
                AttackHand = comboIndex % 2;
            }
            else if (DualWielding && type == AttackType.Heavy) AttackHand = 2;
            comboExpire = Time.time + 1.0f;
            chargeTime = 0f;
            ChargeLevel = 0f;
            // Disarm combo: light, light, heavy (each within ~1.2 s of the last).
            PowerCharged = false;
            if (type == AttackType.Light && !throwing)
            {
                lightChain = Time.time - lastLightTime < 1.2f ? lightChain + 1 : 1;
                lastLightTime = Time.time;
                PowerStrike = false;
            }
            else
            {
                PowerStrike = type == AttackType.Heavy && lightChain >= 2 && Time.time - lastLightTime < 1.4f && !ActiveStats.IsBow;
                lightChain = 0;
            }
            if (PowerStrike) AnyMove?.Invoke(this, "power_combo");
            timing = GetTiming(type);
            armStartRot = ArmPivot != null ? ArmPivot.localRotation : Quaternion.identity;
            if (type == AttackType.Kick && air == AirAttackKind.None) PrepareKickLunge();
            switch (air)
            {
                case AirAttackKind.Slash:
                    if (airSlashes++ == 0) verticalVelocity = Mathf.Max(verticalVelocity, 1.5f); // a short hang, once per jump
                    break;
                case AirAttackKind.DiveKick:
                    PowerStrike = false;
                    verticalVelocity = Mathf.Max(verticalVelocity, 1f);
                    timing = new Timing { Windup = 0.14f, Active = 1.5f, Recovery = 0.3f }; // Active ends on landing
                    break;
                case AirAttackKind.Slam:
                    PowerStrike = false;
                    verticalVelocity = -SlamSpeed;
                    timing.Windup = 1.5f; // Windup lasts until the feet touch the ground
                    break;
            }
            timing.Recovery *= 1f + 0.35f * ArmorLoad;
            SetState(FighterState.Windup, timing.Windup);
            AnyAttackStarted?.Invoke(this, type);
        }

        void Locomotion(float dt)
        {
            float mul;
            switch (state)
            {
                case FighterState.Blocking: mul = 0.45f; break;
                case FighterState.Windup: mul = 0.3f; break;
                case FighterState.Active: mul = 0.2f; break;
                case FighterState.Recovery: mul = 0.55f; break;
                case FighterState.Stagger: mul = 0.1f; break;
                case FighterState.Stunned: mul = 0f; break;
                case FighterState.Taunt: mul = 0f; break;
                case FighterState.Activity: mul = 0f; break;
                case FighterState.Airborne: mul = 0.85f; break;
                case FighterState.Landing: mul = LastFallHeight > 1.3f ? 0.1f : 0.6f; break;
                case FighterState.Dodge: mul = 0f; break;
                default: mul = 1f; break;
            }
            if (WalkMode && IsGrounded) mul *= WalkMul;
            if (AirAttack == AirAttackKind.Slash && !IsGrounded) mul = 0.8f;
            else if (AirAttack == AirAttackKind.DiveKick || AirAttack == AirAttackKind.Slam) mul = state == FighterState.Recovery ? 0.3f : 0.1f;
            if (Crouching) mul *= 0.45f;

            Vector3 v = moveInput * (MoveSpeed * EquipMoveMul * ArmorMoveMul * mul) + knockback;
            if (state == FighterState.Dodge)
            {
                float p = Phase;
                v += dodgeDir * ((DodgeBack ? 6.5f : DodgeSpeed) * (1f - 0.4f * ArmorLoad) * (1f - p * p));
            }
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, 20f * dt);

            if (Controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            else verticalVelocity += Physics.gravity.y * dt;
            v.y = verticalVelocity;
            if (Controller.enabled) Controller.Move(v * dt);

            bool rolling = state == FighterState.Dodge && !DodgeBack;
            if (desiredFacing.sqrMagnitude > 0.0001f && state != FighterState.Stagger && state != FighterState.Stunned && !rolling)
            {
                float turn = TurnSpeed * (state == FighterState.Active ? 0.2f : state == FighterState.Windup ? 0.7f : 1f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(desiredFacing), turn * dt);
            }
        }

        // ------------------------------------------------------------------ hits
        void DetectHits()
        {
            if (Harmless) return;
            WeaponStats s = AttackStats;
            bool versatile2H = TwoHanded && s.Grip == Grip.Versatile;
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            float reach = attackType == AttackType.Kick ? KickReach : s.Reach + (versatile2H ? s.TwoHandReachBonus : 0f);
            float searchRadius = reach + Radius + 0.8f;
            float arc = attackType == AttackType.Light ? 120f : attackType == AttackType.Kick ? 80f : 150f;
            if (s.Style == WeaponStyle.Spear && attackType != AttackType.Kick) arc = attackType == AttackType.Light ? 60f : 80f; // thrusts
            float vertical = 1.2f;
            if (AirAttack == AirAttackKind.Slash || AirAttack == AirAttackKind.DiveKick) { vertical = 2.3f; reach += 0.25f; }
            if (AirAttack == AirAttackKind.DiveKick) arc = 110f;
            if (AirAttack == AirAttackKind.Slam) { arc = Mathf.Max(arc, 200f); reach += 0.3f; }
            Collider[] cols = Physics.OverlapSphere(origin, searchRadius, Ragdoll.WorldMask, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
            {
                var prop = c.GetComponent<TownProp>();
                if (prop != null)
                {
                    // crates and jars take the blow too
                    Vector3 tp = prop.transform.position - transform.position;
                    if (Mathf.Abs(tp.y) > vertical + 0.4f) continue;
                    tp.y = 0f;
                    if (tp.magnitude - Radius - prop.Size.x * 0.5f > reach) continue;
                    if (tp.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, tp) > arc * 0.5f) continue;
                    float pd = (attackType == AttackType.Light ? s.LightDamage : attackType == AttackType.Kick ? KickDamage : s.HeavyDamage) * DamageMultiplier;
                    prop.Hit(this, pd, tp.sqrMagnitude > 0.0001f ? tp.normalized : transform.forward);
                    continue;
                }
                var f = c.GetComponentInParent<Fighter>();
                if (f == null || f == this || !f.IsAlive || hitThisSwing.Contains(f)) continue;
                if (f.TeamId == TeamId) continue;
                Vector3 to = f.transform.position - transform.position;
                if (Mathf.Abs(to.y) > vertical) continue; // up on a platform / below it
                to.y = 0f;
                float edgeDist = to.magnitude - f.Radius - Radius;
                if (edgeDist > reach) continue;
                if (to.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, to) > arc * 0.5f) continue;

                // Dodged (i-frames) or ducked under a horizontal slash: the swing goes through.
                if (f.Invulnerable || (f.Crouching && attackType == AttackType.Light))
                {
                    hitThisSwing.Add(f);
                    AnyAttackEvaded?.Invoke(this, f);
                    continue;
                }

                hitThisSwing.Add(f);
                Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward;
                float dmg = attackType == AttackType.Light ? s.LightDamage : attackType == AttackType.Kick ? KickDamage : s.HeavyDamage;
                if (versatile2H && attackType != AttackType.Kick) dmg *= s.TwoHandDamageMul;
                if (AttackHand == 2 && OffWeapon != null) dmg += OffWeapon.Stats.LightDamage * 0.6f; // both weapons
                if (PowerStrike) dmg *= PowerCharged ? 1.5f : 1.2f;
                if (AirAttack == AirAttackKind.DiveKick) dmg *= DiveKickDamageMul;
                else if (AirAttack == AirAttackKind.Slam) dmg *= SlamDamageMul;
                dmg *= DamageMultiplier;
                HitResult r = f.ReceiveHit(this, dmg, attackType, dir);
                if (r.Outcome == HitOutcome.Blocked && attackType == AttackType.Light)
                {
                    // Bounced off a guard: attacker recoils.
                    SetState(FighterState.Stagger, 0.35f);
                    knockback = -dir * 3f;
                    break;
                }
            }
        }

        /// <summary>Damage of the last blow before armour / guard (online: the owner of the target applies it).</summary>
        public float LastRawDamage { get; private set; }

        public HitResult ReceiveHit(Fighter attacker, float damage, AttackType type, Vector3 dir)
        {
            LastRawDamage = damage;
            // out cold on the ground: nothing more to do to him
            if (state == FighterState.KnockedOut) return new HitResult { Attacker = attacker, Target = this, Type = type, Outcome = HitOutcome.Hit, Point = transform.position };
            var r = new HitResult
            {
                Attacker = attacker,
                Target = this,
                Type = type,
                Point = transform.position + Vector3.up * 1.25f - dir * 0.3f
            };

            bool facing = Vector3.Dot(transform.forward, -dir) > 0.3f;
            float disarmChance;
            // A quick (uncharged) arrow hurts but does not stop what you are doing; only charged shots stagger.
            bool quickArrow = attacker != null && attacker.ActiveStats.IsBow && type == AttackType.Light;
            bool power = attacker != null && attacker.PowerStrike && type == AttackType.Heavy && attacker.State == FighterState.Active;
            r.Power = power;
            // Where it lands depends on the blow (overheads go high, kicks low, thrusts to the body); the armour on
            // that zone stops its share of the damage.
            var zone = PickZone(attacker, type);
            float absorbed = Armor != null ? Armor.Protection(zone) : 0f;
            r.Zone = zone;
            r.ArmorAbsorbed = absorbed;
            r.Point = ZonePoint(zone, dir);
            damage *= 1f - absorbed;
            if (state == FighterState.Blocking && facing)
            {
                if (type == AttackType.Light)
                {
                    r.Outcome = HitOutcome.Blocked;
                    damage *= 1f - BlockReductionLight;
                    knockback = dir * 2.5f;
                    disarmChance = 0f;
                }
                else
                {
                    // Heavy blows break the guard. A kick or a power strike breaks it and leaves the blocker dazed for
                    // KickStunDuration seconds: no moving, attacking or blocking until it wears off or they get hit.
                    bool kick = type == AttackType.Kick || power;
                    r.Outcome = HitOutcome.GuardBreak;
                    damage *= 1f - BlockReductionHeavy;
                    knockback = dir * (kick ? 4f : 5f);
                    if (kick)
                    {
                        SetState(FighterState.Stunned, KickStunDuration);
                        AnyMove?.Invoke(this, "stunned");
                    }
                    else
                    {
                        var aw = attacker != null ? attacker.AttackStats.Shape : WeaponShape.Fists;
                        bool blunt = aw == WeaponShape.Mace || aw == WeaponShape.Maul;
                        SetState(FighterState.Stagger, GuardBreakStagger * (blunt ? 1.6f : 1f));
                    }
                    disarmChance = 0f;
                }
            }
            else
            {
                r.Outcome = HitOutcome.Hit;
                flashAmount = 1f;
                if (quickArrow)
                {
                    knockback += dir * 0.8f;
                    disarmChance = 0f;
                }
                else
                {
                    knockback = dir * (type == AttackType.Light ? 3.5f : type == AttackType.Kick ? 8.5f : 7f);
                    SetState(FighterState.Stagger, type == AttackType.Light ? 0.3f : type == AttackType.Kick ? 0.5f : 0.55f);
                    disarmChance = power ? 1f : 0f; // only the combo / charged power strike disarms
                }
            }

            bufferedAttack = null;
            lastHitDir = dir;
            lastHitPoint = r.Point;
            r.Damage = damage;

            if (!Harmless && WeaponInHand && disarmChance > 0f && UnityEngine.Random.value < disarmChance)
            {
                r.Disarmed = true;
                DropWeapon(dir * 2.5f + Vector3.up * 4.5f + transform.right * UnityEngine.Random.Range(-1.5f, 1.5f));
            }

            if (Harmless) { damage = 0f; r.Disarmed = false; }
            // Non-lethal places (the town): never below MinHealth.
            if (MinHealth > 0f) damage = Mathf.Min(damage, Mathf.Max(0f, Health.Current - MinHealth));
            if (attacker != null) LastAttacker = attacker;
            Health.TakeDamage(damage);
            r.Killed = Health.IsDead;
            AnyHitResolved?.Invoke(r);
            return r;
        }

        Gladiator.Character.ArmorZone PickZone(Fighter attacker, AttackType type)
        {
            float head, torso, arms;
            bool ranged = attacker != null && attacker.ActiveStats.IsBow && type != AttackType.Kick;
            bool thrust = attacker != null && attacker.AttackStats.Style == WeaponStyle.Spear && type != AttackType.Kick;
            if (attacker == null) { head = 0f; torso = 1f; arms = 0f; }
            else if (type == AttackType.Kick) { head = 0f; torso = 0.55f; arms = 0f; }
            else if (ranged) { head = 0.2f; torso = 0.5f; arms = 0.15f; }
            else if (thrust) { head = 0.15f; torso = 0.65f; arms = 0.1f; }
            else if (type == AttackType.Heavy) { head = 0.35f; torso = 0.35f; arms = 0.2f; }
            else { head = 0.12f; torso = 0.5f; arms = 0.25f; }
            float r = UnityEngine.Random.value;
            if (r < head) return Gladiator.Character.ArmorZone.Head;
            if (r < head + torso) return Gladiator.Character.ArmorZone.Torso;
            if (r < head + torso + arms) return Gladiator.Character.ArmorZone.Arms;
            return Gladiator.Character.ArmorZone.Legs;
        }

        Vector3 ZonePoint(Gladiator.Character.ArmorZone zone, Vector3 dir)
        {
            float h = standHeight > 0f ? standHeight : (Controller != null ? Controller.height : 1.8f);
            float k = zone == Gladiator.Character.ArmorZone.Head ? 0.9f : zone == Gladiator.Character.ArmorZone.Torso ? 0.66f :
                      zone == Gladiator.Character.ArmorZone.Arms ? 0.6f : 0.3f;
            Vector3 side = zone == Gladiator.Character.ArmorZone.Arms ? transform.right * (UnityEngine.Random.value < 0.5f ? -0.3f : 0.3f) : Vector3.zero;
            return transform.position + Vector3.up * (h * k) - dir * 0.3f + side;
        }

        void Die()
        {
            if (state == FighterState.Dead) return;
            state = FighterState.Dead;
            deathT = 0f;
            if (OffWeapon != null) DropOff(lastHitDir * 1.5f + Vector3.up * 3f + transform.right);
            if (CurrentWeapon != null) DropWeapon(lastHitDir * 2f + Vector3.up * 3f, false);
            Vector3 moving = Controller.enabled ? Controller.velocity : Vector3.zero;
            Controller.enabled = false;
            GoLimp(moving);
            Vector3 d = lastHitDir.sqrMagnitude > 0.01f ? lastHitDir : -transform.forward;
            deathAxis = Vector3.Cross(Vector3.up, d).normalized;
            if (deathAxis.sqrMagnitude < 0.01f) deathAxis = transform.right;
            if (BodyRoot != null) bodyStartRot = BodyRoot.rotation;
            if (BlobShadow != null) BlobShadow.SetActive(false);
            BloodPool.Spawn(this);
            AnyDied?.Invoke(this);
        }

        /// <summary>The body becomes a rag doll, carried by what it was doing and shoved by the last blow.</summary>
        void GoLimp(Vector3 moving)
        {
            rag = Ragdoll.For(this);
            if (rag == null) return;
            moving.y = Mathf.Clamp(moving.y, -6f, 3f);
            Vector3 push = knockback;
            float force = Mathf.Clamp(push.magnitude, 0f, 10f);
            Vector3 dir = lastHitDir.sqrMagnitude > 0.01f ? lastHitDir.normalized : force > 0.1f ? push.normalized : -transform.forward;
            Vector3 point = lastHitPoint.sqrMagnitude > 0.01f && (lastHitPoint - transform.position).sqrMagnitude < 9f
                ? lastHitPoint : transform.position + Vector3.up * (standHeight > 0f ? standHeight : 1.8f) * 0.66f;
            rag.Activate(moving * 0.6f + push * 0.22f, (dir + Vector3.up * 0.2f) * (22f + force * 17f), point);
            knockback = Vector3.zero;
        }

        /// <summary>
        /// Knocked out: the body drops as a rag doll and lies there for <paramref name="seconds"/> (or until
        /// <see cref="WakeUp"/>). Nothing can hit it meanwhile. False if it cannot (dead, already down).
        /// </summary>
        public bool KnockDown(Vector3 dir, float seconds)
        {
            if (!IsAlive || state == FighterState.KnockedOut) return false;
            Vector3 moving = Controller != null && Controller.enabled ? Controller.velocity : Vector3.zero;
            blockHeld = false;
            bufferedAttack = null;
            SetState(FighterState.KnockedOut, Mathf.Max(0.2f, seconds));
            if (Controller != null) Controller.enabled = false;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f) { lastHitDir = dir.normalized; knockback = lastHitDir * 4f; }
            lastHitPoint = Vector3.zero;
            GoLimp(moving);
            AnyMove?.Invoke(this, "knockdown");
            return true;
        }

        /// <summary>Comes round: the animator takes the body back, blending up from the floor in <paramref name="blend"/> seconds (0 = at once).</summary>
        public void WakeUp(float blend = 0.5f)
        {
            if (state != FighterState.KnockedOut) return;
            if (rag != null) rag.Release(blend);
            knockback = Vector3.zero;
            verticalVelocity = 0f;
            if (Controller != null) Controller.enabled = true;
            SetState(FighterState.Idle, 0f);
        }

        // ------------------------------------------------------------------ procedural animation
        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
        static float EaseIn(float t) => t * t;
        static float EaseInOut(float t) => t * t * (3f - 2f * t);

        void AnimateArm(float dt)
        {
            if (ArmPivot == null || ExternalArmControl) return;
            if (attackType == AttackType.Kick && IsAttacking)
            {
                ArmPivot.localRotation = Quaternion.Slerp(ArmPivot.localRotation, Quaternion.Euler(PoseIdle), 1f - Mathf.Exp(-18f * dt));
                return;
            }
            Vector3 back = attackType == AttackType.Light ? PoseLightBack : PoseHeavyBack;
            Vector3 end = attackType == AttackType.Light ? PoseLightEnd : PoseHeavyEnd;
            float p = Phase;
            switch (state)
            {
                case FighterState.Windup:
                    ArmPivot.localRotation = Quaternion.Slerp(armStartRot, Quaternion.Euler(back), EaseOut(p));
                    return;
                case FighterState.Active:
                    ArmPivot.localRotation = Quaternion.Euler(Vector3.Lerp(back, end, EaseIn(p)));
                    return;
                case FighterState.Recovery:
                    ArmPivot.localRotation = Quaternion.Slerp(Quaternion.Euler(end), Quaternion.Euler(PoseIdle), EaseInOut(p));
                    return;
            }
            Vector3 target = state == FighterState.Blocking ? PoseBlock : state == FighterState.Stagger ? PoseStagger : PoseIdle;
            ArmPivot.localRotation = Quaternion.Slerp(ArmPivot.localRotation, Quaternion.Euler(target), 1f - Mathf.Exp(-18f * dt));
        }

        void AnimateDeath(float dt)
        {
            if (BodyRoot == null) return;
            if (Ragdolled) { UpdateFlash(dt); return; } // physics has the body
            if (ExternalDeathAnimation)
            {
                // Killed in the air or while climbing: fall to whatever is below.
                if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit g, 50f, Ragdoll.WorldMask, QueryTriggerInteraction.Ignore)
                    && g.distance > 0.12f)
                {
                    deathFall += Physics.gravity.y * dt;
                    transform.position += Vector3.up * Mathf.Max(deathFall * dt, -(g.distance - 0.1f));
                }
                else deathFall = 0f;
                UpdateFlash(dt);
                return;
            }
            deathT += dt;
            float t = EaseOut(Mathf.Clamp01(deathT / 0.6f));
            BodyRoot.rotation = Quaternion.AngleAxis(86f * t, deathAxis) * bodyStartRot;
            UpdateFlash(dt);
        }

        void UpdateFlash(float dt)
        {
            if (FlashRenderers == null || (flashAmount <= 0f && mpb == null)) return;
            if (flashAmount <= 0f) return;
            flashAmount = Mathf.Max(0f, flashAmount - dt * 8f);
            mpb.SetColor(FlashId, new Color(1f, 1f, 1f, flashAmount * 0.85f));
            foreach (var r in FlashRenderers)
                if (r != null) r.SetPropertyBlock(flashAmount > 0f ? mpb : null);
        }
    }
}
