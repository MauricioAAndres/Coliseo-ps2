using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// A flying arrow: moves with light gravity, sweeps a ray each frame and hits the first fighter (not the
    /// shooter's team) or sticks in whatever it struck. Dodging fighters (i-frames) let it pass; a raised guard
    /// facing it blocks it like a light blow. Infinite ammo: the bow just spawns a new one.
    /// </summary>
    public class Arrow : MonoBehaviour
    {
        Fighter shooter;
        Vector3 velocity;
        float damage, life, stuckTime;
        AttackType type;
        bool stuck;
        const float Gravity = 3.5f, MaxLife = 4f;

        public static Arrow Fire(Fighter shooter, Vector3 from, Vector3 dir, float speed, float damage, AttackType type)
        {
            var go = WeaponFactory.ArrowMesh();
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
            var a = go.AddComponent<Arrow>();
            a.shooter = shooter;
            a.velocity = dir.normalized * speed;
            a.damage = damage;
            a.type = type;
            return a;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (stuck)
            {
                stuckTime += dt;
                if (stuckTime > 5f) Destroy(gameObject);
                return;
            }
            life += dt;
            if (life > MaxLife) { Destroy(gameObject); return; }

            velocity += Vector3.down * Gravity * dt;
            Vector3 from = transform.position, step = velocity * dt;
            var hits = Physics.RaycastAll(from, step.normalized, step.magnitude + 0.05f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var h in hits)
            {
                var f = h.collider.GetComponentInParent<Fighter>();
                if (f != null)
                {
                    if (f == shooter || !f.IsAlive || (shooter != null && f.TeamId == shooter.TeamId)) continue;
                    if (f.Invulnerable) continue; // rolled through it
                    Vector3 dir = velocity; dir.y = 0f;
                    f.ReceiveHit(shooter, damage, type, dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward);
                    Destroy(gameObject);
                    return;
                }
                if (h.collider.GetComponentInParent<Weapon>() != null) continue; // weapons lying around
                // Stuck in the sand / a wall / a block.
                transform.position = h.point - step.normalized * 0.12f;
                stuck = true;
                return;
            }
            transform.SetPositionAndRotation(from + step, Quaternion.LookRotation(velocity));
        }
    }
}
