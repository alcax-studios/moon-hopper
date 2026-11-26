using UnityEngine;

namespace Alcax.Combat
{
    /// <summary>Put on a child collider (such as a head) to scale the damage it takes.</summary>
    public class HitZone : MonoBehaviour
    {
        public float damageMultiplier = 2f;
        public bool isHead = true;
    }
}
