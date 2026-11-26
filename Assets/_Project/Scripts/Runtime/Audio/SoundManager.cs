using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Audio
{
    public enum Sfx
    {
        Pickup, Punch, Swing, BluntHit, BladeHit, Pistol, Shotgun, Click, Reload, Equip,
        PlayerHurt, Footstep,
        UIClick, DeathSting,
        MagOut, MagIn, SlideRack, PumpBack, PumpForward, ShellInsert, CasingTink, ShellDrop, MagDrop,
        AmbientBed, TalkBlip, ShipEngine, TargetClang, DoorOpen, DoorClose,
        RifleShot, BulletFlyby, BodyHit, NpcGrunt, Shout, Radio, DialogueReply,
        VoContact, VoContactConfirm, VoCopy, VoCover, VoEngaging, VoChatter,
    }

    /// <summary>
    /// Plays the synthesized clips through a pooled set of sources.
    /// Volume goes through a static gain layer (MusicGain and SfxGain) that the settings sliders control.
    /// Swap this for an AudioMixer once one is authored.
    /// </summary>
    public enum AmbientKind { Forest, Station }

    public class SoundManager : MonoBehaviour
    {
        [SerializeField] AmbientKind ambientKind = AmbientKind.Forest;
        public static SoundManager Instance { get; private set; }

        public static float MusicGain = 0.8f;
        public static float SfxGain = 1f;
        /// <summary>Scales the ambient bed (the station hum). A RoomAmbience sets it so the hum is contained by walls and doors too.</summary>
        public static float AmbientScale = 1f;

        [SerializeField] int poolSize = 24;
        [SerializeField] float ambientVolume = 0.35f;
        [Tooltip("Optional real recordings that replace synthesized sounds (Audio/Overrides/<SfxName>/).")]
        [SerializeField] SoundBank bank;

        readonly Dictionary<Sfx, AudioClip[]> clips = new();
        readonly Dictionary<Sfx, float> volumeScale = new();
        AudioSource[] pool;
        int next;
        AudioSource ambient;

        public AudioSource AmbientSource => ambient;

        void Awake()
        {
            Instance = this;
            if (bank == null) bank = Resources.Load<SoundBank>("SoundBank");
            MusicGain = PlayerPrefs.GetFloat("vol_music", 0.8f);
            SfxGain = PlayerPrefs.GetFloat("vol_sfx", 1f);

            clips[Sfx.Pickup] = new[] { AudioSynth.Pickup() };
            clips[Sfx.Punch] = new[] { AudioSynth.Punch(0), AudioSynth.Punch(1), AudioSynth.Punch(2) };
            clips[Sfx.Swing] = new[] { AudioSynth.Swing(0), AudioSynth.Swing(1) };
            clips[Sfx.BluntHit] = new[] { AudioSynth.BluntHit(0), AudioSynth.BluntHit(1) };
            clips[Sfx.BladeHit] = new[] { AudioSynth.BladeHit(0), AudioSynth.BladeHit(1) };
            clips[Sfx.Pistol] = new[] { AudioSynth.Pistol() };
            clips[Sfx.Shotgun] = new[] { AudioSynth.Shotgun() };
            clips[Sfx.Click] = new[] { AudioSynth.Click() };
            clips[Sfx.Reload] = new[] { AudioSynth.Reload() };
            clips[Sfx.Equip] = new[] { AudioSynth.Equip() };
            clips[Sfx.PlayerHurt] = new[] { AudioSynth.PlayerHurt(0), AudioSynth.PlayerHurt(1) };
            clips[Sfx.Footstep] = new[] { AudioSynth.Footstep(0), AudioSynth.Footstep(1), AudioSynth.Footstep(2) };
            clips[Sfx.UIClick] = new[] { AudioSynth.UIClick() };
            clips[Sfx.DeathSting] = new[] { AudioSynth.DeathSting() };
            clips[Sfx.MagOut] = new[] { AudioSynth.MagOut() };
            clips[Sfx.MagIn] = new[] { AudioSynth.MagIn() };
            clips[Sfx.SlideRack] = new[] { AudioSynth.SlideRack() };
            clips[Sfx.PumpBack] = new[] { AudioSynth.PumpBack() };
            clips[Sfx.PumpForward] = new[] { AudioSynth.PumpForward() };
            clips[Sfx.ShellInsert] = new[] { AudioSynth.ShellInsert(0), AudioSynth.ShellInsert(1) };
            clips[Sfx.CasingTink] = new[] { AudioSynth.CasingTink(0), AudioSynth.CasingTink(1), AudioSynth.CasingTink(2) };
            clips[Sfx.ShellDrop] = new[] { AudioSynth.ShellDrop(0), AudioSynth.ShellDrop(1) };
            clips[Sfx.MagDrop] = new[] { AudioSynth.MagDrop() };
            clips[Sfx.TalkBlip] = new[] { AudioSynth.TalkBlip(0), AudioSynth.TalkBlip(1), AudioSynth.TalkBlip(2) };
            clips[Sfx.ShipEngine] = new[] { AudioSynth.ShipEngine() };
            clips[Sfx.TargetClang] = new[] { AudioSynth.TargetClang(0), AudioSynth.TargetClang(1) };
            clips[Sfx.DoorOpen] = new[] { AudioSynth.Door(true) };
            clips[Sfx.DoorClose] = new[] { AudioSynth.Door(false) };
            clips[Sfx.DialogueReply] = new[] { AudioSynth.TalkBlip(1) };   // replaced by Overrides/DialogueReply
            // Recorded enemy voice lines (Overrides/Vo*); the synthesized shout stands in until a recording exists
            foreach (var vo in new[] { Sfx.VoContact, Sfx.VoContactConfirm, Sfx.VoCopy, Sfx.VoCover, Sfx.VoEngaging, Sfx.VoChatter })
                clips[vo] = new[] { AudioSynth.Shout(2, 0) };
            clips[Sfx.RifleShot] = new[] { AudioSynth.Rifle(0), AudioSynth.Rifle(1) };
            clips[Sfx.BulletFlyby] = new[] { AudioSynth.Flyby(0), AudioSynth.Flyby(1), AudioSynth.Flyby(2) };
            clips[Sfx.BodyHit] = new[] { AudioSynth.BodyHit(0), AudioSynth.BodyHit(1) };
            clips[Sfx.NpcGrunt] = new[] { AudioSynth.Grunt(0), AudioSynth.Grunt(1), AudioSynth.Grunt(2) };
            // Callout voices: index = syllables - 1 (1 to 5), two takes each
            clips[Sfx.Shout] = new[] { AudioSynth.Shout(1, 0), AudioSynth.Shout(2, 0), AudioSynth.Shout(3, 0), AudioSynth.Shout(4, 0), AudioSynth.Shout(5, 0), AudioSynth.Shout(1, 1), AudioSynth.Shout(2, 1), AudioSynth.Shout(3, 1), AudioSynth.Shout(4, 1), AudioSynth.Shout(5, 1) };
            clips[Sfx.Radio] = new[] { AudioSynth.Radio(1, 0), AudioSynth.Radio(2, 0), AudioSynth.Radio(3, 0), AudioSynth.Radio(4, 0), AudioSynth.Radio(5, 0), AudioSynth.Radio(1, 1), AudioSynth.Radio(2, 1), AudioSynth.Radio(3, 1), AudioSynth.Radio(4, 1), AudioSynth.Radio(5, 1) };

            // Real recordings win over synthesis.
            if (bank != null)
                foreach (var e in bank.entries)
                    if (e.clips != null && e.clips.Length > 0)
                    {
                        var set = new AudioClip[e.clips.Length];
                        for (int i = 0; i < set.Length; i++) set[i] = TrimLeadingSilence(e.clips[i]);
                        clips[e.id] = set;
                        volumeScale[e.id] = e.volume;
                        overridden.Add(e.id);
                    }

            pool = new AudioSource[poolSize];
            filters = new AudioLowPassFilter[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 2f;
                src.maxDistance = 45f;
                src.dopplerLevel = 0f;
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.cutoffFrequency = 22000f;
                pool[i] = src;
                filters[i] = lp;
            }

            var ambGo = new GameObject("AmbientBed");
            ambGo.transform.SetParent(transform, false);
            ambient = ambGo.AddComponent<AudioSource>();
            var ambOverride = bank != null ? bank.Find(Sfx.AmbientBed) : null;
            ambient.clip = ambOverride != null ? ambOverride.clips[0]
                : ambientKind == AmbientKind.Station ? AudioSynth.StationHum() : AudioSynth.AmbientBed();
            ambient.loop = true;
            ambient.spatialBlend = 0f;
            ambient.volume = ambientVolume * MusicGain * AmbientScale;
            ambient.Play();
        }

        void Update()
        {
            if (ambient != null) ambient.volume = ambientVolume * MusicGain * AmbientScale;
        }

        /// <summary>
        /// Recordings often start with silence (and mp3 adds encoder padding), which reads as lag on short cues like a
        /// weapon switch. Short clips get their leading silence cut at load, keeping 5 ms before the first audible
        /// sample (2% of peak) so the attack stays intact. Long or streamed clips are left alone.
        /// </summary>
        static AudioClip TrimLeadingSilence(AudioClip c)
        {
            if (c == null || c.length > 6f || c.loadType == AudioClipLoadType.Streaming) return c;
            if (c.loadState != AudioDataLoadState.Loaded && !c.LoadAudioData()) return c;
            int ch = c.channels;
            var data = new float[c.samples * ch];
            if (!c.GetData(data, 0)) return c;
            float peak = 0f;
            foreach (var x in data) peak = Mathf.Max(peak, Mathf.Abs(x));
            if (peak <= 0f) return c;
            int first = 0;
            while (first < data.Length && Mathf.Abs(data[first]) < peak * 0.02f) first++;
            int startFrame = Mathf.Max(0, first / ch - Mathf.RoundToInt(0.005f * c.frequency));
            if (startFrame < Mathf.RoundToInt(0.01f * c.frequency)) return c;   // under 10 ms: not worth a copy
            int frames = c.samples - startFrame;
            var trimmed = new float[frames * ch];
            System.Array.Copy(data, startFrame * ch, trimmed, 0, trimmed.Length);
            var clip = AudioClip.Create(c.name + "_trimmed", frames, ch, c.frequency, false);
            clip.SetData(trimmed, 0);
            return clip;
        }

        /// <summary>The clip for a sound (the override recording if one exists), for looping sources like engines.</summary>
        public static AudioClip GetClip(Sfx id)
        {
            if (Instance == null || !Instance.clips.TryGetValue(id, out var set) || set.Length == 0) return null;
            return set[Random.Range(0, set.Length)];
        }

        public static void SetMusic(float v) { MusicGain = v; PlayerPrefs.SetFloat("vol_music", v); }
        public static void SetSfx(float v) { SfxGain = v; PlayerPrefs.SetFloat("vol_sfx", v); }

        AudioSource NextVoice()
        {
            for (int i = 0; i < pool.Length; i++)
            {
                var s = pool[(next + i) % pool.Length];
                if (!s.isPlaying) { next = (next + i + 1) % pool.Length; return s; }
            }
            var steal = pool[next];
            next = (next + 1) % pool.Length;
            return steal;
        }

        // ---------- Voice management ----------
        // Fast gunfire with long echo tails can stack 20+ copies of one sound: the mix clips (crackle) and Unity starts
        // killing voices abruptly. Each sound gets a cap on concurrent copies; past it, the oldest fades out quickly.
        readonly Dictionary<Sfx, List<AudioSource>> live = new();
        AudioLowPassFilter[] filters;
        Transform listenerT;
        /// <summary>Occlusion and realistic distance falloff on world sounds (switch for A/B).</summary>
        public static bool SpatialRealism = true;

        // How far a kind of sound carries: inverse-distance falloff from min (full volume) to max (where it stops falling).
        static (float min, float max, float occludedGain, float occludedCutoff) Acoustics(Sfx id) => id switch
        {
            Sfx.RifleShot or Sfx.Shotgun or Sfx.Pistol => (6f, 300f, 0.55f, 1600f),     // loud: carries far, punches through walls
            Sfx.BulletFlyby => (1f, 15f, 0.6f, 3000f),
            Sfx.Footstep => (1.5f, 25f, 0.3f, 700f),
            Sfx.VoContact or Sfx.VoContactConfirm or Sfx.VoCopy or Sfx.VoCover or Sfx.VoEngaging or Sfx.VoChatter or Sfx.Shout or Sfx.Radio
                or Sfx.NpcGrunt => (2.5f, 60f, 0.35f, 900f),                                 // voices: muffled hard by a wall
            Sfx.ShipEngine => (20f, 700f, 1f, 22000f),
            _ => (2f, 45f, 0.4f, 1100f),
        };

        static readonly int OcclusionMask = ~((1 << 2) | (1 << 8));   // static world only (not the player or characters)

        /// <summary>Is there solid world between a sound and the listener? (two rays: straight, and a little higher, so a low wall doesn't block a voice)</summary>
        bool Occluded(Vector3 pos, Vector3 ear)
        {
            if (!Physics.Linecast(pos, ear, OcclusionMask, QueryTriggerInteraction.Ignore)) return false;
            return Physics.Linecast(pos + Vector3.up * 0.6f, ear, OcclusionMask, QueryTriggerInteraction.Ignore);
        }
        readonly HashSet<Sfx> overridden = new();
        public static int ClipCount(Sfx id) => Instance != null && Instance.clips.TryGetValue(id, out var set) ? set.Length : 0;

        /// <summary>True if a real recording replaces the synthesized sound.</summary>
        public static bool HasOverride(Sfx id) => Instance != null && Instance.overridden.Contains(id);
        /// <summary>Voice caps and tail ducking on (switch for A/B testing).</summary>
        public static bool LimitVoices = true;

        static int CapFor(Sfx id) => id switch
        {
            Sfx.RifleShot => 6, Sfx.Shotgun => 4, Sfx.Pistol => 4, Sfx.Footstep => 8, Sfx.BulletFlyby => 4,
            Sfx.BodyHit => 4, Sfx.CasingTink => 4, Sfx.ShellDrop => 3, Sfx.NpcGrunt => 3, Sfx.Shout => 3, Sfx.Radio => 3,
            Sfx.VoContact => 2, Sfx.VoContactConfirm => 2, Sfx.VoCopy => 2, Sfx.VoCover => 2, Sfx.VoEngaging => 2, Sfx.VoChatter => 2,
            _ => 8,
        };

        void Track(Sfx id, AudioSource src)
        {
            if (!live.TryGetValue(id, out var list)) { list = new List<AudioSource>(8); live[id] = list; }
            list.RemoveAll(x => x == null || !x.isPlaying);
            int cap = CapFor(id);
            while (list.Count >= cap)
            {
                var oldest = list[0];
                list.RemoveAt(0);
                StartCoroutine(FadeStop(oldest, 0.06f));
            }
            // Gunfire: each new shot ducks the tails still ringing, so the newest is crisp and the total stays bounded
            if (id == Sfx.RifleShot || id == Sfx.Shotgun || id == Sfx.Pistol)
                foreach (var other in list) if (other != null && other.isPlaying) StartCoroutine(DuckRoutine(other, 0.5f, 0.08f));
            list.Add(src);
        }

        System.Collections.IEnumerator FadeStop(AudioSource s, float seconds)
        {
            float start = s.volume, t = 0f;
            var clip = s.clip;
            while (t < seconds && s.isPlaying && s.clip == clip)
            {
                t += Time.unscaledDeltaTime;
                s.volume = start * (1f - t / seconds);
                yield return null;
            }
            if (s.clip == clip) s.Stop();
        }

        /// <summary>Fade a playing sound to a fraction of its volume (tail ducking under a newer shot).</summary>
        public static void Duck(AudioSource s, float toFraction, float seconds)
        {
            if (Instance == null || s == null || !s.isPlaying) return;
            Instance.StartCoroutine(Instance.DuckRoutine(s, toFraction, seconds));
        }

        System.Collections.IEnumerator DuckRoutine(AudioSource s, float toFraction, float seconds)
        {
            float start = s.volume, target = start * toFraction, t = 0f;
            var clip = s.clip;
            while (t < seconds && s.isPlaying && s.clip == clip)
            {
                t += Time.unscaledDeltaTime;
                s.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }
        }

        AudioSource PlayInternal(Sfx id, float volume, float pitchVariance, bool spatial, Vector3 pos)
        {
            if (!clips.TryGetValue(id, out var set) || set.Length == 0) return null;
            var src = NextVoice();
            src.Stop();
            src.clip = set[Random.Range(0, set.Length)];
            src.volume = volume * SfxGain * (volumeScale.TryGetValue(id, out var vs) ? vs : 1f);
            src.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
            src.spatialBlend = spatial ? 1f : 0f;
            src.transform.position = pos;
            int idx = System.Array.IndexOf(pool, src);
            var lpf = idx >= 0 && filters != null ? filters[idx] : null;
            if (lpf != null) lpf.cutoffFrequency = 22000f;
            if (spatial && SpatialRealism)
            {
                var ac = Acoustics(id);
                src.rolloffMode = AudioRolloffMode.Logarithmic;   // inverse distance: about a quarter as loud at 4x the distance
                src.minDistance = ac.min;
                src.maxDistance = ac.max;
                if (listenerT == null) { var al = FindAnyObjectByType<AudioListener>(); if (al != null) listenerT = al.transform; }
                if (listenerT != null && Occluded(pos, listenerT.position))
                {
                    src.volume *= ac.occludedGain;
                    if (lpf != null) lpf.cutoffFrequency = ac.occludedCutoff;
                }
            }
            else if (spatial)
            {
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 2f;
                src.maxDistance = 45f;
            }
            // Stingers and UI keep playing through pause and hit-stop.
            src.ignoreListenerPause = !spatial;
            src.Play();
            if (LimitVoices) Track(id, src);
            return src;
        }

        public static AudioSource Play2D(Sfx id, float volume = 1f, float pitchVariance = 0.05f)
        {
            if (Instance == null) return null;
            return Instance.PlayInternal(id, volume, pitchVariance, false, Vector3.zero);
        }

        /// <summary>Play a specific clip of a set at a fixed pitch (a speaker's voice).</summary>
        public static AudioSource PlayVoiceAt(Sfx id, int variant, Vector3 position, float volume, float pitch)
        {
            if (Instance == null || !Instance.clips.TryGetValue(id, out var set) || set.Length == 0) return null;
            var src = Instance.PlayInternal(id, volume, 0f, true, position);
            if (src == null) return null;
            src.Stop();
            src.clip = set[Mathf.Clamp(variant, 0, set.Length - 1)];
            src.pitch = pitch;
            src.Play();
            return src;
        }

        public static AudioSource PlayAt(Sfx id, Vector3 position, float volume = 1f, float pitchVariance = 0.08f)
        {
            if (Instance == null) return null;
            return Instance.PlayInternal(id, volume, pitchVariance, true, position);
        }
    }
}
