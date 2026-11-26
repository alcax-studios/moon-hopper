using UnityEngine;

namespace Alcax.Core
{
    /// <summary>Slowly orbits the title-screen camera around the campsite.</summary>
    public class TitleOrbit : MonoBehaviour
    {
        [SerializeField] float degreesPerSecond = 5f;

        void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.unscaledDeltaTime, 0f, Space.World);
        }
    }
}
