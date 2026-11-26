using UnityEngine;

namespace Alcax.AI
{
    /// <summary>A patrol loop: its child transforms, in order.</summary>
    public class PatrolRoute : MonoBehaviour
    {
        public int Count => transform.childCount;
        public Vector3 Point(int i) => transform.GetChild(((i % Count) + Count) % Count).position;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            for (int i = 0; i < Count; i++)
            {
                Gizmos.DrawWireSphere(Point(i), 0.3f);
                Gizmos.DrawLine(Point(i), Point(i + 1));
            }
        }
    }
}
