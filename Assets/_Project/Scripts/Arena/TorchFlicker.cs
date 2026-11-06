using UnityEngine;

namespace Gladiator.Arena
{
    public class TorchFlicker : MonoBehaviour
    {
        public Light Light;
        public Transform Flame;
        public float BaseIntensity = 2f;
        float seed;

        void Awake() { seed = Random.value * 100f; }

        void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * 6f);
            if (Light != null) Light.intensity = BaseIntensity * (0.75f + 0.5f * n);
            if (Flame != null) Flame.localScale = new Vector3(1f, 0.8f + 0.5f * n, 1f);
        }
    }
}
