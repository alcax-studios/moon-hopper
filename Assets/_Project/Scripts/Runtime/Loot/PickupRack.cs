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
        [Tooltip("What it puts out is station property (taking it is stealing).")]
        [SerializeField] bool stationProperty;
        [SerializeField] string owner;

        GameObject current;
        float emptySince = -1f;

        void Start()
        {
            var p = GetComponentInChildren<Pickup>(true);
            current = p != null ? p.gameObject : Spawn();
            Mark(current);
        }

        void Mark(GameObject go)
        {
            if (go == null || !go.TryGetComponent<Pickup>(out var p)) return;
            p.stationProperty = stationProperty;
            p.owner = owner;
        }

        void Update()
        {
            if (current != null) return;
            if (emptySince < 0f) emptySince = Time.time;
            if (Time.time - emptySince >= restockSeconds) { current = Spawn(); Mark(current); emptySince = -1f; }
        }

        GameObject Spawn()
        {
            if (prefab == null) return null;
            return Instantiate(prefab, transform.position, transform.rotation, transform);
        }
    }
}
