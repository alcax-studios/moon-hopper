using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Audio
{
    /// <summary>
    /// Real recordings that replace the synthesized placeholders. Filled automatically from
    /// Assets/_Project/Audio/Overrides/&lt;SfxName&gt;/ by Alcax &gt; Refresh Sound Overrides
    /// (and by every level build). With several clips per sound, one is picked at random each play.
    /// </summary>
    [CreateAssetMenu(menuName = "Alcax/Sound Bank", fileName = "SoundBank")]
    public class SoundBank : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public Sfx id;
            public AudioClip[] clips;
            [Range(0f, 2f)] public float volume = 1f;
        }

        public List<Entry> entries = new();

        public Entry Find(Sfx id) => entries.Find(e => e.id == id && e.clips != null && e.clips.Length > 0);
    }
}
