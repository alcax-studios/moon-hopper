using UnityEngine;

namespace Alcax.Ambient
{
    /// <summary>The open air flying robots may use (the atrium). Drones pick waypoints inside it.</summary>
    public class FlightVolume : MonoBehaviour
    {
        public static FlightVolume Main { get; private set; }
        public Vector3 size = new Vector3(40f, 10f, 30f);

        public Bounds Bounds => new Bounds(transform.position, size);

        void OnEnable() => Main = this;
        void OnDisable() { if (Main == this) Main = null; }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.3f);
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
