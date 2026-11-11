using System.Collections.Generic;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.Progression;
using Gladiator.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gladiator.Arena
{
    /// <summary>Runs one arena encounter: win/lose rules, hit feedback, restart.</summary>
    public class ArenaManager : MonoBehaviour
    {
        public EncounterType Encounter { get; private set; }
        public Fighter Player { get; private set; }
        public readonly List<Fighter> Enemies = new List<Fighter>();
        public bool MatchOver { get; private set; }
        public bool PlayerWon { get; private set; }
        public int PlayerKills { get; private set; }
        public int Reward { get; private set; }
        /// <summary>Wave mode: current wave (1..6) and waves fully cleared.</summary>
        public int Wave { get; private set; }
        public int WavesCleared { get; private set; }
        /// <summary>Opponents brought down (any killer): the wave mode pays per head.</summary>
        public int EnemiesDown { get; private set; }

        ArenaBootstrap boot;
        readonly List<Fighter> waveEnemies = new List<Fighter>();
        float nextWaveAt = -1f;

        GameHUD hud;
        float endTime;
        bool continuing;

        static readonly Color Blood = new Color(0.55f, 0.03f, 0.02f);
        static readonly Color Spark = new Color(1f, 0.85f, 0.35f);

        /// <summary>Online arena (MP2): NetArena decides the end; no rewards, the campaign day does not change.</summary>
        public bool Online;

        public void Begin(EncounterType encounter, Fighter player, IEnumerable<Fighter> enemies, GameHUD gameHud, ArenaBootstrap bootstrap = null)
        {
            boot = bootstrap;
            Encounter = encounter;
            Player = player;
            Enemies.Clear();
            Enemies.AddRange(enemies);
            hud = gameHud;
            Fighter.AnyDied += OnDied;
            Fighter.AnyHitResolved += OnHit;
            if (Online) return; // NetArena shows its own messages
            string day = GameManager.Instance != null ? "DIA " + GameManager.Instance.DayCycle.Day + "  -  " : "";
            string title = encounter == EncounterType.Duel ? "DUELO  -  ¡LUCHA!" : encounter == EncounterType.Champion ? "¡EL CAMPEON TE ESPERA!" :
                           encounter == EncounterType.Waves ? "OLEADAS  -  OLEADA 1" : Encounters.Title(encounter);
            if (hud != null) hud.ShowMessage(day + title, 2.5f);
            if (encounter == EncounterType.Waves)
            {
                Wave = 1;
                waveEnemies.AddRange(Enemies);
                UpdateWaveStatus();
            }
        }

        void OnDestroy()
        {
            Fighter.AnyDied -= OnDied;
            Fighter.AnyHitResolved -= OnHit;
            Time.timeScale = 1f;
        }

        void OnHit(HitResult r)
        {
            bool heavy = r.Type == AttackType.Heavy;
            switch (r.Outcome)
            {
                case HitOutcome.Hit:
                    HitFeedback.Sparks(r.Point, Blood, heavy ? 14 : 8, heavy ? 5f : 3.5f);
                    HitFeedback.HitStop(heavy ? 0.09f : 0.045f);
                    ArenaCamera.Shake(heavy ? 0.35f : 0.15f);
                    break;
                case HitOutcome.Blocked:
                    HitFeedback.Sparks(r.Point, Spark, 6, 3f);
                    HitFeedback.HitStop(0.03f);
                    ArenaCamera.Shake(0.08f);
                    break;
                case HitOutcome.GuardBreak:
                    HitFeedback.Sparks(r.Point, Spark, 10, 4f);
                    HitFeedback.Sparks(r.Point, Blood, 4, 3f);
                    HitFeedback.HitStop(0.07f);
                    ArenaCamera.Shake(0.25f);
                    if (hud != null) hud.ShowMessage(r.Target == Player ? "¡TU GUARDIA SE ROMPE!" : "¡GUARDIA ROTA!", 1.2f);
                    break;
            }

            if (r.Killed && hud != null && Encounter != EncounterType.Duel && Encounter != EncounterType.Waves && r.Target != Player)
                hud.ShowMessage("¡" + r.Target.DisplayName.ToUpper() + " CAE!" + (r.Attacker == Player ? "  +15" : ""), 1.6f);
            else if (r.Disarmed && hud != null)
                hud.ShowMessage(r.Target == Player ? "¡TE HAN DESARMADO!" : "¡" + r.Target.DisplayName.ToUpper() + " PIERDE SU ARMA!", 1.6f);
        }

        void OnDied(Fighter f)
        {
            if (MatchOver || Online) return;
            if (f != Player && f.LastAttacker == Player) PlayerKills++;
            if (f != Player) EnemiesDown++;
            if (f == Player) { End(false); return; }
            if (Encounter == EncounterType.Waves)
            {
                UpdateWaveStatus();
                foreach (var e in waveEnemies) if (e != null && e.IsAlive) return;
                WavesCleared = Wave;
                if (Wave >= ArenaBootstrap.WaveSizes.Length) { End(true); return; }
                nextWaveAt = Time.time + 3.5f;
                if (hud != null) hud.ShowMessage($"¡OLEADA {Wave} SUPERADA!  +{25 * EnemiesDown} denarios hasta ahora", 3f);
                return;
            }
            foreach (var e in Enemies) if (e != null && e.IsAlive) return;
            End(true);
        }

        void End(bool won)
        {
            MatchOver = true;
            PlayerWon = won;
            endTime = Time.unscaledTime;
            var gm = GameManager.Instance;
            string next = "manana";
            if (gm != null)
            {
                int kills = Encounter == EncounterType.Waves ? EnemiesDown : PlayerKills;
                Reward = StatEffects.Reward(Encounter, won, gm.DayCycle.Day, kills, WavesCleared);
                gm.Profile.AddDenarii(Reward);
                gm.Profile.RecordFight(won);
                gm.DayCycle.LastReward = Reward;
                next = "DIA " + (gm.DayCycle.Day + 1);
            }
            Debug.Log($"[ARENA] end won={won} encounter={Encounter} kills={PlayerKills} down={EnemiesDown} wave={Wave} cleared={WavesCleared} reward={Reward}");
            if (hud != null)
                hud.ShowBanner(won ? "VICTORIA" : "DERROTA",
                               "+" + Reward + " DENARIOS" + (PlayerKills > 0 ? " (" + PlayerKills + (PlayerKills == 1 ? " baja)" : " bajas)") : "") +
                               "      E / ENTER  -  (A) / START:  volver al pueblo",
                               won ? new Color(1f, 0.8f, 0.25f) : new Color(0.85f, 0.15f, 0.1f));
        }

        /// <summary>Leave the arena: next day starts in the ludus.</summary>
        public void Continue()
        {
            if (continuing || !MatchOver) return;
            continuing = true;
            Time.timeScale = 1f;
            var gm = GameManager.Instance;
            if (gm != null) gm.DayCycle.CompleteArena(PlayerWon);
            else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void UpdateWaveStatus()
        {
            if (hud == null || Encounter != EncounterType.Waves) return;
            int alive = 0;
            foreach (var e in waveEnemies) if (e != null && e.IsAlive) alive++;
            hud.SetStatus($"OLEADA {Wave}/{ArenaBootstrap.WaveSizes.Length}    RIVALES {alive}/{waveEnemies.Count}    BAJAS {EnemiesDown} (+{25 * EnemiesDown} den.)");
        }

        void Update()
        {
            if (!MatchOver && Encounter == EncounterType.Waves && nextWaveAt > 0f && Time.time >= nextWaveAt && boot != null)
            {
                // Next wave: the fallen stay on the sand.
                nextWaveAt = -1f;
                Wave++;
                waveEnemies.Clear();
                waveEnemies.AddRange(boot.SpawnWave(Wave));
                foreach (var e in waveEnemies) if (!Enemies.Contains(e)) Enemies.Add(e);
                if (hud != null)
                {
                    hud.ResetEnemies(waveEnemies);
                    hud.ShowMessage($"OLEADA {Wave}  -  {waveEnemies.Count} RIVALES", 2.5f);
                }
                UpdateWaveStatus();
            }
            if (MatchOver && Time.unscaledTime - endTime > 1f && (GameInput.ConfirmPressed || GameInput.RestartPressed))
                Continue();
        }
    }
}
