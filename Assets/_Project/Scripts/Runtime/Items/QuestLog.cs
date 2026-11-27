using System;
using System.Collections.Generic;

namespace Alcax.Items
{
    public enum QuestState { Active, Completed }

    public class QuestObjective
    {
        public string text;
        /// <summary>Progress key, e.g. "talk", "get:weapon", "use:aid". QuestLog.Progress(key) advances it.</summary>
        public string key;
        public int need = 1;
        public int have;
        public bool Done => have >= need;
    }

    public class Quest
    {
        public string id, title, giver, summary;
        public QuestState state;
        public readonly List<QuestObjective> objectives = new List<QuestObjective>();
        public bool Tracked = true;

        public Quest Objective(string text, string key, int need = 1)
        {
            objectives.Add(new QuestObjective { text = text, key = key, need = need });
            return this;
        }
    }

    /// <summary>
    /// The player's quests (kept across map loads). Systems report what happened with Progress("key"); objectives
    /// with that key advance, and a quest completes when all its objectives are done. Content lives in StoryContent
    /// for now; quests can later come from dialogue or data assets.
    /// </summary>
    public static class QuestLog
    {
        public static readonly List<Quest> All = new List<Quest>();
        public static event Action Changed;
        /// <summary>HUD messages ("NEW QUEST: ...", "QUEST COMPLETE: ...").</summary>
        public static event Action<string> Notice;

        /// <summary>Play mode starts without a domain reload: start each session with an empty log.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() { All.Clear(); Changed = null; Notice = null; }

        public static bool Has(string id) => All.Exists(q => q.id == id);

        public static void Add(Quest q, bool announce = true)
        {
            if (q == null || Has(q.id)) return;
            All.Add(q);
            if (announce) Notice?.Invoke("NEW QUEST:  " + q.title.ToUpperInvariant());
            Changed?.Invoke();
        }

        public static void Progress(string key, int n = 1)
        {
            bool any = false;
            foreach (var q in All)
            {
                if (q.state != QuestState.Active) continue;
                foreach (var o in q.objectives)
                {
                    if (o.Done || o.key != key) continue;
                    o.have = Math.Min(o.need, o.have + n);
                    any = true;
                    if (o.Done) Notice?.Invoke("DONE:  " + o.text.ToUpperInvariant());
                }
                if (q.objectives.TrueForAll(o => o.Done))
                {
                    q.state = QuestState.Completed;
                    Notice?.Invoke("QUEST COMPLETE:  " + q.title.ToUpperInvariant());
                }
            }
            if (any) Changed?.Invoke();
        }
    }
}
