using System.Collections.Generic;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The opening hours of the craftsmen who sell in the street (armourers, carpenters): at their post from 7:00 to
    /// 19:00. At closing time they walk to the door of their house and go in; in the morning they come out and walk
    /// back to the post. The armourers hammer away at their work while nobody is being served.
    /// </summary>
    public class ShopKeepers : MonoBehaviour
    {
        public const float OpenHour = 7f, CloseHour = 19f;
        public static bool OpenAt(float hour) => hour >= OpenHour && hour < CloseHour;

        public class Keeper
        {
            public Fighter F;
            public Behaviour Brain;
            public Vector3 Post, Door;
            public Quaternion Rot;
            public bool Hammers;
            public string Trade;
            /// <summary>At the post and selling.</summary>
            public bool AtPost;
            public bool Inside;
            public float Timer, Clang = -1f, Walking;
        }

        public readonly List<Keeper> All = new List<Keeper>();
        Fighter player;

        public void Init(Fighter p) { player = p; }

        public Keeper Add(Fighter f, Behaviour brain, Vector3 post, Quaternion rot, Vector3 door, bool hammers, string trade)
        {
            var k = new Keeper { F = f, Brain = brain, Post = post, Rot = rot, Door = door, Hammers = hammers, Trade = trade, AtPost = true };
            All.Add(k);
            // the town may wake up at night: then he is at home already
            var sim = TownSimulation.Instance;
            if (sim != null && !OpenAt(sim.Hour)) { k.AtPost = false; k.Inside = true; if (brain != null) brain.enabled = false; f.gameObject.SetActive(false); }
            return k;
        }

        /// <summary>The keeper whose post is within a range of a point (there or not).</summary>
        public Keeper Near(Vector3 p, float range)
        {
            Keeper best = null; float bd = range;
            foreach (var k in All) { Vector3 d = k.Post - p; d.y = 0f; if (d.magnitude < bd) { bd = d.magnitude; best = k; } }
            return best;
        }

        static void Teleport(Fighter f, Vector3 at, Quaternion rot)
        {
            if (f.Controller != null) f.Controller.enabled = false;
            f.transform.SetPositionAndRotation(at + Vector3.up * 0.05f, rot);
            if (f.Controller != null) f.Controller.enabled = true;
        }

        void Update()
        {
            var sim = TownSimulation.Instance;
            if (sim == null) return;
            bool open = OpenAt(sim.Hour);
            float dt = Time.deltaTime;
            foreach (var k in All)
            {
                if (k.F == null) continue;
                if (open)
                {
                    if (k.AtPost) { Hammer(k, dt); continue; }
                    if (k.Inside)
                    {
                        // opening time: out of the door
                        k.Inside = false;
                        k.Walking = 0f;
                        k.F.gameObject.SetActive(true);
                        Teleport(k.F, k.Door, k.Rot);
                    }
                    if (Walk(k, k.Post, dt))
                    {
                        Teleport(k.F, k.Post, k.Rot);
                        k.F.SetFacing(k.Rot * Vector3.forward);
                        k.F.Move(Vector3.zero);
                        k.AtPost = true;
                        if (k.Brain != null) k.Brain.enabled = true;
                    }
                }
                else
                {
                    if (k.Inside) continue;
                    if (k.AtPost) { k.AtPost = false; k.Walking = 0f; if (k.Brain != null) k.Brain.enabled = false; }
                    if (Walk(k, k.Door, dt))
                    {
                        k.F.Move(Vector3.zero);
                        k.Inside = true;
                        k.F.gameObject.SetActive(false); // through the door: home until the morning
                    }
                }
            }
        }

        /// <summary>A step towards a point; true once there (or after too long: he is put there).</summary>
        bool Walk(Keeper k, Vector3 to, float dt)
        {
            if (!k.F.gameObject.activeSelf) return true;
            if (k.F.State != FighterState.Idle) return false;
            Vector3 d = to - k.F.transform.position; d.y = 0f;
            k.Walking += dt;
            if (d.magnitude < 0.4f || k.Walking > 12f) return true;
            k.F.Move(d.normalized * 0.31f);
            k.F.SetFacing(d);
            return false;
        }

        // at the post with nobody near: a blow of the hammer every few seconds, and its ring on the iron
        void Hammer(Keeper k, float dt)
        {
            if (!k.Hammers || k.F.State != FighterState.Idle && k.Clang < 0f) return;
            if (k.Clang >= 0f)
            {
                if ((k.Clang -= dt) < 0f)
                {
                    var am = Gladiator.Audio.AudioManager.Instance;
                    Vector3 pp = player != null ? player.transform.position : k.Post;
                    if (am != null && (pp - k.Post).sqrMagnitude < 30f * 30f) am.Play("block", k.Post + Vector3.up, 0.45f, 0.08f, 1.35f);
                }
                return;
            }
            bool busy = player != null && (player.transform.position - k.Post).sqrMagnitude < 3.6f * 3.6f;
            if (busy) { k.Timer = 1.5f; return; }
            if ((k.Timer -= dt) > 0f) return;
            k.Timer = Random.Range(2.2f, 3.4f);
            k.F.SetFacing(k.Rot * Vector3.forward);
            if (k.F.TryAttack(AttackType.Light)) k.Clang = 0.28f;
        }
    }
}
