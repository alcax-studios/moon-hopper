using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// An authored doorway (or stair top) between two rooms; a null room means outside. Positioned at the opening's
    /// center on the floor.
    /// </summary>
    public class CombatDoor : MonoBehaviour
    {
        public CombatRoom a;
        public CombatRoom b;
        public float width = 1.4f;
        public bool stairs;

        public Vector3 Position => transform.position;

        public CombatRoom Other(CombatRoom from) => from == a ? b : a;
        public bool Connects(CombatRoom r) => a == r || b == r;

        void OnDrawGizmos()
        {
            Gizmos.color = stairs ? new Color(1f, 0.8f, 0.3f, 0.8f) : new Color(0.3f, 1f, 0.5f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 1.1f, new Vector3(width, 2.2f, width));
        }
    }
}
