using Alcax.Audio;
using Alcax.Player;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// A sliding automatic door. It opens fast when someone (the player, or any character on the Actor layer:
    /// residents, staff, guards) is right at the doorway, within a short reach on either side and only across the door's
    /// own width, so walking past nearby doors doesn't open them all. It closes again a moment after the doorway clears.
    /// Locked doors never open. The leaves are left out of the NavMesh bake, so people path straight through.
    /// </summary>
    public class AutoDoor : MonoBehaviour
    {
        [SerializeField] Transform[] leaves;
        [Tooltip("Local-space offset each leaf slides by when fully open (same order as leaves).")]
        [SerializeField] Vector3[] openOffsets;
        [Tooltip("Legacy: radius trigger (unused; the doorway box below decides).")]
#pragma warning disable 0414
        [SerializeField] float openDistance = 4f;
#pragma warning restore 0414
        [Tooltip("How far in front of and behind the door plane someone triggers it (m).")]
        [SerializeField] float reach = 1.5f;
        [SerializeField] float openTime = 0.2f;
        [Tooltip("Seconds the doorway must be clear before closing.")]
        [SerializeField] float closeDelay = 0.5f;
        [SerializeField] bool locked;
        [SerializeField] Renderer statusLight;
        [SerializeField] Material openMaterial;
        [SerializeField] Material lockedMaterial;

        const int ActorMask = 1 << 8;
        static readonly Collider[] hits = new Collider[8];

        Vector3[] closedPos;
        Transform player;
        Bounds zone;
        float t;          // 0 closed, 1 open
        bool wantOpen;
        float lastOccupied = -100f, nextScan;

        public bool Locked { get => locked; set { locked = value; UpdateStatus(); } }
        /// <summary>0 closed to 1 fully open (eased, as the leaves move).</summary>
        public float Openness => t * t * (3f - 2f * t);

        void Awake()
        {
            closedPos = new Vector3[leaves.Length];
            for (int i = 0; i < leaves.Length; i++) closedPos[i] = leaves[i].localPosition;
            // The doorway trigger: the closed leaves' footprint, stretched across the door plane by the reach
            var b = new Bounds(transform.position + Vector3.up * 1.2f, Vector3.zero);
            bool any = false;
            foreach (var l in leaves)
                foreach (var r in l.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            var size = b.size;
            bool thinX = size.x < size.z;
            size = thinX ? new Vector3(size.x + reach * 2f, Mathf.Max(size.y, 2.2f), size.z + 0.3f)
                         : new Vector3(size.x + 0.3f, Mathf.Max(size.y, 2.2f), size.z + reach * 2f);
            zone = new Bounds(b.center, size);
        }

        void Start()
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (statusLight == null) return;
            var m = locked ? lockedMaterial : openMaterial;
            if (m != null) statusLight.sharedMaterial = m;
        }

        bool Occupied()
        {
            if (player != null)
            {
                var p = player.position + Vector3.up * 0.9f;
                if (zone.Contains(p)) return true;
            }
            if (Time.time < nextScan) return Time.time - lastOccupied < 0.12f;
            nextScan = Time.time + 0.08f;
            return Physics.OverlapBoxNonAlloc(zone.center, zone.extents, hits, Quaternion.identity, ActorMask, QueryTriggerInteraction.Ignore) > 0;
        }

        void Update()
        {
            if (!locked && Occupied()) lastOccupied = Time.time;
            bool open = !locked && Time.time - lastOccupied < closeDelay;
            if (open != wantOpen)
            {
                wantOpen = open;
                SoundManager.PlayAt(open ? Sfx.DoorOpen : Sfx.DoorClose, transform.position + Vector3.up * 1.5f, 0.55f, 0.06f);
            }
            t = Mathf.MoveTowards(t, wantOpen ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, openTime));
            float k = t * t * (3f - 2f * t);
            for (int i = 0; i < leaves.Length; i++)
                leaves[i].localPosition = closedPos[i] + openOffsets[i] * k;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.4f);
            Gizmos.DrawWireCube(Application.isPlaying ? zone.center : transform.position + Vector3.up * 1.2f, Application.isPlaying ? zone.size : Vector3.one * reach);
        }
    }
}
