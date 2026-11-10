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

        // T tapped: a quick provocation. T held: the taunt wheel (Gladiator.UI.TauntWheel).
        const float WheelDelay = 0.25f;
        float tauntHeldTime;
        Vector2 wheelMouse;
        Gladiator.UI.TauntWheel wheel;
        /// <summary>True while the taunt wheel is open (movement and attacks are paused).</summary>
        public bool TauntWheelOpen => wheel != null && wheel.IsOpen;

        void Awake() { fighter = GetComponent<Fighter>(); }
        void OnDisable() { NearbyWeapon = null; if (wheel != null) wheel.Close(); GameInput.WheelOpen = false; tauntHeldTime = 0f; }
        void OnDestroy() { if (wheel != null) Destroy(wheel.gameObject); GameInput.WheelOpen = false; }

        /// <summary>Returns true while the wheel takes the input this frame.</summary>
        bool UpdateTauntWheel()
        {
            bool held = GameInput.TauntHeld;
            if (held)
            {
                tauntHeldTime += Time.unscaledDeltaTime;
                if (tauntHeldTime >= WheelDelay && !TauntWheelOpen)
                {
                    if (wheel == null) wheel = Gladiator.UI.TauntWheel.Create();
                    wheelMouse = Vector2.zero;
                    wheel.Open();
                    GameInput.WheelOpen = true;
                }
            }
            if (TauntWheelOpen)
            {
                int sel = wheel.Selected;
                int bySticks = Gladiator.UI.TauntWheel.SlotFor(GameInput.Move, 0.5f);
                wheelMouse = Vector2.ClampMagnitude(wheelMouse + GameInput.WheelMouseDelta, 120f);
                int byMouse = Gladiator.UI.TauntWheel.SlotFor(wheelMouse, 40f);
                if (bySticks >= 0) sel = bySticks;
                else if (byMouse >= 0) sel = byMouse;
                for (int d = 1; d <= 4; d++) if (GameInput.DigitPressed(d)) sel = d - 1;
                if (sel != wheel.Selected) wheel.SetSelected(sel);
            }
            if (!held && tauntHeldTime > 0f)
            {
                if (TauntWheelOpen)
                {
                    int sel = wheel.Selected;
                    wheel.Close();
                    GameInput.WheelOpen = false;
                    if (sel >= 0) fighter.TryTaunt(Gladiator.UI.TauntWheel.Kinds[sel]);
                }
                else fighter.TryTaunt(Random.Range(0, 2)); // tap: beat the chest or beckon
                tauntHeldTime = 0f;
                return true;
            }
            return TauntWheelOpen;
        }

        void Update()
        {
            GameInput.UpdateDeviceMode();
            NearbyWeapon = null;
            if (!fighter.IsAlive || AimCamera == null)
            {
                if (TauntWheelOpen) { wheel.Close(); GameInput.WheelOpen = false; }
                tauntHeldTime = 0f;
                return;
            }
            if (UpdateTauntWheel()) { fighter.Move(Vector3.zero); return; }

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
