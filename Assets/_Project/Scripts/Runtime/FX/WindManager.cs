using System.Collections.Generic;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// The world's wind for foliage (Alcax/Foliage Lit) and anything else that wants it: a direction and strength,
    /// gusts that roll across the ground, and up to 8 bodies (the player and nearby NPCs) that push plants aside.
    /// One per scene; plants need nothing on them. Strength can be driven by weather (a sandstorm raises it).
    /// </summary>
    public class WindManager : MonoBehaviour
    {
        public static WindManager Instance { get; private set; }

        [Tooltip("Direction the wind blows toward (y is ignored).")]
        public Vector3 direction = new Vector3(1f, 0f, 0.35f);
        [Range(0f, 2f)] public float strength = 0.55f;
        [Tooltip("Size of the gust waves (1/m): smaller is broader gusts.")]
        public float gustScale = 0.06f;
        [Tooltip("How fast gusts travel across the ground, m/s.")]
        public float gustSpeed = 6f;
        [Range(0f, 1.5f)] public float gustStrength = 0.6f;
        [Tooltip("Radius around each body that bends plants.")]
        public float pusherRadius = 0.7f;

        static readonly int DirStrength = Shader.PropertyToID("_WindDirStrength");
        static readonly int Gust = Shader.PropertyToID("_WindGust");
        static readonly int Pushers = Shader.PropertyToID("_WindPushers");
        static readonly int PusherCount = Shader.PropertyToID("_WindPusherCount");
        static readonly List<Transform> bodies = new();
        readonly Vector4[] pushers = new Vector4[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() => bodies.Clear();

        /// <summary>Register something that pushes plants (the player, NPCs). Unregister on disable.</summary>
        public static void AddBody(Transform t) { if (t != null && !bodies.Contains(t)) bodies.Add(t); }
        public static void RemoveBody(Transform t) => bodies.Remove(t);

        void OnEnable() => Instance = this;

        void Start()
        {
            // The player always pushes plants; NPCs register themselves with AddBody
            var player = FindAnyObjectByType<Alcax.Player.PlayerController>();
            if (player != null) AddBody(player.transform);
        }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Update()
        {
            var d = new Vector3(direction.x, 0f, direction.z);
            if (d.sqrMagnitude < 1e-4f) d = Vector3.right;
            d.Normalize();
            Shader.SetGlobalVector(DirStrength, new Vector4(d.x, 0f, d.z, strength));
            Shader.SetGlobalVector(Gust, new Vector4(gustScale, gustSpeed, gustStrength, Time.time));

            // The nearest bodies to the camera get the pusher slots
            var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            bodies.RemoveAll(b => b == null);
            bodies.Sort((a, b) => (a.position - cam).sqrMagnitude.CompareTo((b.position - cam).sqrMagnitude));
            int n = Mathf.Min(bodies.Count, pushers.Length);
            for (int i = 0; i < n; i++)
            {
                var p = bodies[i].position;
                pushers[i] = new Vector4(p.x, p.y, p.z, pusherRadius);
            }
            Shader.SetGlobalVectorArray(Pushers, pushers);
            Shader.SetGlobalFloat(PusherCount, n);
        }

        /// <summary>The wind at a point (for particles, cloth, flags): direction times strength with the gusts.</summary>
        public Vector3 At(Vector3 position)
        {
            var d = new Vector3(direction.x, 0f, direction.z).normalized;
            var gp = new Vector2(position.x - d.x * Time.time * gustSpeed, position.z - d.z * Time.time * gustSpeed) * gustScale;
            float g = 1f + (Mathf.PerlinNoise(gp.x, gp.y) - 0.4f) * 2f * gustStrength;
            return d * strength * g;
        }
    }
}
