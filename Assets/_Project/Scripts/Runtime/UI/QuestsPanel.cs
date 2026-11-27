using System.Collections.Generic;
using Alcax.Core;
using Alcax.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>The Quests tab: active and completed quests on the left, the selected one's objectives on the right.</summary>
    public class QuestsPanel : MonoBehaviour
    {
        RectTransform root, list;
        TextMeshProUGUI title, giver, summary, objectives, empty;
        Quest selected;
        bool dirty = true;

        public void Build(RectTransform parent)
        {
            root = parent;
            list = UIFactory.Rect("List", root);
            UIFactory.Place(list, new Vector2(0f, 1f), new Vector2(40f, -8f), new Vector2(480f, 720f), new Vector2(0f, 1f));

            var detail = UIFactory.Image("Detail", root, Palette.WithAlpha(Palette.UIDark, 0.6f));
            UIFactory.Place(detail.rectTransform, new Vector2(0f, 1f), new Vector2(560f, -8f), new Vector2(1000f, 720f), new Vector2(0f, 1f));
            var d = detail.transform;
            title = UIFactory.Text("Title", d, "", 34f, Palette.Accent, TextAlignmentOptions.TopLeft, 4f, 0f);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -30f), new Vector2(930f, 44f), new Vector2(0f, 1f));
            giver = UIFactory.Text("Giver", d, "", 15f, Palette.WithAlpha(Palette.UIBone, 0.55f), TextAlignmentOptions.TopLeft, 3f, 0f);
            UIFactory.Place(giver.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -80f), new Vector2(930f, 22f), new Vector2(0f, 1f));
            summary = UIFactory.Body("Summary", d, "", 21f, Palette.UIBone);
            UIFactory.Place(summary.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -120f), new Vector2(920f, 120f), new Vector2(0f, 1f));
            objectives = UIFactory.Body("Objectives", d, "", 21f, Palette.UIBone);
            UIFactory.Place(objectives.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -260f), new Vector2(920f, 420f), new Vector2(0f, 1f));
            empty = UIFactory.Body("Empty", d, "No quests yet. People on the station may have work for you.", 21f, Palette.WithAlpha(Palette.UIBone, 0.5f), TextAlignmentOptions.Center);
            UIFactory.Stretch(empty.rectTransform);
            QuestLog.Changed += MarkDirty;
        }

        void MarkDirty() => dirty = true;
        void OnDestroy() => QuestLog.Changed -= MarkDirty;
        void OnEnable() => dirty = true;

        void Update()
        {
            if (dirty) Rebuild();
        }

        void Rebuild()
        {
            dirty = false;
            foreach (Transform c in list) Destroy(c.gameObject);
            if (selected == null || !QuestLog.All.Contains(selected)) selected = QuestLog.All.Find(q => q.state == QuestState.Active) ?? (QuestLog.All.Count > 0 ? QuestLog.All[0] : null);
            float y = 0f;
            foreach (var (label, state) in new[] { ("ACTIVE", QuestState.Active), ("COMPLETED", QuestState.Completed) })
            {
                var qs = QuestLog.All.FindAll(q => q.state == state);
                if (qs.Count == 0) continue;
                var h = UIFactory.Text("H", list, label, 16f, Palette.WithAlpha(Palette.UIBone, 0.6f), TextAlignmentOptions.MidlineLeft, 5f, 0f);
                UIFactory.Place(h.rectTransform, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(480f, 28f), new Vector2(0f, 1f));
                y -= 34f;
                foreach (var q in qs)
                {
                    var quest = q;
                    int done = q.objectives.FindAll(o => o.Done).Count;
                    var btn = UIFactory.SmallButton(list, $"{q.title.ToUpperInvariant()}   <size=14><color=#E8E1D088>{done}/{q.objectives.Count}</color></size>", () => { selected = quest; dirty = true; }, new Vector2(480f, 52f), 20f);
                    UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(480f, 52f), new Vector2(0f, 1f));
                    if (q == selected) btn.GetComponent<Image>().color = Palette.WithAlpha(Palette.Accent, 0.35f);
                    y -= 58f;
                }
                y -= 14f;
            }

            empty.enabled = selected == null;
            title.text = selected?.title.ToUpperInvariant() ?? "";
            giver.text = selected == null ? "" : (selected.state == QuestState.Completed ? "COMPLETED   ·   " : "") + "FROM  " + (selected.giver ?? "").ToUpperInvariant();
            summary.text = selected?.summary ?? "";
            if (selected == null) { objectives.text = ""; return; }
            var sb = new System.Text.StringBuilder("<b><size=16><color=#E8E1D099>OBJECTIVES</color></size></b>\n");
            foreach (var o in selected.objectives)
            {
                string mark = o.Done ? "<color=#9FE0A0>[x]</color>" : "<color=#F2A23A>[ ]</color>";
                string count = o.need > 1 ? $"  ({o.have}/{o.need})" : "";
                sb.Append(o.Done ? $"{mark}  <color=#E8E1D088><s>{o.text}</s></color>{count}\n" : $"{mark}  {o.text}{count}\n");
            }
            objectives.text = sb.ToString();
        }
    }
}
