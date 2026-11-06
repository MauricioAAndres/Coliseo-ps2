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
    /// "Las Pesas" (Resistance): mash a button to push the effort bar to the top before time runs out
    /// while it constantly drains. 3 lifts, each heavier (faster drain). Faster lifts score more.
    /// </summary>
    public class WeightLiftMinigame : TrainingMinigame
    {
        public int Reps = 3;
        public float TimePerRep = 5f;
        public float PerPress = 0.075f;

        public override string Id => "pesas";
        public override string Title => "LAS PESAS  -  RESISTENCIA";
        public override string Instructions => "Machaca el boton para levantar   (E / ESPACIO / clic  -  A / X)";
        public override StatType Stat => StatType.Resistance;

        Transform barbell;
        Vector3 barbellRest;
        Quaternion barbellRestRot;

        public override void Build()
        {
            Material iron = PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f));
            Material stoneW = PS2Materials.Get("stone_weight", PS2Textures.Stone, new Color(0.7f, 0.66f, 0.6f));
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);

            PlayerFacing = transform.forward;
            PlayerSpot = transform.position;
            CameraFocus = transform.position + Vector3.up * 0.4f;
            CameraDistance = 6f;
            CameraPitch = 16f;

            // Barbell: iron bar with two stone discs per side (cylinders rotated onto the X axis).
            barbell = new GameObject("Barbell").transform;
            barbell.SetParent(transform, false);
            Quaternion side = Quaternion.Euler(0, 0, 90f);
            PS2Spawn.Mesh(barbell, "Bar", LowPolyMesh.Cylinder(0.04f, 1.8f, 6), new Vector3(0.9f, 0, 0), iron, side);
            foreach (float x in new[] { -0.75f, 0.6f })
            {
                PS2Spawn.Mesh(barbell, "Disc", LowPolyMesh.Cylinder(0.26f, 0.15f, 8), new Vector3(x + 0.15f, 0, 0), stoneW, side);
                PS2Spawn.Mesh(barbell, "Disc2", LowPolyMesh.Cylinder(0.2f, 0.1f, 8), new Vector3(x < 0 ? x + 0.0f : x + 0.25f, 0, 0), stoneW, side);
            }
            barbellRest = transform.position + transform.forward * 0.45f + Vector3.up * 0.27f;
            barbellRestRot = transform.rotation;
            barbell.SetPositionAndRotation(barbellRest, barbellRestRot);

            // Rack behind the lifter + a few spare stones.
            PS2Spawn.Box(transform, "RackL", new Vector3(0.15f, 1.3f, 0.15f), new Vector3(-1.2f, 0.65f, -1.4f), wood);
            PS2Spawn.Box(transform, "RackR", new Vector3(0.15f, 1.3f, 0.15f), new Vector3(1.2f, 0.65f, -1.4f), wood);
            PS2Spawn.Box(transform, "RackTop", new Vector3(2.6f, 0.12f, 0.2f), new Vector3(0, 1.3f, -1.4f), wood);
            PS2Spawn.Mesh(transform, "Stone1", LowPolyMesh.Cylinder(0.3f, 0.18f, 8), new Vector3(1.7f, 0, 0.3f), stoneW);
            PS2Spawn.Mesh(transform, "Stone2", LowPolyMesh.Cylinder(0.25f, 0.16f, 8), new Vector3(1.75f, 0.18f, 0.25f), stoneW);
            PS2Spawn.Mesh(transform, "Stone3", LowPolyMesh.Cylinder(0.3f, 0.18f, 8), new Vector3(-1.8f, 0, 0.1f), stoneW);
            PS2Spawn.Blob(transform, 0.6f, new Vector3(0, 0.02f, 0.45f));
            LowPolyMesh.BakeAmbient(gameObject, TrainingBootstrap.AmbientSky, TrainingBootstrap.AmbientGround);
        }

        void SetLift(float h, float tremble)
        {
            // Arm from "hanging" (pitch +55) to "overhead" (pitch -105); barbell follows the fist.
            float pitch = Mathf.Lerp(55f, -82f, h);
            player.ArmPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 fist = player.WeaponSocket.position;
            Vector3 centre = fist - player.transform.right * 0.42f + player.transform.forward * 0.15f; // between both hands, clear of the face
            barbell.SetPositionAndRotation(centre + Random.insideUnitSphere * tremble, player.transform.rotation);
        }

        protected override IEnumerator Run()
        {
            float total = 0f;
            var w = player.CurrentWeapon;
            if (w != null) w.gameObject.SetActive(false);
            player.ExternalArmControl = true;

            int repsDone = 0;
            for (int r = 0; KeepGoing(r, Reps); r++, repsDone++)
            {
                float drain = 0.2f + Mathf.Min(r, Reps - 1) * 0.13f;
                float effort = 0f, t = 0f;
                float botRate = Random.Range(8.5f, 12.5f), botAcc = 0f;
                bool done = false;
                hud.SetCounter("LEVANTAMIENTO " + (r + 1) + " / " + Reps);

                while (t < TimePerRep)
                {
                    t += Time.deltaTime;
                    bool press;
                    if (BotPlays) { botAcc += Time.deltaTime * botRate; press = botAcc >= 1f; if (press) botAcc -= 1f; }
                    else press = GameInput.MashPressed;
                    if (press) effort += PerPress;
                    effort = Mathf.Clamp01(effort - drain * Time.deltaTime);

                    float left = 1f - t / TimePerRep;
                    hud.SetFill(effort, effort > 0.66f ? PS2UI.Green : effort > 0.33f ? PS2UI.Yellow : PS2UI.Red);
                    hud.SetMarker(-1f);
                    hud.SetCounter("LEVANTAMIENTO " + (r + 1) + (FreeMode ? "" : " / " + Reps) + "      TIEMPO " + (TimePerRep - t).ToString("0.0") + (FreeMode ? "      ESC: terminar" : ""));
                    SetLift(Mathf.Pow(effort, 1.3f), 0.015f * effort * (1f + Mathf.Min(r, Reps)));
                    Steer(PlayerSpot, 1f);
                    if (effort >= 0.999f) { done = true; total += 0.6f + 0.4f * left; break; }
                    yield return null;
                }

                if (done)
                {
                    hud.Feedback("¡ARRIBA!", PS2UI.Green, 0.8f);
                    if (!Bot) ArenaCamera.Shake(0.1f);
                    for (float k = 0; k < 0.7f; k += Time.deltaTime) { SetLift(1f, 0f); Steer(PlayerSpot, 1f); yield return null; }
                }
                else
                {
                    total += effort * 0.4f;
                    hud.Feedback("¡SIN FUERZAS!", PS2UI.Red, 0.8f);
                }

                // Lower the weight and rest.
                float from = Mathf.Pow(effort, 1.3f);
                for (float k = 0; k < 0.5f; k += Time.deltaTime) { SetLift(Mathf.Lerp(from, 0f, k / 0.5f), 0f); Steer(PlayerSpot, 1f); yield return null; }
                HitFeedback.Sparks(barbell.position + Vector3.down * 0.2f, new Color(0.75f, 0.65f, 0.5f), 5, 2f);
                hud.SetFill(0f, PS2UI.Red);
                yield return WaitWhileSteering(this, 0.6f);
            }
            score = total / Mathf.Max(1, FreeMode ? repsDone : Reps);
            hud.SetCounter("RESULTADO " + Mathf.RoundToInt(score * 100f) + "%");
            yield return WaitWhileSteering(this, 0.6f);
        }

        protected override void Cleanup()
        {
            player.ExternalArmControl = false;
            var w = player.CurrentWeapon;
            if (w != null) w.gameObject.SetActive(true);
            barbell.SetPositionAndRotation(barbellRest, barbellRestRot);
        }
    }
}
