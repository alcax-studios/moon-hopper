using Alcax.Audio;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// A room's ambience that stays contained by its walls, after the "rooms and portals" model used by game audio
    /// middleware (Wwise Spatial Audio, Steam Audio). Inside the source room (this object's bounds) the loop plays all
    /// around the listener at full clarity. Outside it, the sound reaches the listener only through doorways: it is
    /// emitted from the nearest doorway on the path back to the room, and every door on that path attenuates and
    /// low-passes it (an open doorway lets most of it through, a closed door leaves a dull rumble through the wall),
    /// plus distance from each opening. Everything is smoothed, so walking through and doors closing blend naturally.
    /// Two players crossfade at the loop point, so recordings that weren't made to loop don't click.
    /// </summary>
    public class RoomAmbience : MonoBehaviour
    {
        [SerializeField] AudioClip clip;
        [SerializeField] float volume = 0.55f;
        [Tooltip("The source room, in world space.")]
        [SerializeField] Vector3 roomMin, roomMax;
        [Tooltip("Rooms reachable from the source through doors (each zone points to the next zone toward the source).")]
        [SerializeField] AmbienceZone[] zones;
        [SerializeField] float crossfade = 2f;

        [Header("Transmission")]
        [Tooltip("How much gets through a closed door (0 to 1).")]
        [SerializeField] float closedDoorGain = 0f;
        [SerializeField] float closedDoorCutoff = 330f;
        [SerializeField] float openDoorCutoff = 5500f;
        [Tooltip("Distance (m) from an opening at which the sound has halved.")]
        [SerializeField] float halfDistance = 5f;
        [Tooltip("Also contain the station hum (SoundManager ambient bed) with this room.")]
        [SerializeField] bool containStationHum = true;

        AudioSource a, b;
        AudioLowPassFilter fa, fb;
        bool aActive = true;
        float gain, cutoff = 22000f, spatial;
        Transform listener;
        Vector3 emitPos;

        /// <summary>Debug: what the listener currently gets (gain 0 to 1, low-pass cutoff Hz, and which room).</summary>
        public float CurrentGain => gain;
        public float CurrentCutoff => cutoff;
        public string Where { get; private set; } = "";

        /// <summary>How much of this room (and anything heard through it, like the shuttles outside the window) reaches the listener: gain 0 to 1 and a low-pass cutoff. 1 / 22 kHz where no RoomAmbience exists.</summary>
        public static float Containment = 1f, ContainmentCutoff = 22000f;

        void Start()
        {
            if (clip == null) { enabled = false; return; }
            a = MakePlayer("AmbienceA", out fa);
            b = MakePlayer("AmbienceB", out fb);
            a.Play();
            var cam = Camera.main;
            listener = cam != null ? cam.transform : null;
        }

        AudioSource MakePlayer(string name, out AudioLowPassFilter filter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = false;
            s.playOnAwake = false;
            s.volume = 0f;
            s.dopplerLevel = 0f;
            s.spread = 140f;
            s.rolloffMode = AudioRolloffMode.Linear;   // distance is handled here, through the doorways
            s.minDistance = 1f;
            s.maxDistance = 1000f;
            filter = go.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = 22000f;
            filter.lowpassResonanceQ = 1f;
            return s;
        }

        bool InRoom(Vector3 p) => p.x >= roomMin.x && p.x <= roomMax.x && p.y >= roomMin.y && p.y <= roomMax.y && p.z >= roomMin.z && p.z <= roomMax.z;

        void Update()
        {
            if (listener == null) { var cam = Camera.main; if (cam == null) return; listener = cam.transform; }
            var L = listener.position;

            // Where the sound reaches the listener from, and how much of it
            float targetGain, targetCutoff, targetSpatial;
            Vector3 emitFrom = L;
            if (InRoom(L))
            {
                targetGain = 1f; targetCutoff = 22000f; targetSpatial = 0f; Where = "in the room";
            }
            else
            {
                AmbienceZone zone = null;
                if (zones != null) foreach (var z in zones) if (z != null && z.Contains(L)) { zone = z; break; }
                if (zone == null) { targetGain = 0f; targetCutoff = 400f; targetSpatial = 1f; Where = "out of range"; }
                else
                {
                    Where = "via " + zone.name;
                    targetGain = 1f; targetCutoff = 22000f; targetSpatial = 0.85f;
                    emitFrom = zone.Portal;
                    var from = L;
                    int guard = 0;
                    for (var z = zone; z != null && guard++ < 8; z = z.Next)
                    {
                        float open = z.Door != null ? z.Door.Openness : 1f;
                        float d = Vector3.Distance(from, z.Portal);
                        targetGain *= Mathf.Lerp(closedDoorGain, 1f, open) / (1f + d / halfDistance);
                        // Bending around an opening and passing a wall both lose the highs; distance loses a little more
                        float c = Mathf.Lerp(closedDoorCutoff, openDoorCutoff, open) * Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(d / 20f));
                        targetCutoff = Mathf.Min(targetCutoff, c);
                        from = z.Portal;
                    }
                }
            }

            float k = 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime);
            gain = Mathf.Lerp(gain, targetGain, k);
            // Cutoff moves on a log scale so the muffling sounds even
            cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(cutoff), Mathf.Log(Mathf.Max(80f, targetCutoff)), k));
            spatial = Mathf.Lerp(spatial, targetSpatial, k);

            // Move only the players (the zones and their portals hang under this object and must stay put)
            emitPos = spatial > 0.01f ? emitFrom : L;
            Crossfade();
            float v = volume * gain * SoundManager.MusicGain;
            if (containStationHum) SoundManager.AmbientScale = gain;
            Containment = gain;
            ContainmentCutoff = cutoff;
            Apply(a, fa, v, aActive ? FadeIn(a) * FadeOut(a) : FadeOut(a));
            Apply(b, fb, v, !aActive ? FadeIn(b) * FadeOut(b) : FadeOut(b));
        }

        void Apply(AudioSource s, AudioLowPassFilter f, float v, float fade)
        {
            s.volume = v * fade;
            s.spatialBlend = spatial;
            f.cutoffFrequency = cutoff;
            s.transform.position = emitPos;
        }

        // Loop crossfade: start the other player before this one ends
        void Crossfade()
        {
            var cur = aActive ? a : b;
            var other = aActive ? b : a;
            if (cur.isPlaying && cur.time >= clip.length - crossfade && !other.isPlaying)
            {
                other.time = 0f;
                other.Play();
                aActive = !aActive;
            }
            if (!cur.isPlaying && !other.isPlaying) { cur.time = 0f; cur.Play(); }
        }

        float FadeIn(AudioSource s) => s.isPlaying ? Mathf.Clamp01(s.time / crossfade) : 0f;
        float FadeOut(AudioSource s) => s.isPlaying ? Mathf.Clamp01((clip.length - s.time) / crossfade) : 0f;

        void OnDestroy() { if (containStationHum) SoundManager.AmbientScale = 1f; Containment = 1f; ContainmentCutoff = 22000f; }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube((roomMin + roomMax) * 0.5f, roomMax - roomMin);
        }
    }
}
