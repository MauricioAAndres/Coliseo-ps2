using Gladiator.Progression;
using UnityEngine;

namespace Gladiator.Core
{
    /// <summary>
    /// Persistent singleton (DontDestroyOnLoad). Owns the player profile and the day cycle.
    /// Created on demand by whichever scene starts first, so both Training and Arena can be played directly.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public PlayerProfile Profile { get; private set; } = new PlayerProfile();
        public DayCycleManager DayCycle { get; private set; }

        /// <summary>True when the player was launched with -autotest (scripted smoke test).</summary>
        public static bool AutoTest => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        public static GameManager Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GameManager");
            DontDestroyOnLoad(go);
            return go.AddComponent<GameManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DayCycle = gameObject.AddComponent<DayCycleManager>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
