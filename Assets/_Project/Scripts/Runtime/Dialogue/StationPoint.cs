using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Dialogue
{
    public enum PointKind { Seat, Window, Console, Railing, Chat }

    /// <summary>
    /// A place an ambient NPC can go and do something: sit, look out, work, or lean on the railing.
    /// The transform's forward is the direction the NPC faces there. Only one NPC uses a point at a time.
    /// </summary>
    public class StationPoint : MonoBehaviour
    {
        public static readonly List<StationPoint> All = new();

        public PointKind kind;
        [Tooltip("For seats: the point to stand on before sitting down (in front of the seat).")]
        public Vector3 approachOffset = new Vector3(0f, 0f, 0.65f);

        public AmbientNpc Occupant { get; set; }
        public Vector3 ApproachPoint => transform.TransformPoint(approachOffset);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = Alcax.Core.Palette.StTechCyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, 0.2f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, transform.position + Vector3.up * 0.1f + transform.forward * 0.5f);
        }
    }
}
