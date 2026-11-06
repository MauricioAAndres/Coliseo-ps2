using System.Collections;
using Gladiator.Combat;
using Gladiator.Progression;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Training
{
    /// <summary>
    /// Base for a ludus training station. The station builds its own low-poly props, walks the player
    /// onto its spot, runs a timing minigame and returns a 0..1 score that the TrainingManager turns into stat points.
    /// While it runs, the station drives the player's Fighter directly (PlayerController is disabled).
    /// </summary>
    public abstract class TrainingMinigame : MonoBehaviour
    {
        /// <summary>Autotest: minigames feed themselves bot input instead of reading the gamepad/keyboard.</summary>
        public static bool AutoPlay;
        /// <summary>The minigame the player is doing right now (null when free roaming).</summary>
        public static TrainingMinigame PlayerRunning;

        /// <summary>Free practice: no stat points, no daily limit, runs until StopRequested (Esc).</summary>
        [System.NonSerialized] public bool FreeMode;
        [System.NonSerialized] public bool StopRequested;
        /// <summary>A training partner (NPC) plays this station: bot input, no camera shake or slow-motion.</summary>
        [System.NonSerialized] public bool Bot;
        /// <summary>Taken by a training partner: the player can't use it.</summary>
        [System.NonSerialized] public bool Occupied;
        protected bool BotPlays => AutoPlay || Bot;

        /// <summary>Round loop condition: a fixed number in training, until Esc in free practice.</summary>
        protected bool KeepGoing(int done, int total) => FreeMode ? !StopRequested : done < total;

        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Instructions { get; }
        public abstract StatType Stat { get; }

        [Header("Placement (world)")]
        public float InteractRadius = 2.2f;
        public float CameraDistance = 7f;
        public float CameraPitch = 24f;

        /// <summary>Where the player stands and faces during the minigame.</summary>
        public Vector3 PlayerSpot { get; protected set; }
        public Vector3 PlayerFacing { get; protected set; } = Vector3.forward;
        public Vector3 CameraFocus { get; protected set; }
        public Vector3 InteractPoint => PlayerSpot;

        public float LastScore { get; private set; }

        protected Fighter player;
        protected TrainingHUD hud;
        protected float score;

        public static int PointsFor(float s) => s >= 0.85f ? 3 : s >= 0.55f ? 2 : s >= 0.25f ? 1 : 0;

        /// <summary>Create props as children. Called once by the bootstrap.</summary>
        public abstract void Build();

        protected abstract IEnumerator Run();

        public IEnumerator Play(Fighter p, TrainingHUD h)
        {
            player = p;
            hud = h;
            score = 0f;
            player.SetBlock(false);
            player.DisarmResistance = 1f;

            // Walk onto the spot.
            float t = 0f;
            while (t < 2.5f && Flat(PlayerSpot - player.transform.position).magnitude > 0.12f)
            {
                Steer(PlayerSpot, 1f);
                t += Time.deltaTime;
                yield return null;
            }
            if (Flat(PlayerSpot - player.transform.position).magnitude > 0.4f)
            {
                // Something blocked the walk: snap onto the spot.
                player.Controller.enabled = false;
                player.transform.position = PlayerSpot + Vector3.up * 0.05f;
                player.Controller.enabled = true;
            }
            for (float k = 0; k < 0.3f; k += Time.deltaTime) { Steer(PlayerSpot, 1f); yield return null; }

            hud.ShowMinigame(Title, Instructions);
            for (int i = 3; i >= 1; i--)
            {
                hud.Feedback(i.ToString(), Color.white, 0.6f);
                for (float k = 0; k < 0.6f; k += Time.deltaTime) { Steer(PlayerSpot, 1f); yield return null; }
            }
            hud.Feedback("¡YA!", PS2UI.Gold, 0.5f);

            yield return Run();

            LastScore = Mathf.Clamp01(score);
            hud.HideMinigame();
            Cleanup();
        }

        protected virtual void Cleanup() { }

        /// <summary>Move the fighter towards a point (it keeps facing PlayerFacing). Call every frame.</summary>
        protected void Steer(Vector3 target, float speedScale)
        {
            Vector3 d = Flat(target - player.transform.position);
            player.Move(Vector3.ClampMagnitude(d * 4f, 1f) * speedScale);
            player.SetFacing(PlayerFacing);
        }

        protected static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        protected static IEnumerator WaitWhileSteering(TrainingMinigame m, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime) { m.Steer(m.PlayerSpot, 1f); yield return null; }
        }
    }
}
