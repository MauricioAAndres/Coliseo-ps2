using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Training
{
    /// <summary>
    /// The ludus is part of the town: its training yard (walls, portico, props, the three stations and the partners'
    /// stations) is built inside the town's ludus walls, its north gate opens onto the town by itself. No scene change:
    /// walk in and out. Yard space is the old Training scene's world space, placed with the ludus' transform
    /// (+z = towards the town).
    /// </summary>
    public static class LudusInTown
    {
        public const float HalfX = 13f, HalfZ = 11f;

        /// <summary>Is a world point inside the yard (inside the walls)?</summary>
        public static bool Inside(Transform yard, Vector3 p)
        {
            Vector3 l = yard.InverseTransformPoint(p);
            return Mathf.Abs(l.x) < HalfX + 0.3f && Mathf.Abs(l.z) < HalfZ + 0.3f && l.y > -1f && l.y < 6f;
        }

        /// <summary>Walls, gate, props and the three stations (colliders: build before the town's navmesh).</summary>
        public static List<TrainingMinigame> BuildStructure(Transform yard, Transform markers)
        {
            TrainingBootstrap.BuildYard(yard, true);
            TrainingBootstrap.StationRoot = yard;
            var stations = new List<TrainingMinigame>
            {
                TrainingBootstrap.MakeStation<PostStrikeMinigame>("Station_Palus", new Vector3(-6.5f, 0, 4.5f), 90f),
                TrainingBootstrap.MakeStation<WeightLiftMinigame>("Station_Pesas", new Vector3(-5.25f, 0, -2.5f), 180f),
                TrainingBootstrap.MakeStation<DodgeMinigame>("Station_Esquiva", new Vector3(6.5f, 0, -5.5f), 0f),
            };
            TrainingBootstrap.StationRoot = null;
            foreach (var s in stations) TrainingBootstrap.MakeStationMarker(markers, s);
            return stations;
        }

        /// <summary>The partners and the training manager (needs the player, the camera and the HUD).</summary>
        public static TrainingManager Begin(Transform yard, Transform markers, List<TrainingMinigame> stations, Fighter player,
                                            PlayerController pc, TrainingHUD hud, ArenaCamera camCtl, Camera cam)
        {
            TrainingBootstrap.StationRoot = yard;
            var partners = TrainingBootstrap.BuildPartners(markers, cam, hud);
            TrainingBootstrap.StationRoot = null;
            var manager = new GameObject("TrainingManager").AddComponent<TrainingManager>();
            Vector3 nowhere = new Vector3(100000f, 0f, 100000f); // the arena is reached through the town now
            manager.Init(player, pc, hud, camCtl, stations, nowhere, nowhere);
            manager.Yard = yard;
            Partners = partners;
            return manager;
        }

        public static List<TrainingMinigame> Partners { get; private set; }

        /// <summary>Where a gladiator starts the day in the yard (multiplayer: side by side).</summary>
        public static Vector3 Spawn(Transform yard, int slot = 0) => yard.TransformPoint(TrainingBootstrap.SlotSpawn(slot));
    }

    /// <summary>The ludus' gate onto the town: both leaves swing open while someone is close, and close again after.</summary>
    public class LudusGateDoors : MonoBehaviour
    {
        public Transform Left, Right;
        public const float OpenAngle = 100f, Range = 3.4f;
        public bool IsOpen { get; private set; }
        public float Angle { get; private set; }

        void Update()
        {
            bool near = false;
            Vector3 c = transform.position;
            foreach (var f in Fighter.All)
            {
                if (f == null || !f.isActiveAndEnabled) continue;
                if (f.GetComponent<Gladiator.Town.CitizenNPC>() != null) continue; // townsfolk (the gate guards) do not open it
                Vector3 d = f.transform.position - c; d.y = 0f;
                if (d.sqrMagnitude < Range * Range) { near = true; break; }
            }
            if (near != IsOpen)
            {
                IsOpen = near;
                Gladiator.Audio.AudioManager.At3D(near ? "ui_select" : "ui_back", c + Vector3.up * 1.5f, 0.7f);
            }
            float target = IsOpen ? OpenAngle : 0f;
            if (Mathf.Approximately(Angle, target)) return;
            Angle = Mathf.MoveTowards(Angle, target, 220f * Time.deltaTime);
            // both leaves swing into the yard (-z)
            if (Left != null) Left.localRotation = Quaternion.Euler(0f, Angle, 0f);
            if (Right != null) Right.localRotation = Quaternion.Euler(0f, -Angle, 0f);
        }
    }
}
