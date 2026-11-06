using System.Collections;
using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.PS2Style;
using Gladiator.Progression;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Training
{
    /// <summary>
    /// "Los Proyectiles" (Agility): a wooden launcher lobs slow clay balls down three lanes.
    /// The lane about to be hit is telegraphed in red; step left/right to the free lane.
    /// Later throws come faster and sometimes two at once. Score = dodged / thrown.
    /// </summary>
    public class DodgeMinigame : TrainingMinigame
    {
        public int Throws = 10;
        public float LaneWidth = 1.6f;
        public float LauncherDistance = 11f;

        public override string Id => "esquiva";
        public override string Title => "PROYECTILES  -  AGILIDAD";
        public override string Instructions => "Esquiva al carril libre   (A / D  -  flechas  -  stick / cruceta)";
        public override StatType Stat => StatType.Agility;

        Vector3 right;
        Transform launcherArm;
        readonly GameObject[] laneWarn = new GameObject[3];
        int lane = 1; // 0 = left, 1 = centre, 2 = right
        float savedSpeed;

        class Ball { public Transform T, Shadow; public int Lane; public float Z, Speed; public bool Resolved; }
        readonly List<Ball> balls = new List<Ball>();
        int dodged, thrown;

        public override void Build()
        {
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            Material darkWood = PS2Materials.Get("wood_dark", PS2Textures.Wood, new Color(0.6f, 0.5f, 0.42f));
            Material iron = PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f));
            Material laneMat = PS2Materials.Get("lane", PS2Textures.Sand, new Color(0.72f, 0.62f, 0.5f));
            Material warn = PS2Materials.Flat(new Color(0.9f, 0.15f, 0.08f), 0.5f);

            PlayerFacing = transform.forward;
            right = transform.right;
            PlayerSpot = transform.position;
            CameraFocus = transform.position + transform.forward * 1.5f;
            CameraDistance = 9.5f;
            CameraPitch = 36f;

            // Lanes (darker raked strips) + red warning plates near the launcher.
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * LaneWidth;
                PS2Spawn.Box(transform, "Lane" + i, new Vector3(LaneWidth * 0.8f, 0.02f, LauncherDistance - 1f),
                    new Vector3(x, 0.011f, LauncherDistance * 0.5f - 0.5f), laneMat, null, 0.5f);
                laneWarn[i] = PS2Spawn.Box(transform, "Warn" + i, new Vector3(LaneWidth * 0.7f, 0.03f, 1.2f), new Vector3(x, 0.03f, LauncherDistance - 2f), warn);
                laneWarn[i].SetActive(false);
            }

            // Launcher: a wooden frame with a throwing arm.
            var launcher = new GameObject("Launcher").transform;
            launcher.SetParent(transform, false);
            launcher.localPosition = new Vector3(0, 0, LauncherDistance);
            launcher.localRotation = Quaternion.Euler(0, 180f, 0);
            PS2Spawn.Box(launcher, "Base", new Vector3(3.6f, 0.3f, 1.4f), new Vector3(0, 0.15f, 0), darkWood);
            PS2Spawn.Box(launcher, "UprightL", new Vector3(0.22f, 1.6f, 0.22f), new Vector3(-0.9f, 1.0f, 0), wood);
            PS2Spawn.Box(launcher, "UprightR", new Vector3(0.22f, 1.6f, 0.22f), new Vector3(0.9f, 1.0f, 0), wood);
            PS2Spawn.Box(launcher, "Axle", new Vector3(2.0f, 0.12f, 0.12f), new Vector3(0, 1.6f, 0), iron);
            launcherArm = new GameObject("ArmPivot").transform;
            launcherArm.SetParent(launcher, false);
            launcherArm.localPosition = new Vector3(0, 1.6f, 0);
            PS2Spawn.Box(launcherArm, "Arm", new Vector3(0.16f, 0.16f, 1.8f), new Vector3(0, 0, -0.7f), wood);
            PS2Spawn.Box(launcherArm, "Cup", new Vector3(0.45f, 0.15f, 0.45f), new Vector3(0, 0.08f, -1.55f), darkWood);
            launcherArm.localRotation = Quaternion.Euler(-35f, 0, 0);
            var col = launcher.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 1f, 0); col.size = new Vector3(3.6f, 2f, 1.4f);

            LowPolyMesh.BakeAmbient(gameObject, TrainingBootstrap.AmbientSky, TrainingBootstrap.AmbientGround);
        }

        Vector3 LanePos(int i) => PlayerSpot + right * ((i - 1) * LaneWidth);

        /// <summary>
        /// Speed multiplier from the dodge streak: x1 for the first 5, x1.25 for the next 5, x1.5 at 10, then +0.25 every 3.
        /// A hit resets the streak (and the speed) to zero.
        /// </summary>
        public static float SpeedForStreak(int streak) =>
            streak < 5 ? 1f : streak < 10 ? 1.25f : 1.5f + 0.25f * ((streak - 10) / 3);

        public int Streak { get; private set; }
        public int BestStreak { get; private set; }

        protected override IEnumerator Run()
        {
            savedSpeed = player.MoveSpeed;
            player.MoveSpeed = 9f;
            lane = 1;
            dodged = 0; thrown = 0;
            Streak = 0; BestStreak = 0;
            balls.Clear();
            var mat = PS2Materials.Get("clay", PS2Textures.Sand, new Color(0.75f, 0.45f, 0.3f));
            Mesh ballMesh = LowPolyMesh.Sphere(0.28f, 7, 5);
            int total = FreeMode ? int.MaxValue : Throws;

            for (int i = 0; KeepGoing(i, total); i++)
            {
                float m = SpeedForStreak(Streak);
                float speed = Mathf.Min(30f, 8f * m);
                float telegraph = Mathf.Max(0.16f, 0.55f / m);
                bool doubleThrow = Streak >= 5 && Random.value < 0.4f;

                int a = Random.Range(0, 3);
                int b = doubleThrow ? (a + Random.Range(1, 3)) % 3 : -1;
                laneWarn[a].SetActive(true);
                if (b >= 0) laneWarn[b].SetActive(true);
                hud.SetCounter((FreeMode ? "LIBRE      " : "LANZAMIENTO " + (i + 1) + " / " + Throws + "      ") +
                               "RACHA " + Streak + "   VELOCIDAD x" + m.ToString("0.00") + "   MEJOR " + BestStreak + (FreeMode ? "      ESC: terminar" : ""));

                // Bot (autotest / training partners): reacts after a human-ish delay; worse when it is fast.
                float botReact = Random.Range(0.08f, Mathf.Max(0.1f, telegraph * 0.75f));
                bool botCorrect = Random.value < Mathf.Lerp(0.88f, 0.6f, Mathf.InverseLerp(1f, 2.5f, m));
                int botLane = lane;
                if (lane == a || lane == b)
                {
                    if (botCorrect) { for (int l = 0; l < 3; l++) if (l != a && l != b) { botLane = l; if (Mathf.Abs(l - lane) == 1) break; } }
                }

                for (float t = 0; t < telegraph; t += Time.deltaTime)
                {
                    launcherArm.localRotation = Quaternion.Euler(Mathf.Lerp(-35f, -60f, t / telegraph), 0, 0);
                    if (BotPlays && t >= botReact) lane = botLane;
                    if (FreeMode && StopRequested) break;
                    yield return Tick();
                }
                launcherArm.localRotation = Quaternion.Euler(40f, 0, 0);
                laneWarn[a].SetActive(false);
                if (b >= 0) laneWarn[b].SetActive(false);
                if (FreeMode && StopRequested) break;
                Launch(a, speed, ballMesh, mat);
                if (b >= 0) Launch(b, speed, ballMesh, mat);

                float gap = Mathf.Max(0.1f, 0.4f / m);
                while (AnyBallIncoming()) { if (BotPlays) lane = botLane; yield return Tick(); }
                for (float t = 0; t < gap; t += Time.deltaTime)
                {
                    launcherArm.localRotation = Quaternion.Slerp(launcherArm.localRotation, Quaternion.Euler(-35f, 0, 0), 0.2f);
                    yield return Tick();
                }
            }
            while (balls.Count > 0) yield return Tick();

            score = thrown > 0 ? dodged / (float)thrown : 0f;
            hud.SetCounter("ESQUIVADOS " + dodged + " / " + thrown + "      MEJOR RACHA " + BestStreak);
            yield return WaitWhileSteering(this, 0.8f);
        }

        void Launch(int laneIndex, float speed, Mesh mesh, Material mat)
        {
            var go = PS2Spawn.Mesh(null, "ClayBall", mesh, Vector3.zero, mat);
            var sh = PS2Spawn.Blob(null, 0.3f, Vector3.zero);
            balls.Add(new Ball { T = go.transform, Shadow = sh.transform, Lane = laneIndex, Z = LauncherDistance - 1.5f, Speed = speed });
            thrown++;
        }

        bool AnyBallIncoming()
        {
            foreach (var b in balls) if (!b.Resolved) return true;
            return false;
        }

        /// <summary>One frame of the minigame: lane input, player steering, ball flight and collisions.</summary>
        object Tick()
        {
            if (!BotPlays)
            {
                if (GameInput.StepLeftPressed) lane = Mathf.Max(0, lane - 1);
                if (GameInput.StepRightPressed) lane = Mathf.Min(2, lane + 1);
            }
            Steer(LanePos(lane), 1f);

            float dt = Time.deltaTime;
            Vector3 fwd = PlayerFacing;
            float playerLat = Vector3.Dot(player.transform.position - PlayerSpot, right);
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                var b = balls[i];
                float zPrev = b.Z;
                b.Z -= b.Speed * dt;
                float travel = Mathf.InverseLerp(LauncherDistance - 1.5f, 0f, b.Z);
                float h = 1.1f + Mathf.Sin(Mathf.Clamp01(travel) * Mathf.PI) * 1.4f;
                Vector3 basePos = PlayerSpot + right * ((b.Lane - 1) * LaneWidth) + fwd * b.Z;
                b.T.position = basePos + Vector3.up * h;
                b.T.Rotate(Vector3.right, 400f * dt, Space.Self);
                b.Shadow.position = basePos + Vector3.up * 0.02f;

                if (!b.Resolved && zPrev > 0f && b.Z <= 0f)
                {
                    b.Resolved = true;
                    float lat = (b.Lane - 1) * LaneWidth;
                    if (Mathf.Abs(lat - playerLat) < 0.8f)
                    {
                        player.ReceiveHit(null, 0f, AttackType.Light, fwd * -1f);
                        HitFeedback.Sparks(b.T.position, new Color(0.7f, 0.42f, 0.28f), 12, 4f);
                        if (!Bot) ArenaCamera.Shake(0.2f);
                        hud.Feedback(Streak >= 5 ? "¡GOLPE!  RACHA PERDIDA" : "¡GOLPE!", PS2UI.Red);
                        if (!Bot) Debug.Log($"[TRAINING] dodge HIT lane={b.Lane} streak={Streak} -> 0");
                        Streak = 0;
                        Kill(i);
                        continue;
                    }
                    dodged++;
                    Streak++;
                    BestStreak = Mathf.Max(BestStreak, Streak);
                    if (!Bot) Debug.Log($"[TRAINING] dodge ok lane={b.Lane} streak={Streak} speed x{SpeedForStreak(Streak):0.00}");
                    bool faster = Streak == 5 || Streak == 10 || (Streak > 10 && (Streak - 10) % 3 == 0);
                    hud.Feedback(faster ? "¡MAS RAPIDO!  x" + SpeedForStreak(Streak).ToString("0.00") : "¡ESQUIVA!", faster ? PS2UI.Gold : PS2UI.Green, 0.5f);
                }
                if (b.Z < -6f) Kill(i);
            }
            return null;
        }

        void Kill(int i)
        {
            Destroy(balls[i].T.gameObject);
            Destroy(balls[i].Shadow.gameObject);
            balls.RemoveAt(i);
        }

        protected override void Cleanup()
        {
            player.MoveSpeed = savedSpeed;
            foreach (var w in laneWarn) if (w != null) w.SetActive(false);
            for (int i = balls.Count - 1; i >= 0; i--) Kill(i);
        }
    }
}
