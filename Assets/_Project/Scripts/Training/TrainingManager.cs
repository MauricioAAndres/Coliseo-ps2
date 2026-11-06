using System.Collections;
using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.Progression;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Training
{
    /// <summary>
    /// Ludus flow: free roam between stations, start a minigame with E / A, award stat points,
    /// and leave for the arena through the north gate. Each station works once per day.
    /// </summary>
    public class TrainingManager : MonoBehaviour
    {
        public Fighter Player { get; private set; }
        public PlayerController Controller { get; private set; }
        public TrainingHUD Hud { get; private set; }
        public ArenaCamera Cam { get; private set; }
        public readonly List<TrainingMinigame> Stations = new List<TrainingMinigame>();
        public Vector3 GatePoint;
        public float GateRadius = 2.6f;
        public Vector3 ShopPoint;
        public float ShopRadius = 2f;

        public bool Busy { get; private set; }

        GameManager gm;
        float baseDistance, basePitch;

        public void Init(Fighter player, PlayerController pc, TrainingHUD hud, ArenaCamera cam, IEnumerable<TrainingMinigame> stations, Vector3 gate, Vector3 shop)
        {
            Player = player; Controller = pc; Hud = hud; Cam = cam;
            Stations.AddRange(stations);
            GatePoint = gate;
            ShopPoint = shop;
            Hud.Shop.Closed += OnShopClosed;
            baseDistance = cam.Distance;
            basePitch = cam.Pitch;
        }

        void Start()
        {
            gm = GameManager.Ensure();
            var dc = gm.DayCycle;
            string intro = "DIA " + dc.Day + "  -  EL LUDUS:  ENTRENA ANTES DEL COMBATE";
            if (dc.LastFightWon.HasValue)
                intro = (dc.LastFightWon.Value ? "AYER: VICTORIA" : "AYER: DERROTA") + " (+" + dc.LastReward + " denarios).   DIA " + dc.Day + "  -  A ENTRENAR";
            var net = Gladiator.Net.NetSession.Instance;
            if (net != null && net.InGame)
                intro = "LUDUS COMPARTIDO  -  " + net.Players.Count + " GLADIADORES.   ESC > MULTIJUGADOR: LA SALA";
            Hud.ShowMessage(intro, 4f);
        }

        void Update()
        {
            GameInput.UpdateDeviceMode();
            if (Hud.Shop.IsOpen) { Hud.SetPrompt(""); return; } // the shop handles its own input (Esc closes it)
            if (UI.ArenaGateMenu.Instance != null && UI.ArenaGateMenu.Instance.IsOpen) { Hud.SetPrompt(""); return; }
            if (UI.ChoiceMenu.Instance != null && UI.ChoiceMenu.Instance.IsOpen) { Hud.SetPrompt(""); return; }
            // Free practice ends with Esc / B.
            var running = TrainingMinigame.PlayerRunning;
            if (running != null && running.FreeMode && !running.StopRequested && GameInput.BackPressed)
            {
                running.StopRequested = true;
                Hud.Feedback("TERMINANDO...", UI.PS2UI.Gold, 0.8f);
            }
            if (Busy || gm == null) { Hud.SetPrompt(""); return; }

            string key = GameInput.UsingGamepad ? "(A) " : "[E] ";
            var dc = gm.DayCycle;
            Vector3 p = Player.transform.position;

            TrainingMinigame near = null;
            float best = float.MaxValue;
            foreach (var s in Stations)
            {
                float d = Flat(s.InteractPoint - p).magnitude;
                if (d < s.InteractRadius && d < best) { best = d; near = s; }
            }

            // Training partners: E to talk (they only mumble for now).
            Training.TrainingNPC npcNear = null;
            float npcBest = 2.2f;
            foreach (var n in Training.TrainingNPC.All)
            {
                if (n == null || n.Fighter == null) continue;
                float dn = Flat(n.Fighter.transform.position - p).magnitude;
                if (dn < npcBest) { npcBest = dn; npcNear = n; }
            }
            if (npcNear != null && (near == null || npcBest < Flat(near.InteractPoint - p).magnitude))
            {
                Hud.SetPrompt(key + "HABLAR CON " + npcNear.DisplayName.ToUpperInvariant());
                if (GameInput.InteractPressed) npcNear.Talk();
                return;
            }

            if (near != null)
            {
                Hud.SetPrompt(key + near.Title + (dc.StationUsed(near.Id) ? "   (hoy solo practica libre)" : "   (+" + PlayerProfile.Label(near.Stat) + ")"));
                if (GameInput.InteractPressed) OpenStationMenu(near);
                return;
            }

            if (Flat(ShopPoint - p).magnitude < ShopRadius)
            {
                Hud.SetPrompt(key + "TIENDA DEL MERCATOR   (" + gm.Profile.Denarii + " denarios)");
                if (GameInput.InteractPressed) OpenShop();
                return;
            }

            if (Flat(GatePoint - p).magnitude < GateRadius)
            {
                int left = Stations.Count - dc.StationsUsedCount;
                Hud.SetPrompt(key + "PUERTA DE LA ARENA: ELEGIR COMBATE" + (left > 0 ? "     quedan " + left + " entrenamientos" : ""));
                if (GameInput.InteractPressed) OpenGate();
                return;
            }
            Hud.SetPrompt("");
        }

        /// <summary>Station menu: train for stat points (once a day) or free practice until Esc.</summary>
        public void OpenStationMenu(TrainingMinigame s)
        {
            if (Busy || s == null || gm == null) return;
            bool used = gm.DayCycle.StationUsed(s.Id);
            string stat = PlayerProfile.Label(s.Stat);
            UI.ChoiceMenu.Ensure().Open(s.Title, new[]
            {
                new UI.ChoiceMenu.Option
                {
                    Label = "ENTRENAR  (+" + stat + ")", Enabled = !used,
                    Blurb = used ? "Ya entrenaste aqui hoy: vuelve manana." : "Mejora tu " + stat.ToLowerInvariant() + " segun lo bien que lo hagas (una vez al dia).",
                    OnChoose = () => StartStation(s, false),
                },
                new UI.ChoiceMenu.Option
                {
                    Label = "PRACTICA LIBRE", Blurb = "Sin limite y sin mejora de estadisticas: practica hasta que pulses ESC.",
                    OnChoose = () => StartStation(s, true),
                },
            });
        }

        /// <summary>Starts a station's minigame. Training is refused if it was already used today; free practice never.</summary>
        public bool StartStation(TrainingMinigame s, bool free = false)
        {
            if (Busy || s == null || gm == null || s.Occupied) return false;
            if (!free && gm.DayCycle.StationUsed(s.Id)) return false;
            s.FreeMode = free;
            s.StopRequested = false;
            StartCoroutine(RunStation(s));
            return true;
        }

        IEnumerator RunStation(TrainingMinigame s)
        {
            Busy = true;
            Controller.enabled = false;
            Cam.FocusOverride = s.CameraFocus;
            Cam.Distance = s.CameraDistance;
            Cam.Pitch = s.CameraPitch;

            TrainingMinigame.PlayerRunning = s;
            yield return s.Play(Player, Hud);
            TrainingMinigame.PlayerRunning = null;

            if (s.FreeMode)
            {
                Debug.Log($"[TRAINING] free practice {s.Id} score={s.LastScore:0.00}" + (s is DodgeMinigame dm ? $" bestStreak={dm.BestStreak}" : ""));
                Hud.ShowResult("PRACTICA TERMINADA", "Rendimiento " + Mathf.RoundToInt(s.LastScore * 100f) + "%" +
                    (s is DodgeMinigame d2 ? "   mejor racha " + d2.BestStreak : "") + "   (sin mejora de estadisticas)", 3f);
                Cam.FocusOverride = null;
                Cam.Distance = baseDistance;
                Cam.Pitch = basePitch;
                s.FreeMode = false;
                yield return new WaitForSeconds(0.2f);
                Controller.enabled = true;
                Busy = false;
                yield break;
            }

            int pts = TrainingMinigame.PointsFor(s.LastScore);
            gm.Profile.Add(s.Stat, pts);
            gm.DayCycle.MarkStationUsed(s.Id);
            Debug.Log($"[TRAINING] day={gm.DayCycle.Day} station={s.Id} score={s.LastScore:0.00} +{pts} {s.Stat}");
            Hud.ShowResult(pts > 0 ? PlayerProfile.Label(s.Stat) + "  +" + pts : "SIN MEJORA",
                           "Rendimiento " + Mathf.RoundToInt(s.LastScore * 100f) + "%" + (gm.DayCycle.StationsUsedCount >= Stations.Count
                               ? "   -   Entrenamiento completo: ve a la puerta norte" : ""), 3f);

            Cam.FocusOverride = null;
            Cam.Distance = baseDistance;
            Cam.Pitch = basePitch;
            Controller.enabled = true;
            Busy = false;
        }

        /// <summary>The gate menu: duel, 2 vs 1, free-for-all, waves or the champion.</summary>
        public void OpenGate()
        {
            if (Busy || gm == null) return;
            var net = Gladiator.Net.NetSession.Instance;
            if (net != null && net.InGame)
            {
                Hud.ShowMessage(net.IsHost ? "COMBATES MULTIJUGADOR: LLEGAN EN LA PROXIMA FASE (MP2)"
                                           : "SOLO EL ANFITRION PUEDE ELEGIR EL COMBATE (PROXIMA FASE, MP2)", 3f);
                return;
            }
            UI.ArenaGateMenu.Ensure().Open(gm.DayCycle.Day, mode => GoToArena(mode));
        }

        public void GoToArena(EncounterType encounter = EncounterType.Duel)
        {
            if (Busy) return;
            Busy = true;
            Debug.Log($"[TRAINING] day={gm.DayCycle.Day} -> arena {encounter}  STR={gm.Profile.Strength} AGI={gm.Profile.Agility} RES={gm.Profile.Resistance} denarii={gm.Profile.Denarii}");
            gm.DayCycle.GoToArena(encounter);
        }

        public void OpenShop()
        {
            if (Busy) return;
            Busy = true;
            Controller.enabled = false;
            Player.SetBlock(false);
            Hud.HideResult();
            Hud.Shop.Open(gm.Profile);
        }

        void OnShopClosed()
        {
            Controller.enabled = true;
            Busy = false;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
