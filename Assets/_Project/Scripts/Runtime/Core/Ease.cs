using System;
using System.Collections;
using UnityEngine;

namespace Alcax.Core
{
    /// <summary>Easing curves and a coroutine tween. Tweens run on unscaled time by default.</summary>
    public static class Ease
    {
        public static float Linear(float t) => Mathf.Clamp01(t);

        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float f = 1f - t;
            return 1f - f * f * f;
        }

        public static float InOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }

        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        public static float OutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c4 = 2f * Mathf.PI / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }

        public static IEnumerator Tween(float duration, Action<float> apply, Func<float, float> ease, bool unscaled = true)
        {
            if (duration <= 0f) { apply(1f); yield break; }
            for (float t = 0f; t < 1f; t += (unscaled ? Time.unscaledDeltaTime : Time.deltaTime) / duration)
            {
                apply(ease(t));
                yield return null;
            }
            apply(1f);
        }
    }
}
