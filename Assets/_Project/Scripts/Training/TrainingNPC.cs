using System.Collections;
using System.Collections.Generic;
using Gladiator.Audio;
using Gladiator.Combat;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Training
{
    /// <summary>
    /// A training partner in the ludus: keeps practising at its station forever (the station's own minigame, played
    /// by a bot with a hidden HUD). Hitting it with the wooden gladius does no damage, it just flinches and complains.
    /// E next to it: it mumbles something (no dialogue yet).
    /// </summary>
    public class TrainingNPC : MonoBehaviour
    {
        public static readonly List<TrainingNPC> All = new List<TrainingNPC>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); }

        public Fighter Fighter { get; private set; }
        public TrainingMinigame Station { get; private set; }
        public string DisplayName => Fighter != null ? Fighter.DisplayName : name;
        public int Complaints { get; private set; }

        TrainingHUD hud;
        float lastComplain = -10f;

        static readonly string[] Complain =
        {
            "¡Eh! ¡Mira por donde golpeas!", "¡Ay! ¿Que haces?", "¡Buscate tu propio poste!", "¡Cuidado con esa espada de madera!",
            "¡Que te vea el lanista!", "¡Basta ya!"
        };
        static readonly string[] Murmur = { "Mmm... hmm... mmh.", "Hmm, hmm...", "...mmmh. Mm-hm.", "Hmf... mmm...", "Mmm-mm." };

        public void Begin(Fighter f, TrainingMinigame station, TrainingHUD silentHud, TrainingHUD mainHud)
        {
            Fighter = f;
            Station = station;
            hud = mainHud;
            station.Bot = true;
            station.FreeMode = true;
            station.Occupied = true;
            f.Harmless = true;
            f.DisarmResistance = 1f;
            StartCoroutine(Loop(silentHud));
        }

        void OnEnable() { All.Add(this); Gladiator.Combat.Fighter.AnyHitResolved += OnHit; }
        void OnDisable() { All.Remove(this); Gladiator.Combat.Fighter.AnyHitResolved -= OnHit; }

        IEnumerator Loop(TrainingHUD silent)
        {
            yield return new WaitForSeconds(Random.Range(0.2f, 1.5f));
            while (true)
            {
                Station.StopRequested = false;
                yield return Station.Play(Fighter, silent);
                yield return new WaitForSeconds(Random.Range(1f, 2.5f));
            }
        }

        void OnHit(HitResult r)
        {
            if (r.Target != Fighter || r.Attacker == null) return;
            Complaints++;
            if (Time.time - lastComplain < 1.2f) return;
            lastComplain = Time.time;
            AudioManager.At3D("complain", Fighter.transform.position + Vector3.up * 1.6f, 0.9f);
            if (hud != null) hud.ShowMessage(DisplayName.ToUpperInvariant() + ":  " + Complain[Random.Range(0, Complain.Length)], 1.8f);
        }

        /// <summary>E next to it: a murmur (no dialogue for now).</summary>
        public void Talk()
        {
            AudioManager.At3D("murmur", Fighter.transform.position + Vector3.up * 1.6f, 1f);
            if (hud != null) hud.ShowMessage(DisplayName.ToUpperInvariant() + ":  «" + Murmur[Random.Range(0, Murmur.Length)] + "»", 2.4f);
        }
    }
}
