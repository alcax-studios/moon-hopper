using Alcax.Combat;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Anything the combat AI can see, track, and fight: the player (PlayerTarget) and every operative.
    /// Perception, aim, and the decision layer work against "the target" through this, not against the player.
    /// </summary>
    public interface ICombatTarget
    {
        GameObject gameObject { get; }
        Health Health { get; }
        bool IsDead { get; }
        /// <summary>Not targetable right now (the player in no-clip).</summary>
        bool NoClipping { get; }
        Vector3 Position { get; }
        Vector3 Velocity { get; }
        Vector3 HeadPosition { get; }
        Vector3 Forward { get; }
        bool IsCrouching { get; }
        bool IsReloading { get; }
        bool FlashlightOn { get; }
        float LastShotTime { get; }
        string DisplayName { get; }
        /// <summary>The points vision checks: head, chest, hips.</summary>
        void GetBodyPoints(Vector3[] points);
        /// <summary>A point to aim at: 0 hips, 1 chest, 2 head.</summary>
        Vector3 AimPoint(int part);
    }

    public static class Targets
    {
        /// <summary>The target behind a GameObject (a noise source, a damage source), if it is one.</summary>
        public static ICombatTarget Of(GameObject go)
        {
            if (go == null) return null;
            var p = PlayerTarget.Instance;
            if (p != null && go == p.gameObject) return p;
            return go.GetComponentInParent<Operative>();
        }
    }
}
