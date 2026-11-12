using Gladiator.Core;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// Player input -> Fighter intents. WASD / left stick moves (camera relative),
    /// the fighter turns towards the mouse cursor or the right stick.
    /// With a gamepad and no right-stick input there is a soft lock-on to the closest enemy.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class PlayerController : MonoBehaviour
    {
        public Camera AimCamera;
        public float SoftLockRange = 4f;

        Fighter fighter;
        public Fighter Fighter => fighter;
        public Weapon NearbyWeapon { get; private set; }
        float dropHeldTime;
        bool dropHandled;

        // T tapped: a quick provocation. T held: the taunt wheel. X held: the activity wheel, X tapped: get up / repeat.
        const float WheelDelay = 0.25f;

        /// <summary>A button that opens a 4-slot wheel when held (Gladiator.UI.TauntWheel).</summary>
        class HoldWheel
        {
            public float HeldTime;
            public Vector2 Mouse;
            public Gladiator.UI.TauntWheel Wheel;
            public bool Open => Wheel != null && Wheel.IsOpen;
            public void Close() { if (Open) Wheel.Close(); HeldTime = 0f; }
        }
        readonly HoldWheel tauntWheel = new HoldWheel(), actWheel = new HoldWheel();
        int lastActivity = 2, pendingActivity = -1;

        /// <summary>True while a wheel is open (movement and attacks are paused).</summary>
        public bool TauntWheelOpen => tauntWheel.Open || actWheel.Open;

        void Awake() { fighter = GetComponent<Fighter>(); fighter.AutoDraw = false; }
        void OnDisable() { NearbyWeapon = null; CloseWheels(); }
        void OnDestroy()
        {
            foreach (var h in new[] { tauntWheel, actWheel }) if (h.Wheel != null) Destroy(h.Wheel.gameObject);
            GameInput.WheelOpen = false;
        }

        void CloseWheels()
        {
            tauntWheel.Close();
            actWheel.Close();
            GameInput.WheelOpen = false;
        }

        /// <summary>Runs one hold-wheel. Returns: -2 nothing, -1 tapped, 0..3 slot chosen on release; busy = the wheel takes the input.</summary>
        int UpdateWheel(HoldWheel h, bool held, string title, string[] labels, out bool busy)
        {
            int result = -2;
            if (held)
            {
                h.HeldTime += Time.unscaledDeltaTime;
                if (h.HeldTime >= WheelDelay && !h.Open)
                {
                    if (h.Wheel == null) h.Wheel = Gladiator.UI.TauntWheel.Create(title, labels);
                    h.Mouse = Vector2.zero;
                    h.Wheel.Open();
                    GameInput.WheelOpen = true;
                }
            }
            if (h.Open)
            {
                int sel = h.Wheel.Selected;
                int bySticks = Gladiator.UI.TauntWheel.SlotFor(GameInput.Move, 0.5f);
                h.Mouse = Vector2.ClampMagnitude(h.Mouse + GameInput.WheelMouseDelta, 120f);
                int byMouse = Gladiator.UI.TauntWheel.SlotFor(h.Mouse, 40f);
                if (bySticks >= 0) sel = bySticks;
                else if (byMouse >= 0) sel = byMouse;
                for (int d = 1; d <= 4; d++) if (GameInput.DigitPressed(d)) sel = d - 1;
                if (sel != h.Wheel.Selected) h.Wheel.SetSelected(sel);
            }
            busy = h.Open;
            if (!held && h.HeldTime > 0f)
            {
                if (h.Open)
                {
                    result = h.Wheel.Selected;
                    if (result < 0) result = -2; // nothing chosen
                    h.Wheel.Close();
                    GameInput.WheelOpen = tauntWheel.Open || actWheel.Open;
                }
                else result = -1;
                h.HeldTime = 0f;
                busy = true;
            }
            return result;
        }

        void Update()
        {
            GameInput.UpdateDeviceMode();
            NearbyWeapon = null;
            if (!fighter.IsAlive || AimCamera == null)
            {
                if (TauntWheelOpen || tauntWheel.HeldTime > 0f || actWheel.HeldTime > 0f) CloseWheels();
                return;
            }
            if (GameInput.WalkTogglePressed)
            {
                fighter.WalkMode = !fighter.WalkMode;
                string msg = fighter.WalkMode ? "CAMINANDO" : "CORRIENDO";
                var gh = FindAnyObjectByType<Gladiator.UI.GameHUD>();
                if (gh != null) gh.ShowMessage(msg, 1f);
                var th = FindAnyObjectByType<Gladiator.UI.TrainingHUD>();
                if (th != null) th.ShowMessage(msg, 1f);
            }

            bool busyT = false, busyA = false;
            if (!actWheel.Open)
            {
                int t = UpdateWheel(tauntWheel, GameInput.TauntHeld, "BURLA", Gladiator.UI.TauntWheel.Labels, out busyT);
                if (t == -1) fighter.TryTaunt(Random.Range(0, 2)); // tap: beat the chest or beckon
                else if (t >= 0) fighter.TryTaunt(Gladiator.UI.TauntWheel.Kinds[t]);
            }
            if (!tauntWheel.Open)
            {
                int a = UpdateWheel(actWheel, GameInput.ActivityHeld, "ACCION", Gladiator.UI.TauntWheel.ActivityLabels, out busyA);
                if (a == -1)
                {
                    // tap: get up, or repeat the last activity
                    if (fighter.InActivity) fighter.StopActivity();
                    else fighter.TryActivity(lastActivity);
                }
                else if (a >= 0 && Gladiator.UI.TauntWheel.ActivityKinds[a] >= 0)
                {
                    lastActivity = Gladiator.UI.TauntWheel.ActivityKinds[a];
                    if (fighter.InActivity)
                    {
                        // another activity: get up first, then start it
                        if (fighter.ActivityKind != lastActivity) pendingActivity = lastActivity;
                        fighter.StopActivity();
                    }
                    else fighter.TryActivity(lastActivity);
                }
            }
            if (busyT || busyA) { fighter.Move(Vector3.zero); return; }
            if (pendingActivity >= 0 && fighter.State == FighterState.Idle)
            {
                fighter.TryActivity(pendingActivity);
                pendingActivity = -1;
            }
            else if (pendingActivity >= 0 && !fighter.InActivity) pendingActivity = -1;

            if (fighter.InActivity)
            {
                // Any move / attack / jump gets the fighter up; nothing else happens meanwhile.
                fighter.Move(Vector3.zero);
                if (GameInput.Move.sqrMagnitude > 0.2f || GameInput.LightAttackPressed || GameInput.HeavyAttackPressed ||
                    GameInput.KickPressed || GameInput.JumpPressed || GameInput.DodgePressed || GameInput.BlockHeld)
                    fighter.StopActivity();
                return;
            }

            Vector3 fwd = AimCamera.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = AimCamera.transform.right; right.y = 0f; right.Normalize();

            Vector2 m = GameInput.Move;
            Vector3 move = fwd * m.y + right * m.x;
            fighter.Move(move);

            Vector3 face = Vector3.zero;
            var orbit = Gladiator.Arena.ArenaCamera.Instance;
            if (orbit != null && orbit.ShoulderActive)
            {
                // Souls style: face where you walk; locked on, always face the rival.
                // An attack with a direction held snaps the body that way first.
                Fighter lockT = orbit.LockTarget;
                bool attackNow = GameInput.LightAttackPressed || GameInput.HeavyAttackPressed || GameInput.KickPressed;
                if (lockT != null) face = lockT.transform.position - transform.position;
                else if (move.sqrMagnitude > 0.01f)
                {
                    face = move;
                    if (attackNow && fighter.State != FighterState.Airborne && !fighter.IsAttacking)
                        transform.rotation = Quaternion.LookRotation(new Vector3(move.x, 0f, move.z));
                }
            }
            else if (GameInput.UsingGamepad)
            {
                Vector2 a = GameInput.AimStick;
                if (a.sqrMagnitude > 0.09f) face = fwd * a.y + right * a.x;
                else
                {
                    Fighter lockTarget = ClosestEnemy(SoftLockRange);
                    if (lockTarget != null) face = lockTarget.transform.position - transform.position;
                    else if (move.sqrMagnitude > 0.01f) face = move;
                }
            }
            else
            {
                Ray ray = PS2RenderSetup.Instance != null
                    ? PS2RenderSetup.Instance.ScreenPointToRay(GameInput.MouseScreenPosition)
                    : AimCamera.ScreenPointToRay(GameInput.MouseScreenPosition);
                var plane = new Plane(Vector3.up, transform.position + Vector3.up * 1f);
                if (plane.Raycast(ray, out float d)) face = ray.GetPoint(d) - transform.position;
            }
            face.y = 0f;
            if (face.sqrMagnitude > 0.04f) fighter.SetFacing(face);

            if (GameInput.SheathPressed) fighter.ToggleSheath(); // R: put the weapon away / draw it
            if (GameInput.TorchPressed) { Torch.Ensure(fighter); fighter.ToggleTorch(); } // H: the torch
            if (GameInput.LightAttackPressed) fighter.TryAttack(AttackType.Light);
            if (GameInput.HeavyAttackPressed) fighter.TryAttack(AttackType.Heavy);
            if (GameInput.KickPressed) fighter.TryAttack(AttackType.Kick);

            // G: a tap drops the weapon at your feet; held for 0.35 s it throws it.
            bool g = GameInput.DropHeld;
            if (g) dropHeldTime += Time.deltaTime;
            if (g && !dropHandled && dropHeldTime >= 0.35f) { dropHandled = true; fighter.TryThrow(); }
            if (!g)
            {
                if (dropHeldTime > 0f && !dropHandled) fighter.DropAtFeet();
                dropHeldTime = 0f;
                dropHandled = false;
            }
            fighter.SetCharge(GameInput.HeavyAttackHeld);
            fighter.SetBlock(GameInput.BlockHeld);
            fighter.SetCrouch(GameInput.CrouchHeld);

            NearbyWeapon = Weapon.FindNearestGround(transform.position, Fighter.PickupRadius);
            bool picked = NearbyWeapon != null && GameInput.InteractPressed && fighter.TryPickup(NearbyWeapon);
            // Gamepad A is both pick up and jump: picking up wins.
            if (!picked && GameInput.JumpPressed) fighter.TryJump(move);
            if (GameInput.DodgePressed) fighter.TryDodge(move);
        }

        Fighter ClosestEnemy(float range)
        {
            Fighter best = null;
            float bestD = range * range;
            foreach (var f in Fighter.All)
            {
                if (f == fighter || !f.IsAlive || f.TeamId == fighter.TeamId) continue;
                float d = (f.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = f; }
            }
            return best;
        }
    }
}
