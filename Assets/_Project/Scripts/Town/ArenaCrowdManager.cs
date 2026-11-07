using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The town comes to the games. When a fight starts, asks the <see cref="TownSimulation"/> who is up and about at
    /// that hour, picks 30-60 of them (those who adore or hate you are keener to come), and seats them in the front rows
    /// of the stands as real townsfolk (same face and clothes as in the streets) among the painted crowd. They follow the
    /// fight with their heads; fans stand up and cheer when you land a blow or a rival falls, haters provoke you when you
    /// get hit and celebrate if you fall. At the end the town remembers: a win raises what the spectators think of you,
    /// a defeat lowers it — that is your reputation in the town.
    /// </summary>
    public class ArenaCrowdManager : MonoBehaviour
    {
        public struct Seat
        {
            public int Tier;
            public float Angle;      // radians around the arena (x = cos, z = sin)
            public Vector3 Position; // feet on the step
            public Quaternion Facing;
            public CitizenData Citizen;
        }

        public const int MinSpectators = 30, MaxSpectators = 60;
        // stand geometry (ArenaBootstrap): first step at R + 0.7, steps 1.1 high and 1.6 deep, starting at 3 m
        const float FirstStepIn = 0.7f, StepH = 1.1f, StepD = 1.6f, BaseY = 3f;

        /// <summary>Seats chosen for this fight (planned before the stands are built, so the painted crowd leaves them free).</summary>
        public static List<Seat> Planned { get; private set; } = new List<Seat>();
        public static ArenaCrowdManager Instance { get; private set; }

        readonly List<(Fighter body, CitizenData c)> spectators = new List<(Fighter, CitizenData)>();
        readonly List<(Fighter body, float at, int kind)> queue = new List<(Fighter, float, int)>();
        ArenaManager manager;
        GameHUD hud;
        TownSimulation sim;
        bool settled;
        int cheers, boos, kills;
        float fpsTime; int fpsFrames;

        public int Seated => spectators.Count;
        public int Cheers => cheers;
        public int Boos => boos;

        // ------------------------------------------------------------------ planning (before the stands are built)
        /// <summary>Who comes and where they sit. Deterministic for a day and hour (same fight, same crowd).</summary>
        public static List<Seat> Plan(float arenaRadius)
        {
            Planned = new List<Seat>();
            var sim = TownSimulation.Instance;
            if (sim == null || sim.Citizens.Count == 0) return Planned;
            var rng = new System.Random(sim.Day * 7919 + Mathf.FloorToInt(sim.Hour) * 31 + 5);
            float hour = sim.Hour;

            // who is up and about (not asleep); fans and haters are keener to come
            var awake = new List<(CitizenData c, double key)>();
            var asleep = new List<CitizenData>();
            foreach (var c in sim.Citizens)
            {
                var l = sim.WhereIs(c, hour);
                if (l.Activity == Activity.Dormir) { asleep.Add(c); continue; }
                double keen = 1.0 + Mathf.Abs(c.Relation) / 25.0 + (l.Activity == Activity.Trabajar ? -0.6 : 0.0);
                awake.Add((c, rng.NextDouble() * keen));
            }
            awake.Sort((a, b) => b.key.CompareTo(a.key));
            int n = Mathf.Clamp(awake.Count, MinSpectators, MaxSpectators);
            var chosen = new List<CitizenData>();
            foreach (var a in awake) { if (chosen.Count >= n) break; chosen.Add(a.c); }
            foreach (var c in asleep) { if (chosen.Count >= MinSpectators) break; chosen.Add(c); } // a quiet hour: the keen wake up

            // seats: the three front rows, mostly on the far (north) side the camera looks at, avoiding the gates
            var free = new List<(int tier, float ang)>();
            for (int tier = 0; tier < 3; tier++)
            {
                float radius = arenaRadius + FirstStepIn + StepD * tier + 0.8f;
                float step = 1.05f / radius; // ~1 m apart
                for (float a = 0.02f; a < Mathf.PI * 2f; a += step)
                {
                    float deg = a * Mathf.Rad2Deg;
                    if (Mathf.Abs(deg - 90f) < 9f || Mathf.Abs(deg - 270f) < 9f) continue; // gates
                    bool north = deg > 8f && deg < 172f;
                    if (!north && rng.NextDouble() < 0.8) continue;                        // a few on the near side too
                    free.Add((tier, a));
                }
            }
            // shuffle, then fill
            for (int i = free.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (free[i], free[j]) = (free[j], free[i]); }
            for (int i = 0; i < chosen.Count && i < free.Count; i++)
            {
                var (tier, ang) = free[i];
                float radius = arenaRadius + FirstStepIn + StepD * tier + 0.8f;
                Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Planned.Add(new Seat
                {
                    Tier = tier, Angle = ang, Citizen = chosen[i],
                    Position = d * radius + Vector3.up * (BaseY + StepH * (tier + 1)),
                    Facing = Quaternion.LookRotation(-d),
                });
            }
            return Planned;
        }

        public static void ClearPlan() { Planned = new List<Seat>(); }

        /// <summary>The painted crowd leaves a gap where a real townsperson sits.</summary>
        public static bool Reserved(int tier, float angle, float radius)
        {
            foreach (var s in Planned)
            {
                if (s.Tier != tier) continue;
                float da = Mathf.Abs(Mathf.DeltaAngle(s.Angle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                if (da * radius < 0.85f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the fight
        public void Init(ArenaManager arenaManager, GameHUD gameHud)
        {
            Instance = this;
            manager = arenaManager;
            hud = gameHud;
            sim = TownSimulation.Instance;
            float t0 = Time.realtimeSinceStartup;
            foreach (var seat in Planned)
            {
                var c = seat.Citizen;
                var f = CharacterFactory.Create(c.Name, CitizenSpawner.Look(c), seat.Position, seat.Facing, CitizenSpawner.CitizenTeam);
                f.Spectator = true;
                f.Harmless = true;
                f.Controller.enabled = false; // sits on the stone, no physics
                f.transform.localScale = Vector3.one * (c.Age < 16 ? 0.72f : 1f);
                f.SetCrouch(true);            // seated
                f.transform.SetParent(transform, true);
                spectators.Add((f, c));
            }
            Fighter.AnyHitResolved += OnHit;
            Fighter.AnyDied += OnDied;
            int fans = 0, haters = 0;
            foreach (var s in spectators) { if (s.c.Relation >= 25) fans++; else if (s.c.Relation <= -25) haters++; }
            Debug.Log($"[CROWD] seated={spectators.Count} fans={fans} haters={haters} hour={TownSimulation.Clock(sim != null ? sim.Hour : 0f)} built in {Time.realtimeSinceStartup - t0:0.00}s");
            if (hud != null && spectators.Count > 0)
                hud.ShowMessage($"EN LA GRADA: {spectators.Count} VECINOS DEL PUEBLO  ({fans} TE ADORAN, {haters} TE ODIAN)", 4f);
        }

        void OnDestroy()
        {
            Fighter.AnyHitResolved -= OnHit;
            Fighter.AnyDied -= OnDied;
            if (Instance == this) Instance = null;
        }

        Fighter Player => manager != null ? manager.Player : null;

        void OnHit(HitResult r)
        {
            if (settled || r.Outcome == HitOutcome.Blocked) return;
            if (r.Attacker == Player && r.Target != Player)
                React(c => c.Relation >= 25 ? 0.45f : c.Relation > -25 ? 0.08f : 0f, 2);   // fans cheer your blows
            else if (r.Target == Player)
                React(c => c.Relation <= -25 ? 0.45f : 0f, 0);                              // haters jeer when you are hit
        }

        void OnDied(Fighter f)
        {
            if (settled || f == null || f.Spectator) return;
            if (f == Player)
            {
                React(c => c.Relation <= -25 ? 0.95f : c.Relation < 25 ? 0.25f : 0f, 2);    // your enemies celebrate
                return;
            }
            if (manager != null && manager.Enemies.Contains(f))
            {
                kills++;
                React(c => c.Relation >= 25 ? 0.95f : c.Relation > -25 ? 0.45f : 0f, 2);    // a rival falls: the stands rise
                React(c => c.Relation <= -25 ? 0.4f : 0f, 0);
            }
        }

        /// <summary>Each spectator reacts with some chance (from what they think of you), with a short random delay.</summary>
        void React(System.Func<CitizenData, float> chance, int kind)
        {
            foreach (var s in spectators)
                if (Random.value < chance(s.c)) queue.Add((s.body, Time.time + Random.Range(0.05f, 0.7f), kind));
        }

        void Update()
        {
            fpsTime += Time.unscaledDeltaTime; fpsFrames++;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (Time.time < queue[i].at) continue;
                var q = queue[i];
                queue.RemoveAt(i);
                if (q.body == null) continue;
                if (q.body.TryTaunt(q.kind)) { if (q.kind == 2) cheers++; else boos++; } // stands up for it, then sits again
            }
            if (!settled && manager != null && manager.MatchOver) Settle();
        }

        /// <summary>The fight is over: the town remembers it.</summary>
        void Settle()
        {
            settled = true;
            if (sim == null || spectators.Count == 0) return;
            bool won = manager.PlayerWon;
            float before = sim.Reputation;
            foreach (var s in spectators)
            {
                int d = won ? 4 + (s.c.Relation >= 25 ? 2 : 0) : -3 - (s.c.Relation <= -25 ? 2 : 0);
                d += kills; // every rival you bring down impresses them a little
                sim.ChangeRelation(s.c, d);
            }
            float after = sim.Reputation;
            Debug.Log($"[CROWD] end won={won} kills={kills} cheers={cheers} boos={boos} reputation {before:0.0}->{after:0.0} fps~{fpsFrames / Mathf.Max(0.01f, fpsTime):0}");
            if (hud != null)
                hud.ShowMessage($"LA GRADA: {cheers} VITORES, {boos} ABUCHEOS   -   TU FAMA EN EL PUEBLO {(after >= before ? "SUBE" : "BAJA")} ({after - before:+0.0;-0.0})", 5f);
        }
    }
}
