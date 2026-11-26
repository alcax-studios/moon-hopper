using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// A room next to an ambience source (see RoomAmbience): its volume in world space, the doorway it is reached
    /// through, the door in that doorway (if any), and the next zone toward the source (none = the source room itself).
    /// </summary>
    public class AmbienceZone : MonoBehaviour
    {
        [SerializeField] Vector3 min, max;
        [SerializeField] AutoDoor door;
        [SerializeField] Transform portal;
        [SerializeField] AmbienceZone next;

        public AutoDoor Door => door;
        public Vector3 Portal => portal != null ? portal.position : transform.position;
        public AmbienceZone Next => next;

        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.6f);
            Gizmos.DrawWireCube((min + max) * 0.5f, max - min);
            Gizmos.DrawSphere(Portal, 0.3f);
        }
    }
}
