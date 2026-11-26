using UnityEngine;

namespace Alcax.Loot
{
    /// <summary>
    /// A restocking display slot (the armory): keeps one pickup here, and puts a fresh one back a while after it's taken.
    /// </summary>
    public class PickupRack : MonoBehaviour
    {
        [SerializeField] GameObject prefab;
        [SerializeField] float restockSeconds = 20f;

        GameObject current;
        float emptySince = -1f;

        void Start() => current = GetComponentInChildren<Pickup>(true) != null ? GetComponentInChildren<Pickup>(true).gameObject : Spawn();

        void Update()
        {
            if (current != null) return;
            if (emptySince < 0f) emptySince = Time.time;
            if (Time.time - emptySince >= restockSeconds) { current = Spawn(); emptySince = -1f; }
        }

        GameObject Spawn()
        {
            if (prefab == null) return null;
            return Instantiate(prefab, transform.position, transform.rotation, transform);
        }
    }
}
