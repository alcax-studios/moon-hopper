using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// An authored room volume (emitted by the level builders): an axis-aligned box centered on this transform.
    /// Rooms and the doors between them form the RoomGraph, the AI's map of "which room, and which way in".
    /// </summary>
    public class CombatRoom : MonoBehaviour
    {
        public string roomName = "Room";
        public Vector3 size = new Vector3(6f, 3f, 6f);

        public Vector3 Center => transform.position;

        public bool Contains(Vector3 p)
        {
            var d = p - transform.position;
            return Mathf.Abs(d.x) <= size.x * 0.5f && Mathf.Abs(d.z) <= size.z * 0.5f && d.y >= -size.y * 0.5f - 0.3f && d.y <= size.y * 0.5f;
        }

        /// <summary>Roughly how far apart two people in this room can be (m).</summary>
        public float Span => Mathf.Sqrt(size.x * size.x + size.z * size.z);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
