using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Layer setup for combat. The player and lobby NPCs sit on Ignore Raycast (2). Combat characters sit on
    /// Actor (8) so bullets hit their body-part colliders, while their own foot IK and vision rays skip them.
    /// </summary>
    public static class AILayers
    {
        public const int IgnoreRaycast = 2;
        public const int Actor = 8;
        public const int Glass = 9;

        /// <summary>Static world only: floors, walls, cover. Used for vision, cover tests, and foot IK. Glass is see-through, so it's not in here.</summary>
        public static readonly int World = ~((1 << IgnoreRaycast) | (1 << Actor) | (1 << Glass));

        /// <summary>What a bullet can hit: the world plus combat characters (the player is tested separately).</summary>
        public static readonly int Bullet = ~(1 << IgnoreRaycast);
    }
}
