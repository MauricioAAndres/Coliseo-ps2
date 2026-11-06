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

        void Awake() { fighter = GetComponent<Fighter>(); }
        void OnDisable() { NearbyWeapon = null; }

        void Update()
        {
            GameInput.UpdateDeviceMode();
            NearbyWeapon = null;
            if (!fighter.IsAlive || AimCamera == null) return;

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
            if (GameInput.TauntPressed) fighter.TryTaunt(Random.Range(0, 3));

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
