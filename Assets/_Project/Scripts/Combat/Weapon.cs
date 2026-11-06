using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// A physical weapon. Either held (parented to a fighter's hand socket, physics off)
    /// or lying on the ground as a pickup (rigidbody on, registered in GroundWeapons).
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        static readonly List<Weapon> ground = new List<Weapon>();
        public static IReadOnlyList<Weapon> GroundWeapons => ground;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ground.Clear(); }

        public WeaponStats Stats { get; private set; }
        public Fighter Holder { get; private set; }
        public bool OnGround => Holder == null;
        public bool CanBePickedUp => Holder == null && Time.time >= pickupBlockedUntil;

        Rigidbody body;
        Collider col;
        float pickupBlockedUntil;
        Vector3 safeSpawn;

        public void Init(WeaponStats stats, Rigidbody rb, Collider c)
        {
            Stats = stats;
            body = rb;
            col = c;
            SetPhysics(false);
        }

        void SetPhysics(bool on)
        {
            body.isKinematic = !on;
            body.detectCollisions = on;
            col.enabled = on;
            body.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }

        public void AttachTo(Fighter fighter, Transform socket)
        {
            Holder = fighter;
            ground.Remove(this);
            SetPhysics(false);
            transform.SetParent(socket, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        public void Drop(Vector3 velocity)
        {
            Holder = null;
            transform.SetParent(null, true);
            SetPhysics(true);
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 10f;
            pickupBlockedUntil = Time.time + 0.6f;
            safeSpawn = new Vector3(transform.position.x * 0.5f, 1f, transform.position.z * 0.5f);
            // Weapons on the ground never block fighters.
            foreach (var f in Fighter.All)
                if (f != null && f.Controller != null) Physics.IgnoreCollision(col, f.Controller, true);
            if (!ground.Contains(this)) ground.Add(this);
        }

        /// <summary>Teleports a ground weapon (used by the auto-test and spawners).</summary>
        public void PlaceAt(Vector3 position)
        {
            if (!OnGround) return;
            body.position = position;
            transform.position = position;
            body.linearVelocity = Vector3.zero;
        }

        Fighter thrownBy;
        float thrownUntil, thrownDamage;

        /// <summary>Thrown by a fighter: flies spinning and hurts the first rival it touches (once), then lies on the ground.</summary>
        public void Throw(Fighter by, Vector3 velocity, float damage)
        {
            body.position = transform.position;
            body.linearVelocity = velocity;
            body.angularVelocity = by.transform.right * 18f;
            thrownBy = by;
            thrownDamage = damage;
            thrownUntil = Time.time + 1.6f;
            pickupBlockedUntil = Time.time + 1f;
        }

        void Update()
        {
            // Safety net: anything that escapes the arena comes back.
            if (OnGround && transform.position.y < -5f) PlaceAt(safeSpawn);
            if (thrownBy == null) return;
            if (Time.time > thrownUntil) { thrownBy = null; return; }
            var hits = Physics.OverlapSphere(col.bounds.center, Mathf.Max(0.35f, Stats.Length * 0.35f), ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                var f = h.GetComponentInParent<Fighter>();
                if (f == null || f == thrownBy || !f.IsAlive || f.TeamId == thrownBy.TeamId) continue;
                Vector3 dir = body.linearVelocity; dir.y = 0f;
                f.ReceiveHit(thrownBy, thrownDamage, AttackType.Light, dir.sqrMagnitude > 0.01f ? dir.normalized : thrownBy.transform.forward);
                body.linearVelocity *= 0.25f;
                thrownBy = null;
                break;
            }
        }

        void OnDisable() { ground.Remove(this); }

        public static Weapon FindNearestGround(Vector3 pos, float maxDist)
        {
            Weapon best = null;
            float bestD = maxDist * maxDist;
            foreach (var w in ground)
            {
                if (w == null || !w.CanBePickedUp) continue;
                Vector3 d = w.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestD) { bestD = sq; best = w; }
            }
            return best;
        }
    }
}
