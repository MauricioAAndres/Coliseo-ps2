using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gladiator.Progression
{
    public enum DayPhase { Training, Arena }
    public enum EncounterType { Duel, FreeForAll, Waves, TwoVsOne, Champion }

    /// <summary>Names and blurbs of the arena modes (gate menu, HUD).</summary>
    public static class Encounters
    {
        public static readonly EncounterType[] MenuOrder =
            { EncounterType.Duel, EncounterType.TwoVsOne, EncounterType.FreeForAll, EncounterType.Waves, EncounterType.Champion };

        public static string Title(EncounterType e)
        {
            switch (e)
            {
                case EncounterType.Duel: return "DUELO";
                case EncounterType.TwoVsOne: return "2 CONTRA 1";
                case EncounterType.FreeForAll: return "TODOS CONTRA TODOS";
                case EncounterType.Waves: return "OLEADAS";
                default: return "CAMPEON";
            }
        }

        public static string Blurb(EncounterType e, int day)
        {
            switch (e)
            {
                case EncounterType.Duel: return $"Un rival, uno contra uno.  Victoria: {50 + 15 * day} denarios.";
                case EncounterType.TwoVsOne: return $"Dos rivales a la vez contra ti.  Victoria: {90 + 20 * day} denarios, +15 por baja.";
                case EncounterType.FreeForAll: return $"Cuatro gladiadores, todos contra todos.  Victoria: {100 + 25 * day}, +15 por baja.";
                case EncounterType.Waves: return "Seis oleadas: 1, 2, 4, 6, 8 y 10 rivales.  25 denarios por baja, +200 si superas la sexta.";
                default: return $"Carpophorus, el campeon: armadura de oro y tridente; esquiva y bloquea mucho mejor.  Victoria: {250 + 20 * day}.";
            }
        }
    }

    /// <summary>
    /// Drives the "Road to Freedom" loop: every day starts in the ludus (Training scene) and ends with
    /// a fight in the arena (Arena scene). Winning or losing advances to the next day's training.
    /// Each training station can be used once per day.
    /// </summary>
    public class DayCycleManager : MonoBehaviour
    {
        public const string TrainingScene = "Town"; // the ludus is inside the town now
        public const string ArenaScene = "Arena";

        public int Day { get; private set; } = 1;
        public DayPhase Phase { get; private set; } = DayPhase.Training;
        public bool? LastFightWon { get; private set; }
        public EncounterType NextEncounter { get; private set; } = EncounterType.Duel;
        /// <summary>Denarii earned in the last fight (shown in the ludus the next morning).</summary>
        public int LastReward { get; set; }

        /// <summary>The arena was fought today already: one fight a day (sleep, or wait for midnight).</summary>
        public bool FoughtToday { get; private set; }
        /// <summary>Game hours a fight in the arena takes.</summary>
        public const float ArenaHours = 2f;

        readonly HashSet<string> stationsUsedToday = new HashSet<string>();

        /// <summary>Midnight passed (awake or asleep): a new day. Stations and the arena are available again.</summary>
        public void NextDay()
        {
            Day++;
            FoughtToday = false;
            stationsUsedToday.Clear();
            Changed?.Invoke();
        }

        /// <summary>Online, a guest: the host's day.</summary>
        public void SetDay(int day)
        {
            if (day == Day) return;
            Day = day;
            FoughtToday = false;
            stationsUsedToday.Clear();
            Changed?.Invoke();
        }

        /// <summary>Online arena: it counts as today's fight too.</summary>
        public void MarkFought() { FoughtToday = true; Changed?.Invoke(); }

        public event Action Changed;

        public static string PhaseLabel(DayPhase p) => p == DayPhase.Training ? "ENTRENAMIENTO" : "ARENA";

        /// <summary>Scenes call this on load so the phase is right even when a scene is opened directly in the editor.</summary>
        public void NotifySceneStarted(DayPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            Changed?.Invoke();
        }

        public bool StationUsed(string id) => stationsUsedToday.Contains(id);
        public int StationsUsedCount => stationsUsedToday.Count;

        public void MarkStationUsed(string id)
        {
            if (stationsUsedToday.Add(id)) Changed?.Invoke();
        }

        /// <summary>Training is over for today: walk through the gate to the arena.</summary>
        public void GoToArena(EncounterType encounter = EncounterType.Duel)
        {
            NextEncounter = encounter;
            Phase = DayPhase.Arena;
            Changed?.Invoke();
            Load(ArenaScene);
        }

        /// <summary>The day's fight has been resolved: next day, back to the ludus.</summary>
        /// <summary>Lost in the arena: the next scene (the town) starts with the gladiator in an infirmary bed.</summary>
        public static bool WakeInInfirmary;
        public const string TownScene = "Town";

        public void CompleteArena(bool won)
        {
            LastFightWon = won;
            FoughtToday = true;
            Phase = DayPhase.Training;
            Changed?.Invoke();
            // the fight took a couple of hours (the town's clock; past midnight it is a new day)
            var sim = Gladiator.Town.TownSimulation.Instance;
            if (sim != null) sim.AdvanceHours(ArenaHours);
            if (!won) { WakeInInfirmary = true; Load(TownScene); return; }
            Load(TrainingScene);
        }

        static void Load(string scene)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
        }
    }
}
