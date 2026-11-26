using System.Collections.Generic;
using Alcax.Core;
using Alcax.UI;
using TMPro;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Subtitles for overheard enemy callouts, at the bottom of the screen. Each line names the speaker and
    /// where the voice came from relative to you (ahead, left, right, behind), so a shout is usable information.
    /// </summary>
    public class CalloutSubtitles : MonoBehaviour
    {
        const int MaxLines = 4;
        const float Life = 4f;

        readonly List<(TextMeshProUGUI text, float born)> lines = new();
        RectTransform panel;

        void Awake()
        {
            var canvas = UIFactory.Canvas("CalloutCanvas", transform, 5);
            panel = UIFactory.Place(UIFactory.Rect("Subtitles", canvas.transform), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(900f, 140f), new Vector2(0.5f, 0f));
        }

        void OnEnable() => Callouts.Heard += OnHeard;
        void OnDisable() => Callouts.Heard -= OnHeard;

        void OnHeard(Operative op, string line, bool radio)
        {
            var player = PlayerTarget.Instance;
            string where = "";
            if (player != null)
            {
                var to = (op.transform.position - player.Position).Flat();
                float ang = Vector3.SignedAngle(player.Forward.Flat(), to, Vector3.up);
                where = Mathf.Abs(ang) < 40f ? "ahead" : Mathf.Abs(ang) > 135f ? "behind" : ang > 0f ? "right" : "left";
            }
            string who = $"{op.Callsign.ToUpper()}";
            string tag = radio ? "<color=#8fd3ff>[RADIO]</color> " : "";
            string text = $"{tag}<color=#ffb27a>{who}</color> <color=#9aa3ad><size=70%>({where})</size></color>  {line}";

            if (lines.Count >= MaxLines) { Destroy(lines[0].text.gameObject); lines.RemoveAt(0); }
            var t = UIFactory.Text("Line", panel, text, 22f, Palette.UIBone, TextAlignmentOptions.Bottom, 1f, 0.2f);
            t.richText = true;
            lines.Add((t, Time.unscaledTime));
            Layout();
        }

        void Layout()
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var rt = lines[i].text.rectTransform;
                int fromBottom = lines.Count - 1 - i;
                UIFactory.Place(rt, new Vector2(0.5f, 0f), new Vector2(0f, fromBottom * 30f), new Vector2(900f, 30f), new Vector2(0.5f, 0f));
            }
        }

        void Update()
        {
            bool changed = false;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - lines[i].born;
                var c = lines[i].text.color;
                c.a = Mathf.Clamp01((Life - age) / 0.6f);
                lines[i].text.color = c;
                if (age > Life) { Destroy(lines[i].text.gameObject); lines.RemoveAt(i); changed = true; }
            }
            if (changed) Layout();
            panel.gameObject.SetActive(GameManager.IsPlaying);
        }
    }
}
