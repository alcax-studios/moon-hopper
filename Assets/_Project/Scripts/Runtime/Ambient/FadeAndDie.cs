using UnityEngine;

namespace Alcax.Ambient
{
    /// <summary>Fades a light out over a short time, then removes it (an explosion flash).</summary>
    public class FadeAndDie : MonoBehaviour
    {
        Light l;
        float start, life, from;

        public void Init(Light light, float seconds)
        {
            l = light; life = seconds; start = Time.time; from = light.intensity;
        }

        void Update()
        {
            float k = (Time.time - start) / Mathf.Max(0.01f, life);
            if (l != null) l.intensity = from * (1f - k) * (1f - k);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
