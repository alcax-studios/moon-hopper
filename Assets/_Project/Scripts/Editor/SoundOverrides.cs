using System;
using System.Collections.Generic;
using Alcax.Audio;
using UnityEditor;
using UnityEngine;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Builds the SoundBank from Assets/_Project/Audio/Overrides/&lt;SfxName&gt;/.
    /// Drop .wav/.ogg/.mp3 files into a folder (for example Overrides/Groan/) and run Alcax > Refresh Sound Overrides.
    /// The level build runs it too. Empty folders keep the synthesized sound.
    /// </summary>
    public static class SoundOverrides
    {
        public const string OverridesRoot = BuildUtil.Root + "/Audio/Overrides";
        public const string BankPath = BuildUtil.Root + "/Audio/Resources/SoundBank.asset";

        [MenuItem("Alcax/Refresh Sound Overrides")]
        public static SoundBank Refresh()
        {
            BuildUtil.EnsureFolder(OverridesRoot);
            BuildUtil.EnsureFolder(BuildUtil.Root + "/Audio/Resources");
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<SoundBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }

            var oldVolumes = new Dictionary<Sfx, float>();
            foreach (var e in bank.entries) oldVolumes[e.id] = e.volume;
            bank.entries.Clear();

            int total = 0;
            foreach (Sfx id in Enum.GetValues(typeof(Sfx)))
            {
                string folder = $"{OverridesRoot}/{id}";
                BuildUtil.EnsureFolder(folder);
                var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
                var list = new List<AudioClip>();
                foreach (var g in guids)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g));
                    if (clip != null) list.Add(clip);
                }
                if (list.Count == 0) continue;
                bank.entries.Add(new SoundBank.Entry
                {
                    id = id,
                    clips = list.ToArray(),
                    volume = oldVolumes.TryGetValue(id, out var v) ? v : 1f,
                });
                total += list.Count;
            }
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SoundOverrides] {bank.entries.Count} sounds overridden with {total} recordings.");
            return bank;
        }
    }
}
