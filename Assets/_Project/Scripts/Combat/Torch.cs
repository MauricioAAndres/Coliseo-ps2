using System;
using Gladiator.Character;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Combat
{
    /// <summary>
    /// A lit torch held up in the left hand: a stick, a flickering flame and its light. The player lights it and puts
    /// it out with a key; guards and townsfolk carry one when they are out at night (<see cref="Wants"/>). It goes
    /// away by itself while the left hand is busy (a second weapon, a bow, a two-hander, arms full, sitting, down).
    /// </summary>
    [DefaultExecutionOrder(600)]
    public class Torch : MonoBehaviour
    {
        public Fighter Fighter;
        /// <summary>NPCs: whether they carry it now (night). Null for players: they choose (Fighter.TorchOn).</summary>
        public Func<bool> Wants;
        public bool Shown { get; private set; }
        /// <summary>How far from the camera a torch still casts light (the flame is always drawn).</summary>
        public static float LightRange = 26f;

        Transform vis, flame;
        Light lamp;
        HumanoidPoser poser;
        float seed;

        public static Torch Ensure(Fighter f)
        {
            if (f == null) return null;
            var t = f.GetComponent<Torch>();
            if (t == null) { t = f.gameObject.AddComponent<Torch>(); t.Fighter = f; t.seed = UnityEngine.Random.value * 50f; }
            return t;
        }

        void Build()
        {
            vis = new GameObject("Torch").transform;
            vis.SetParent(transform, false);
            var wood = PS2Materials.Get("furn_dark", PS2Textures.Wood, new Color(0.36f, 0.24f, 0.14f));
            var cloth = PS2Materials.Get("torch_head", PS2Textures.Cloth, new Color(0.2f, 0.16f, 0.12f));
            PS2Spawn.Box(vis, "Stick", new Vector3(0.035f, 0.6f, 0.035f), new Vector3(0f, 0.15f, 0f), wood);
            PS2Spawn.Box(vis, "Head", new Vector3(0.075f, 0.12f, 0.075f), new Vector3(0f, 0.46f, 0f), cloth);
            flame = new GameObject("Flame").transform;
            flame.SetParent(vis, false);
            flame.localPosition = new Vector3(0f, 0.56f, 0f);
            var fire = PS2Materials.Flat(new Color(1f, 0.62f, 0.12f), 0f);
            var core = PS2Materials.Flat(new Color(1f, 0.9f, 0.45f), 0f);
            var a = PS2Spawn.Box(flame, "Fire", new Vector3(0.11f, 0.2f, 0.11f), Vector3.zero, fire, Quaternion.Euler(0f, 45f, 0f));
            var b = PS2Spawn.Box(flame, "Core", new Vector3(0.06f, 0.13f, 0.06f), new Vector3(0f, -0.02f, 0f), core);
            foreach (var r in new[] { a, b }) r.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var r in vis.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lamp = new GameObject("Light").AddComponent<Light>();
            lamp.transform.SetParent(flame, false);
            lamp.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            lamp.type = LightType.Point;
            lamp.range = 9f;
            lamp.intensity = 1.6f;
            lamp.color = new Color(1f, 0.7f, 0.38f);
            lamp.shadows = LightShadows.None;
        }

        void LateUpdate()
        {
            var f = Fighter;
            if (f == null) return;
            if (Wants != null) f.TorchOn = Wants();
            bool busy = f.CurrentWeapon != null && f.WeaponInHand && (f.TwoHanded || f.CurrentWeapon.Stats.IsBow || f.OffWeapon != null);
            bool can = f.IsAlive && !f.KnockedOut && !f.Ragdolled && !f.InActivity && f.Carried == null && !busy && f.transform.position.y > -20f;
            Shown = f.TorchOn && can;
            f.TorchShown = Shown;
            if (!Shown) { if (vis != null && vis.gameObject.activeSelf) vis.gameObject.SetActive(false); return; }
            if (vis == null) Build();
            if (!vis.gameObject.activeSelf) vis.gameObject.SetActive(true);
            if (poser == null) poser = f.GetComponentInChildren<HumanoidPoser>();
            Transform hand = poser != null && poser.Rig != null ? poser.Rig.Bones[SomaSkeleton.LeftHand] : null;
            Vector3 up = (Vector3.up + f.transform.forward * 0.22f - f.transform.right * 0.1f).normalized;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, up) * f.transform.rotation;
            Vector3 at = hand != null ? hand.position : f.transform.position + Vector3.up * 1.2f + f.transform.forward * 0.3f - f.transform.right * 0.3f;
            vis.SetPositionAndRotation(at, rot);
            float n = Mathf.PerlinNoise(Time.time * 9f, seed);
            flame.localScale = new Vector3(0.9f + n * 0.3f, 0.8f + n * 0.55f, 0.9f + n * 0.3f);
            var cam = Camera.main;
            bool near = cam == null || (cam.transform.position - at).sqrMagnitude < LightRange * LightRange;
            if (lamp.enabled != near) lamp.enabled = near;
            if (near) lamp.intensity = 1.35f + n * 0.6f;
        }
    }
}
