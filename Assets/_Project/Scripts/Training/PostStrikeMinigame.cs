using System.Collections;
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
    /// "El Palus" (Strength): a marker sweeps a bar; strike when it's inside the green zone.
    /// 6 rounds, each faster with a narrower zone. Green = 1 pt, yellow (near) = 0.4, miss = 0.
    /// The gladiator actually swings at a wooden post that wobbles and throws chips.
    /// </summary>
    public class PostStrikeMinigame : TrainingMinigame
    {
        public int Rounds = 6;

        public override string Id => "palus";
        public override string Title => "EL PALUS  -  FUERZA";
        public override string Instructions => "Golpea cuando la marca este en VERDE   (clic / E / ESPACIO  -  X / A)";
        public override StatType Stat => StatType.Strength;

        Transform postPivot;
        float wobble, wobbleVel;
        Vector3 wobbleAxis = Vector3.right;

        public override void Build()
        {
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            Material darkWood = PS2Materials.Get("wood_dark", PS2Textures.Wood, new Color(0.6f, 0.5f, 0.42f));
            Material straw = PS2Materials.Get("straw", PS2Textures.Cloth, new Color(0.95f, 0.8f, 0.42f));
            Material rope = PS2Materials.Get("rope", PS2Textures.Cloth, new Color(0.6f, 0.48f, 0.3f));

            PS2Spawn.Mesh(transform, "Base", LowPolyMesh.Cylinder(0.5f, 0.2f, 8), Vector3.zero, darkWood);
            postPivot = new GameObject("PostPivot").transform;
            postPivot.SetParent(transform, false);
            postPivot.localPosition = new Vector3(0, 0.2f, 0);
            PS2Spawn.Mesh(postPivot, "Palus", LowPolyMesh.Cylinder(0.2f, 1.9f, 7), Vector3.zero, wood);
            PS2Spawn.Mesh(postPivot, "Straw", LowPolyMesh.Cylinder(0.27f, 0.55f, 7), new Vector3(0, 0.95f, 0), straw);
            PS2Spawn.Mesh(postPivot, "RopeA", LowPolyMesh.Cylinder(0.28f, 0.06f, 7), new Vector3(0, 1.0f, 0), rope);
            PS2Spawn.Mesh(postPivot, "RopeB", LowPolyMesh.Cylinder(0.28f, 0.06f, 7), new Vector3(0, 1.38f, 0), rope);
            PS2Spawn.Blob(transform, 0.7f, new Vector3(0, 0.02f, 0));
            LowPolyMesh.BakeAmbient(gameObject, TrainingBootstrap.AmbientSky, TrainingBootstrap.AmbientGround);

            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.32f; col.height = 2.1f; col.center = new Vector3(0, 1.05f, 0);

            PlayerFacing = transform.forward;
            PlayerSpot = transform.position - transform.forward * 1.45f;
            CameraFocus = Vector3.Lerp(PlayerSpot, transform.position, 0.5f);
            CameraDistance = 6.5f;
            CameraPitch = 22f;
        }

        void Update()
        {
            // Damped spring wobble of the post.
            wobbleVel += (-wobble * 160f - wobbleVel * 9f) * Time.deltaTime;
            wobble += wobbleVel * Time.deltaTime;
            if (postPivot != null) postPivot.localRotation = Quaternion.AngleAxis(wobble, wobbleAxis);
        }

        protected override IEnumerator Run()
        {
            float total = 0f;
            int done = 0;
            for (int r = 0; KeepGoing(r, Rounds); r++, done++)
            {
                float kr = Mathf.Clamp01(r / (float)(Rounds - 1));
                float width = Mathf.Lerp(0.2f, 0.09f, kr);
                float speed = Mathf.Lerp(0.75f, 1.6f, kr); // sweeps per second
                float center = Random.Range(0.2f + width, 0.8f - width);
                float gMin = center - width * 0.5f, gMax = center + width * 0.5f;
                const float near = 0.07f;
                hud.SetZones(gMin, gMax, near);
                hud.SetCounter(FreeMode ? "GOLPE " + (r + 1) + "      PUNTOS " + total.ToString("0.0") + "      ESC: terminar"
                                        : "GOLPE " + (r + 1) + " / " + Rounds + "      PUNTOS " + total.ToString("0.0"));

                // Bot (autotest) picks a target it will try to hit: usually green, sometimes slightly off.
                float botTarget = Random.value < 0.7f ? center + Random.Range(-width * 0.35f, width * 0.35f)
                                                      : center + (Random.value < 0.5f ? -1f : 1f) * (width * 0.5f + Random.Range(0.02f, 0.12f));

                float t = Random.Range(0f, 0.5f), v = 0f;
                bool pressed = false;
                float limit = 3.2f;
                for (float el = 0f; el < limit; el += Time.deltaTime)
                {
                    t += Time.deltaTime * speed;
                    v = Mathf.PingPong(t, 1f);
                    hud.SetMarker(v);
                    Steer(PlayerSpot, 1f);
                    if (BotPlays ? Mathf.Abs(v - botTarget) < 0.025f && el > 0.4f : GameInput.StrikePressed) { pressed = true; break; }
                    yield return null;
                }

                float gained;
                if (!pressed) { gained = 0f; hud.Feedback("¡LENTO!", PS2UI.Red); }
                else if (v >= gMin && v <= gMax) { gained = 1f; hud.Feedback("¡PERFECTO!", PS2UI.Green); }
                else if (v >= gMin - near && v <= gMax + near) { gained = 0.4f; hud.Feedback("BIEN", PS2UI.Yellow); }
                else { gained = 0f; hud.Feedback("FALLO", PS2UI.Red); }
                total += gained;

                if (pressed)
                {
                    player.TryAttack(gained >= 1f ? AttackType.Heavy : AttackType.Light);
                    // Wait for the swing to connect, then react.
                    for (float k = 0; k < 0.6f && player.State != FighterState.Active; k += Time.deltaTime) { Steer(PlayerSpot, 1f); yield return null; }
                    Vector3 hitPoint = transform.position + Vector3.up * 1.2f - PlayerFacing * 0.25f;
                    wobbleAxis = Vector3.Cross(Vector3.up, PlayerFacing).normalized;
                    wobbleVel += gained >= 1f ? 260f : gained > 0f ? 140f : 60f;
                    HitFeedback.Sparks(hitPoint, new Color(0.85f, 0.7f, 0.35f), gained >= 1f ? 12 : 6, gained >= 1f ? 4.5f : 3f);
                    if (gained >= 1f && !Bot) { HitFeedback.HitStop(0.07f); ArenaCamera.Shake(0.2f); }
                }
                hud.SetMarker(v);
                yield return WaitWhileSteering(this, 0.7f);
            }
            score = total / Mathf.Max(1, FreeMode ? done : Rounds);
            hud.SetCounter("PUNTOS " + total.ToString("0.0") + " / " + Mathf.Max(1, FreeMode ? done : Rounds));
            yield return WaitWhileSteering(this, 0.6f);
        }
    }
}
