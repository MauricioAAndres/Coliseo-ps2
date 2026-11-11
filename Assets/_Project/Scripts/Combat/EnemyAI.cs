using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// Simple gladiator AI using the same Fighter API as the player:
    /// approach -> strafe at weapon range -> attack on cooldown, reactive blocking with a
    /// human-ish delay, and if disarmed it runs for the nearest weapon on the ground.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class EnemyAI : MonoBehaviour
    {
        [Range(0.1f, 2f)] public float Aggression = 0.9f;
        [Range(0f, 1f)] public float BlockChance = 0.4f;
        [Range(0f, 1f)] public float HeavyChance = 0.3f;
        [Range(0f, 1f)] public float DodgeChance = 0.15f;
        [Range(0f, 1f)] public float DuckChance = 0.12f;
        [Range(0f, 1f)] public float KickChance = 0.12f;
        public float WeaponSearchRadius = 9f;

        static readonly System.Collections.Generic.List<EnemyAI> all = new System.Collections.Generic.List<EnemyAI>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { all.Clear(); }

        /// <summary>Fighter this AI is currently going after (used to spread targets in free-for-all).</summary>
        public Fighter Target { get; private set; }

        Fighter self;
        float strafeSign = 1f, decisionTimer, attackCooldown = 1.2f;
        float blockStart = -1f, blockEnd = -1f, dodgeAt = -1f, duckStart = -1f, duckEnd = -1f, traverseCheck, chargeUntil = -1f;
        float tauntCheck = 4f, celebrateAt = -1f;
        int comboStep;           // light-light-heavy disarm combo in progress (1, 2)
        bool comboStarting;
        [Range(0f, 1f)] public float ComboChance = 0.14f;
        [Range(0f, 1f)] public float PowerChargeChance = 0.05f;
        [Range(0f, 1f)] public float TauntChance = 0.35f;
        Vector3 dodgeWay;

        void Awake() { self = GetComponent<Fighter>(); }
        void OnEnable() { Fighter.AnyAttackStarted += OnAttackStarted; Fighter.AnyDied += OnSomeoneDied; all.Add(this); }
        void OnDisable() { Fighter.AnyAttackStarted -= OnAttackStarted; Fighter.AnyDied -= OnSomeoneDied; all.Remove(this); Target = null; }

        /// <summary>Brought someone down: celebrate a moment later.</summary>
        void OnSomeoneDied(Fighter f)
        {
            if (self != null && f != self && f.LastAttacker == self) celebrateAt = Time.time + Random.Range(0.4f, 0.9f);
        }

        void OnAttackStarted(Fighter attacker, AttackType type)
        {
            if (attacker == self || self == null || !self.IsAlive || attacker.TeamId == self.TeamId) return;
            if (Flat(attacker.transform.position - transform.position).magnitude > attacker.ActiveStats.Reach + 2f) return;
            // Reaction: roll away, duck under a horizontal slash, or raise the guard.
            float r = Random.value;
            if (r < DodgeChance * (type == AttackType.Kick ? 2.5f : 1f))
            {
                dodgeAt = Time.time + Random.Range(0.04f, 0.14f);
                Vector3 away = Flat(transform.position - attacker.transform.position).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? -1f : 1f);
                dodgeWay = Random.value < 0.35f ? Vector3.zero : (side + away * 0.4f).normalized;
                return;
            }
            if (type == AttackType.Light && r < DodgeChance + DuckChance)
            {
                duckStart = Time.time + Random.Range(0.03f, 0.1f);
                duckEnd = duckStart + 0.5f;
                return;
            }
            if (Random.value > BlockChance) return;
            blockStart = Time.time + Random.Range(0.05f, 0.18f);
            blockEnd = blockStart + (type == AttackType.Heavy ? 0.9f : 0.55f);
            // Counter-attack right after the guard drops (punishes a bounced light attack).
            attackCooldown = Mathf.Min(attackCooldown, blockEnd - Time.time + 0.05f);
        }

        void Update()
        {
            if (!self.IsAlive) return;
            float dt = Time.deltaTime;
            if (dodgeAt > 0f && Time.time >= dodgeAt) { dodgeAt = -1f; self.TryDodge(dodgeWay); }
            if (celebrateAt > 0f && Time.time >= celebrateAt) { celebrateAt = -1f; self.TryTaunt(2); }
            if (self.State == FighterState.Taunt) { self.Move(Vector3.zero); return; }
            self.SetCrouch(Time.time >= duckStart && Time.time <= duckEnd);

            Fighter target = Target = FindTarget();
            bool wantBlock = Time.time >= blockStart && Time.time <= blockEnd;
            if (target == null) { self.SetBlock(false); return; }

            // Disarmed: go grab something.
            if (self.CurrentWeapon == null && !wantBlock)
            {
                Weapon w = Weapon.FindNearestGround(transform.position, WeaponSearchRadius);
                if (w != null)
                {
                    self.SetBlock(false);
                    Vector3 tw = Flat(w.transform.position - transform.position);
                    if (tw.magnitude <= Fighter.PickupRadius) self.TryPickup(w);
                    else { self.Move(tw.normalized); self.SetFacing(tw); Traverse(tw); }
                    return;
                }
            }

            Vector3 to = Flat(target.transform.position - transform.position);
            float dist = to.magnitude;
            Vector3 dir = dist > 0.001f ? to / dist : transform.forward;
            self.SetFacing(dir);
            self.SetBlock(wantBlock);
            if (self.ActiveStats.IsBow) { Archer(target, dist, dir, dt); return; }
            self.SetCharge(Time.time < chargeUntil); // charged heavy (held for 2 s)

            float reach = self.ActiveStats.Reach;
            float ideal = reach * 0.75f + self.Radius + target.Radius;

            // At a safe distance, now and then: taunt (beat the chest / beckon), then go back after the target.
            tauntCheck -= dt;
            if (tauntCheck <= 0f)
            {
                tauntCheck = Random.Range(5f, 10f);
                if (dist > 4.5f && dist < 10f && !wantBlock && Random.value < TauntChance && self.TryTaunt(Random.Range(0, 2))) return;
            }

            decisionTimer -= dt;
            if (decisionTimer <= 0f)
            {
                decisionTimer = Random.Range(0.6f, 1.6f);
                strafeSign = Random.value < 0.5f ? -1f : 1f;
            }

            Vector3 side = Vector3.Cross(Vector3.up, dir) * strafeSign;
            Vector3 move;
            float above = target.transform.position.y - transform.position.y;
            if (above > 1f) move = dir; // target up on a platform: walk at it (and climb)
            else if (dist > ideal + 0.6f) move = dir + side * 0.3f;
            else if (dist < ideal - 0.6f) move = -dir * 0.6f;
            else move = side * 0.5f;
            if (wantBlock) move *= 0.5f;
            self.Move(move);
            Traverse(move);

            attackCooldown -= dt;
            float edge = dist - self.Radius - target.Radius;
            if (!wantBlock && attackCooldown <= 0f && edge <= reach + 0.2f && Vector3.Angle(transform.forward, dir) < 30f)
            {
                // Kick an opponent hiding behind the guard (it breaks it), now and then anyway.
                var type = Random.value < (target.IsStunned ? 0.8f : HeavyChance) ? AttackType.Heavy : AttackType.Light;
                // Disarm combo (light, light, heavy) or a charged heavy now and then.
                if (comboStep == 1) type = AttackType.Light;
                else if (comboStep == 2) type = AttackType.Heavy;
                else if (target.CurrentWeapon != null && Random.value < ComboChance) { comboStep = 0; type = AttackType.Light; comboStarting = true; }
                bool charge = comboStep == 0 && !comboStarting && type == AttackType.Heavy && target.CurrentWeapon != null && Random.value < PowerChargeChance;
                if (!target.IsStunned && edge <= self.KickReach + 0.1f && Random.value < (target.State == FighterState.Blocking ? 0.6f : KickChance))
                    type = AttackType.Kick;
                if (self.TryAttack(type))
                {
                    if (comboStarting) { comboStep = 1; comboStarting = false; attackCooldown = 0.12f; }
                    else if (comboStep == 1) { comboStep = 2; attackCooldown = 0.12f; }
                    else if (comboStep == 2) { comboStep = 0; attackCooldown = Random.Range(0.8f, 1.5f) / Mathf.Max(0.1f, Aggression); }
                    else attackCooldown = Random.Range(0.5f, 1.4f) / Mathf.Max(0.1f, Aggression);
                    if (charge) chargeUntil = Time.time + Fighter.PowerChargeTime + Random.Range(0.1f, 0.4f);
                }
            }
        }

        /// <summary>
        /// Bow: keep 7-12 m away (back off and strafe), kick anyone who gets too close, shoot when lined up:
        /// quick shots or a charged one held for a random time.
        /// </summary>
        void Archer(Fighter target, float dist, Vector3 dir, float dt)
        {
            decisionTimer -= dt;
            if (decisionTimer <= 0f) { decisionTimer = Random.Range(0.8f, 1.8f); strafeSign = Random.value < 0.5f ? -1f : 1f; }
            Vector3 side = Vector3.Cross(Vector3.up, dir) * strafeSign;
            Vector3 move = dist < 6f ? (-dir + side * 0.5f).normalized : dist > 12f ? (dir + side * 0.3f).normalized : side * 0.6f;
            if (self.Charging) move *= 0.3f;
            self.Move(move);
            Traverse(move);
            self.SetCharge(Time.time < chargeUntil);

            attackCooldown -= dt;
            float edge = dist - self.Radius - target.Radius;
            if (edge <= self.KickReach + 0.2f && attackCooldown <= 0f && self.TryAttack(AttackType.Kick)) { attackCooldown = 0.8f; return; }
            if (attackCooldown <= 0f && dist < 30f && Vector3.Angle(transform.forward, dir) < 10f)
            {
                bool charged = Random.value < 0.45f;
                if (self.TryAttack(charged ? AttackType.Heavy : AttackType.Light))
                {
                    if (charged) chargeUntil = Time.time + Random.Range(0.4f, 1.1f);
                    attackCooldown = Random.Range(0.6f, 1.5f) / Mathf.Max(0.1f, Aggression);
                }
            }
        }

        /// <summary>Walking into a block or platform: vault onto it / climb it (jumping off is just walking off).</summary>
        void Traverse(Vector3 move)
        {
            if ((traverseCheck -= Time.deltaTime) > 0f || move.sqrMagnitude < 0.09f) return;
            traverseCheck = 0.2f;
            Vector3 d = Flat(move).normalized;
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, d, out RaycastHit hit, self.Radius + 0.45f, Ragdoll.WorldMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Fighter>() == null)
                self.TryTraverse(d);
        }

        Fighter FindTarget()
        {
            Fighter best = null;
            float bestD = float.MaxValue;
            foreach (var f in Fighter.All)
            {
                if (f == self || !f.IsAlive || f.TeamId == self.TeamId) continue;
                // Distance, plus a penalty per other AI already chasing that fighter (avoids 3-on-1 dogpiles),
                // with a little stickiness so targets don't flip every frame.
                float d = Vector3.Distance(f.transform.position, transform.position);
                int chasers = 0;
                foreach (var ai in all) if (ai != this && ai.Target == f) chasers++;
                d += chasers * 4f;
                if (f == Target) d -= 1.5f;
                if (d < bestD) { bestD = d; best = f; }
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
