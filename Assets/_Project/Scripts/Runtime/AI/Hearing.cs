using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alcax.AI
{
    public enum NoiseKind { Footstep, Gunshot, Impact, Reload, Voice, Melee, Door }

    public struct NoiseEvent
    {
        public Vector3 Position;
        public float Radius;        // audible distance in open air
        public NoiseKind Kind;
        public GameObject Source;
        public float Time;
    }

    /// <summary>
    /// The world's sound events that AI can hear. Anything noisy calls Emit, and every listener
    /// decides for itself whether it heard it (distance, plus walls muffling the sound).
    /// </summary>
    public static class Hearing
    {
        static readonly List<Action<NoiseEvent>> listeners = new();

        public static void Listen(Action<NoiseEvent> onHear) { if (!listeners.Contains(onHear)) listeners.Add(onHear); }
        public static void Stop(Action<NoiseEvent> onHear) => listeners.Remove(onHear);

        public static void Emit(Vector3 position, float radius, NoiseKind kind, GameObject source)
        {
            var e = new NoiseEvent { Position = position, Radius = radius, Kind = kind, Source = source, Time = UnityEngine.Time.time };
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                if (i < listeners.Count) listeners[i]?.Invoke(e);
            }
        }

        /// <summary>How loud a noise is at a listener (0 to 1), muffled by a wall in between.</summary>
        public static float Loudness(NoiseEvent e, Vector3 ear)
        {
            float d = Vector3.Distance(e.Position, ear);
            float radius = e.Radius;
            if (Physics.Linecast(e.Position + Vector3.up * 0.3f, ear, AILayers.World, QueryTriggerInteraction.Ignore)) radius *= 0.4f;
            return Mathf.Clamp01(1f - d / Mathf.Max(0.1f, radius));
        }
    }
}
