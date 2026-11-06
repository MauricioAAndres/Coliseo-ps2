using System.Collections.Generic;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.PS2Style;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gladiator.Audio
{
    /// <summary>
    /// Plays every sound of the game from the combat/movement events (no scene wiring needed):
    /// swings, hits, blocks, guard breaks, kicks, steps, jumps, landings, rolls, climbs, stun, deaths, pickups;
    /// the arena crowd (murmur loop + cheers) and horns; one music loop per scene with a crossfade.
    /// Volumes come from GameSettings (master = AudioListener, music, effects). The listener follows the game
    /// camera. While the pause menu is open the effects pause; music and menu clicks keep playing.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }
        /// <summary>How many times each sound played (for the autotest).</summary>
        public static readonly SortedDictionary<string, int> Counts = new SortedDictionary<string, int>();

        readonly Dictionary<string, AudioClip[]> sfx = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<MusicSynth.Piece, AudioClip> music = new Dictionary<MusicSynth.Piece, AudioClip>();
        AudioSource[] pool;
        int poolNext;
        AudioSource musicA, musicB, ambience, ui;
        float musicFade = 1f, musicTarget = 0.5f;
        bool musicOnA = true;
        AudioListener listener;
        Transform listenerT;

        // per-scene state
        bool inArena;
        Arena.ArenaManager arenaManager;
        bool endPlayed, startHornPlayed;
        float sceneTime, nextCheer;
        readonly Dictionary<Fighter, (Vector3 pos, float dist)> steps = new Dictionary<Fighter, (Vector3, float)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("AudioManager");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioManager>();
        }

        void Awake()
        {
            Instance = this;
            listener = new GameObject("Listener").AddComponent<AudioListener>();
            listener.transform.SetParent(transform, false);
            listenerT = listener.transform;

            pool = new AudioSource[20];
            for (int i = 0; i < pool.Length; i++)
            {
                var s = new GameObject("Sfx" + i).AddComponent<AudioSource>();
                s.transform.SetParent(transform, false);
                s.playOnAwake = false;
                s.spatialBlend = 0.7f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 5f;
                s.maxDistance = 45f;
                s.dopplerLevel = 0f;
                pool[i] = s;
            }
            AudioSource Flat(string n, bool loop)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = loop;
                s.spatialBlend = 0f;
                s.ignoreListenerPause = n != "ambience";
                return s;
            }
            musicA = Flat("musicA", true);
            musicB = Flat("musicB", true);
            ambience = Flat("ambience", true);
            ui = Flat("ui", false);

            float t0 = Time.realtimeSinceStartup;
            Register("swing_light", SoundSynth.SwingLight());
            Register("swing_heavy", SoundSynth.SwingHeavy());
            Register("swing_fist", SoundSynth.SwingFist());
            Register("swing_kick", SoundSynth.SwingKick());
            Register("whiff", SoundSynth.Whiff());
            Register("hit", SoundSynth.HitFlesh(0), SoundSynth.HitFlesh(1), SoundSynth.HitFlesh(2));
            Register("hit_heavy", SoundSynth.HitHeavy());
            Register("kick_hit", SoundSynth.KickHit());
            Register("block", SoundSynth.Block(0), SoundSynth.Block(1), SoundSynth.Block(2));
            Register("guard_break", SoundSynth.GuardBreak());
            Register("step", SoundSynth.Step(0), SoundSynth.Step(1), SoundSynth.Step(2), SoundSynth.Step(3));
            Register("jump", SoundSynth.Jump());
            Register("land", SoundSynth.Land(false));
            Register("land_hard", SoundSynth.Land(true));
            Register("roll", SoundSynth.Roll());
            Register("climb", SoundSynth.Climb());
            Register("stun", SoundSynth.Stun());
            Register("death", SoundSynth.Death());
            Register("pickup", SoundSynth.Pickup());
            Register("ui_move", SoundSynth.UiMove());
            Register("ui_select", SoundSynth.UiTwoTone("ui_select", 660f, 990f));
            Register("ui_back", SoundSynth.UiTwoTone("ui_back", 990f, 620f));
            Register("crowd_loop", SoundSynth.CrowdLoop());
            Register("cheer", SoundSynth.Cheer(3.2f, "cheer"));
            Register("cheer_small", SoundSynth.Cheer(1.6f, "cheer_small"));
            Register("horn_start", SoundSynth.HornStart());
            Register("horn_win", SoundSynth.HornVictory());
            Register("horn_lose", SoundSynth.HornDefeat());
            Register("murmur", SoundSynth.Murmur(0), SoundSynth.Murmur(1), SoundSynth.Murmur(2));
            Register("complain", SoundSynth.Complain());
            Register("taunt_shout", SoundSynth.TauntShout());
            Register("power_ready", SoundSynth.PowerReady());
            Debug.Log($"[AUDIO] {sfx.Count} sounds synthesized in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");

            Fighter.AnyAttackSwing += OnSwing;
            Fighter.AnyHitResolved += OnHit;
            Fighter.AnyAttackEvaded += OnEvaded;
            Fighter.AnyMove += OnMove;
            Fighter.AnyWeaponChanged += OnWeapon;
            Fighter.AnyDied += OnDied;
            SceneManager.activeSceneChanged += (a, b) => OnScene(b);
            GameSettings.Changed += ApplyVolumes;
            OnScene(SceneManager.GetActiveScene());
        }

        void OnDestroy()
        {
            Fighter.AnyAttackSwing -= OnSwing;
            Fighter.AnyHitResolved -= OnHit;
            Fighter.AnyAttackEvaded -= OnEvaded;
            Fighter.AnyMove -= OnMove;
            Fighter.AnyWeaponChanged -= OnWeapon;
            Fighter.AnyDied -= OnDied;
            GameSettings.Changed -= ApplyVolumes;
        }

        void Register(string key, params AudioClip[] clips) => sfx[key] = clips;

        /// <summary>Peak and RMS of every synthesized sound (autotest sanity check: nothing silent or clipping).</summary>
        public string LevelReport()
        {
            var parts = new List<string>();
            foreach (var kv in sfx)
            {
                var c = kv.Value[0];
                var data = new float[c.samples];
                c.GetData(data, 0);
                float peak = 0f, sum = 0f;
                foreach (var v in data) { peak = Mathf.Max(peak, Mathf.Abs(v)); sum += v * v; }
                parts.Add($"{kv.Key}:{c.length:0.00}s/pk{peak:0.00}/rms{Mathf.Sqrt(sum / data.Length):0.000}");
            }
            foreach (var kv in music) parts.Add($"music_{kv.Key}:{kv.Value.length:0.0}s");
            return string.Join(" ", parts);
        }

        // ------------------------------------------------------------------ playback
        static void Count(string key) => Counts[key] = Counts.TryGetValue(key, out int c) ? c + 1 : 1;

        public void Play(string key, Vector3 pos, float volume = 1f, float pitchVar = 0.06f, float pitch = 1f)
        {
            if (!sfx.TryGetValue(key, out var clips) || clips.Length == 0) return;
            var s = pool[poolNext];
            poolNext = (poolNext + 1) % pool.Length;
            s.transform.position = pos;
            s.clip = clips[Random.Range(0, clips.Length)];
            s.pitch = pitch * (1f + Random.Range(-pitchVar, pitchVar));
            s.volume = Mathf.Clamp01(volume * GameSettings.SfxVolume);
            s.Play();
            Count(key);
        }

        /// <summary>A 3D one-shot by name at a position (training partners' murmurs and complaints).</summary>
        public static void At3D(string key, Vector3 pos, float volume = 1f)
        {
            if (Instance != null) Instance.Play(key, pos, volume, 0.08f);
        }

        /// <summary>Menu clicks (2D, keep playing while paused): "ui_move", "ui_select", "ui_back".</summary>
        public static void UI(string key)
        {
            if (Instance == null || !Instance.sfx.TryGetValue(key, out var clips)) return;
            Instance.ui.PlayOneShot(clips[0], 0.8f * GameSettings.SfxVolume);
            Count(key);
        }

        void Play2D(string key, float volume)
        {
            if (!sfx.TryGetValue(key, out var clips)) return;
            ui.PlayOneShot(clips[Random.Range(0, clips.Length)], volume * GameSettings.SfxVolume);
            Count(key);
        }

        static Vector3 At(Fighter f) => f.transform.position + Vector3.up;

        // ------------------------------------------------------------------ events
        void OnSwing(Fighter f, AttackType type)
        {
            string k = type == AttackType.Kick ? "swing_kick" : f.CurrentWeapon == null ? "swing_fist"
                     : type == AttackType.Heavy ? "swing_heavy" : "swing_light";
            Play(k, At(f), 0.75f, 0.08f);
        }

        void OnHit(HitResult r)
        {
            Vector3 p = r.Point;
            switch (r.Outcome)
            {
                case HitOutcome.Hit:
                    if (r.Type == AttackType.Kick) Play("kick_hit", p, 1f);
                    else if (r.Type == AttackType.Heavy) Play("hit_heavy", p, 1f);
                    else Play("hit", p, 0.9f, 0.1f);
                    if (inArena && (r.Type != AttackType.Light) && Random.value < 0.45f) Cheer(false);
                    break;
                case HitOutcome.Blocked:
                    Play("block", p, 0.85f, 0.08f);
                    break;
                case HitOutcome.GuardBreak:
                    Play("guard_break", p, 1f, 0.05f);
                    if (inArena) Cheer(false);
                    break;
            }
            if (r.Disarmed) { Play("pickup", p, 0.8f, 0.1f, 0.8f); if (inArena && r.Power) Cheer(true); }
            if (r.Outcome == HitOutcome.Hit && r.ArmorAbsorbed >= 0.25f) Play("block", p, 0.45f, 0.1f, 0.75f); // the blow rings on armour
        }

        void OnEvaded(Fighter attacker, Fighter target) => Play("whiff", At(target), 0.6f, 0.1f);

        void OnMove(Fighter f, string what)
        {
            switch (what)
            {
                case "jump": case "dodge_back": Play("jump", At(f), 0.7f); break;
                case "land": Play("land", f.transform.position, 0.7f); break;
                case "land_hard": Play("land_hard", f.transform.position, 1f); break;
                case "dodge": Play("roll", f.transform.position, 0.85f); break;
                case "vault": case "climb": Play("climb", At(f), 0.8f, 0.08f, what == "vault" ? 1.15f : 1f); break;
                case "stunned": Play("stun", At(f) + Vector3.up, 0.7f, 0.03f); break;
                case "power_ready": Play("power_ready", At(f), 0.8f, 0.02f); break;
                case "power_combo": Play("power_ready", At(f), 0.45f, 0.02f, 1.3f); break;
                case "throw": Play("swing_heavy", At(f), 0.9f, 0.05f, 0.85f); break;
                case "drop": Play("pickup", f.transform.position, 0.6f, 0.1f, 0.7f); break;
                case "taunt":
                case "cheer":
                    Play("taunt_shout", At(f) + Vector3.up * 0.5f, 0.8f, 0.12f, what == "cheer" ? 1.15f : 1f);
                    if (inArena && what == "cheer") Cheer(false);
                    break;
            }
        }

        void OnWeapon(Fighter f, Weapon w)
        {
            if (w != null && sceneTime > 1.5f) Play("pickup", At(f), 0.7f, 0.08f);
        }

        void OnDied(Fighter f)
        {
            Play("death", f.transform.position, 1f, 0.05f);
            if (inArena) Cheer(true);
        }

        void Cheer(bool big)
        {
            if (!big && Time.unscaledTime < nextCheer) return;
            nextCheer = Time.unscaledTime + (big ? 1.5f : 3.5f);
            Play2D(big ? "cheer" : "cheer_small", big ? 0.75f : 0.4f);
        }

        // ------------------------------------------------------------------ scenes, music, steps
        void OnScene(Scene scene)
        {
            sceneTime = 0f;
            steps.Clear();
            endPlayed = false;
            startHornPlayed = false;
            arenaManager = null;
            inArena = scene.name == "Arena";
            var piece = scene.name == "Arena" ? MusicSynth.Piece.Arena : scene.name == "Training" ? MusicSynth.Piece.Ludus : MusicSynth.Piece.Menu;
            if (!music.TryGetValue(piece, out var clip))
            {
                float t0 = Time.realtimeSinceStartup;
                clip = music[piece] = MusicSynth.Render(piece);
                Debug.Log($"[AUDIO] music {piece} {clip.length:0.0}s rendered in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
            }
            var next = musicOnA ? musicB : musicA;
            if (next.clip != clip || !next.isPlaying)
            {
                var cur = musicOnA ? musicA : musicB;
                if (cur.clip == clip && cur.isPlaying) { /* same piece: keep it */ }
                else
                {
                    next.clip = clip;
                    next.volume = 0f;
                    next.Play();
                    musicOnA = !musicOnA;
                    musicFade = 0f;
                }
            }
            musicTarget = inArena ? 0.32f : 0.5f;
            if (inArena)
            {
                ambience.clip = sfx["crowd_loop"][0];
                if (!ambience.isPlaying) ambience.Play();
            }
            else ambience.Stop();
            ApplyVolumes();
        }

        void ApplyVolumes()
        {
            var cur = musicOnA ? musicA : musicB;
            var old = musicOnA ? musicB : musicA;
            float m = musicTarget * GameSettings.MusicVolume;
            cur.volume = m * musicFade;
            old.volume = m * (1f - musicFade);
            ambience.volume = 0.32f * GameSettings.SfxVolume;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            sceneTime += Time.deltaTime;
            AudioListener.pause = GameInput.Paused;

            if (musicFade < 1f)
            {
                musicFade = Mathf.Min(1f, musicFade + dt / 1.5f);
                if (musicFade >= 1f) (musicOnA ? musicB : musicA).Stop();
            }
            ApplyVolumes();

            if (inArena)
            {
                if (arenaManager == null) arenaManager = FindAnyObjectByType<Arena.ArenaManager>();
                if (!startHornPlayed && sceneTime > 0.4f) { startHornPlayed = true; Play2D("horn_start", 0.6f); }
                if (arenaManager != null && arenaManager.MatchOver && !endPlayed)
                {
                    endPlayed = true;
                    Play2D(arenaManager.PlayerWon ? "horn_win" : "horn_lose", 0.7f);
                    if (arenaManager.PlayerWon) Play2D("cheer", 0.7f);
                }
            }
            Footsteps();
        }

        void LateUpdate()
        {
            Camera cam = PS2RenderSetup.Instance != null ? PS2RenderSetup.Instance.GameCamera : Camera.main;
            if (cam != null) listenerT.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
        }

        /// <summary>One step sound per stride of ground actually covered (quieter when crouching).</summary>
        void Footsteps()
        {
            if (Time.deltaTime <= 0f) return;
            foreach (var f in Fighter.All)
            {
                if (f == null) continue;
                Vector3 p = f.transform.position;
                if (!steps.TryGetValue(f, out var st)) { steps[f] = (p, 0f); continue; }
                Vector3 d = p - st.pos; d.y = 0f;
                float dist = st.dist;
                var s = f.State;
                bool walking = f.IsAlive && f.IsGrounded && f.Controller != null && f.Controller.enabled &&
                               s != FighterState.Dodge && s != FighterState.Airborne && s != FighterState.Traverse && s != FighterState.Stunned;
                float speed = d.magnitude / Time.deltaTime;
                if (!walking || speed < 0.4f || speed > 15f) dist = 0f;
                else
                {
                    dist += d.magnitude;
                    float h = f.Controller.height;
                    float stride = 0.42f * h * (speed > 3.5f ? 1.25f : 1f);
                    if (dist >= stride)
                    {
                        dist = 0f;
                        float vol = Mathf.Lerp(0.25f, 0.5f, Mathf.Clamp01(speed / 5f)) * (f.Crouching ? 0.5f : 1f);
                        Play("step", p, vol, 0.12f);
                    }
                }
                steps[f] = (p, dist);
            }
        }
    }
}
