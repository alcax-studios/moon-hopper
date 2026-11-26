using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.AI
{
    /// <summary>
    /// F3 cycles the AI debug view: Off, Full (a panel per operative), Compact (one line each).
    /// Full: action and top scores, style, faction, wound, emotions, confidence, health, ammo, and the last callout.
    /// Whenever it's on: a squad panel (fighting count, confidence, leader, last order), the COMMS log, and in-world
    /// lines for beliefs, cover, hidden routes, gaze, and danger spots. Everything scales with the screen (DevGUI).
    /// </summary>
    public class AIDebugOverlay : MonoBehaviour
    {
        public enum Mode { Off, Full, Compact }
        public static Mode View;
        public static bool Enabled => View != Mode.Off;

        GUIStyle label, small, head;
        Texture2D white;
        Material lineMat;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) View = (Mode)(((int)View + 1) % 3);
        }

        void EnsureStyles()
        {
            if (label != null) return;
            white = Texture2D.whiteTexture;
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, richText = true, wordWrap = false };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = false };
            head = new GUIStyle(label) { fontSize = 15 };
        }

        void Box(Rect r, float a = 0.62f)
        {
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(r, white);
            GUI.color = Color.white;
        }

        static string StateColor(Operative op) => op.Surrendered ? "#bbbbbb" : op.Broken ? "#c9b6ff" : op.Perception.State switch
        {
            Alertness.Combat => "#ff5a3c", Alertness.Alert => "#ffb03c", Alertness.Suspicious => "#ffe27a", _ => "#9fe0a0",
        };

        static string StateText(Operative op) => op.Surrendered ? "SURRENDERED" : op.Broken ? "BROKEN" : op.Perception.State.ToString();

        static Color ConfColor(float c) => c >= 0f
            ? Color.Lerp(new Color(0.8f, 0.8f, 0.8f), new Color(0.35f, 1f, 0.45f), c)
            : Color.Lerp(new Color(0.8f, 0.8f, 0.8f), new Color(0.7f, 0.45f, 1f), -c);

        void OnGUI()
        {
            if (!Enabled) return;
            var cam = Camera.main;
            if (cam == null) return;
            EnsureStyles();
            float k = DevGUI.Scale;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float W = Screen.width / k, H = Screen.height / k;

            foreach (var op in Operative.All)
            {
                if (op == null || op.Perception == null) continue;
                var sp = cam.WorldToScreenPoint(op.EyePosition + Vector3.up * 0.45f);
                if (sp.z <= 0f || sp.z > 90f) continue;
                float sx = sp.x / k, sy = (Screen.height - sp.y) / k;
                if (View == Mode.Compact || sp.z > 45f) Compact(op, sx, sy);
                else Full(op, sx, sy);
            }

            SquadPanel(H);
            // Comms log: every callout, radio (R) and shouted (S), marked when the player could not hear it
            float cw = 430f, lh = 17f;
            Box(new Rect(W - cw - 12, 12, cw, 24 + Callouts.Log.Count * lh));
            GUI.Label(new Rect(W - cw - 4, 13, cw, 20), "<b>COMMS</b>", head);
            for (int i = 0; i < Callouts.Log.Count; i++)
                GUI.Label(new Rect(W - cw - 4, 33 + i * lh, cw - 8, 18), Callouts.Log[i].text, small);
            GUI.Label(new Rect(12, H - 26, 760, 22), $"<b>AI DEBUG</b>  F3: {View} (cycles Off / Full / Compact)   F1 help", small);
            GUI.matrix = saved;
        }

        void Compact(Operative op, float sx, float sy)
        {
            string t = op.IsDead
                ? $"<color=#888>{op.Callsign} DOWN</color>"
                : $"{op.Callsign} <color={StateColor(op)}>{(op.Broken || op.Surrendered ? StateText(op) : op.Mind.Current.ToString())}</color>";
            var r = new Rect(sx - 85, sy - 36, 170, 20);
            Box(new Rect(r.x, r.y, r.width, op.IsDead ? 20 : 29), 0.5f);
            GUI.Label(new Rect(r.x + 4, r.y, r.width, r.height), t, label);
            if (op.IsDead) return;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(r.x + 5, r.y + 21, 78, 5), white);
            GUI.color = new Color(0.4f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(r.x + 5, r.y + 21, 78 * op.Health.Normalized, 5), white);
            CenteredBar(r.x + 88, r.y + 21, 76, op.Confidence);
            GUI.color = Color.white;
        }

        void Full(Operative op, float sx, float sy)
        {
            const float w = 250f, hgt = 258f;
            float x = sx - w * 0.5f, y = sy - hgt - 6f;
            Box(new Rect(x - 5, y - 3, w + 10, op.IsDead ? 24 : hgt));
            if (op.IsDead) { GUI.Label(new Rect(x, y, w, 20), "<color=#888>" + op.Callsign + " " + op.Archetype.displayName + "  DOWN</color>", label); return; }

            var m = op.Mind;
            string fac = op.Faction != null ? op.Faction.displayName : "-";
            bool lead = op.Squad != null && op.Squad.Leader == op;
            GUI.Label(new Rect(x, y, w, 20), $"{op.Callsign} {op.Archetype.displayName}{(lead ? " ★" : "")}  <color={StateColor(op)}>{StateText(op)}</color>", label);
            y += 19;
            string wound = op.Wound == Wound.Healthy ? "" : op.LastStand ? " <color=#ff5a3c>LAST STAND</color>" : $" <color=#ffb03c>{op.Wound}</color>";
            GUI.Label(new Rect(x, y, w, 18), $"<color=#c9b6ff>{op.Style}/{op.SecondaryStyle}</color>  {fac}{wound}", small);
            y += 17;
            GUI.Label(new Rect(x, y, w, 20), $"<color=#7fd7ff>{m.Current}</color> {op.Note}{(m.OrderNote != "" ? " <color=#ffd27a>[order: " + m.OrderNote + "]</color>" : "")}", label);
            y += 19;
            var top = Enumerable.Range(0, OperativeMind.ActionCount).OrderByDescending(i => m.Scores[i]).Take(3)
                .Select(i => $"{(OpAction)i} {m.Scores[i]:0.00}");
            GUI.Label(new Rect(x, y, w, 18), string.Join("  ", top), small);
            y += 19;
            Bar(x, ref y, "aware", op.Perception.Awareness, new Color(1f, 0.85f, 0.3f));
            Bar(x, ref y, "fear", op.Emotion.fear, new Color(0.6f, 0.5f, 1f));
            Bar(x, ref y, "anger", op.Emotion.anger, new Color(1f, 0.35f, 0.3f));
            Bar(x, ref y, "stress", op.Emotion.stress, new Color(1f, 0.6f, 0.2f));
            Bar(x, ref y, "health", op.Health.Normalized, new Color(0.4f, 1f, 0.5f));
            GUI.Label(new Rect(x, y - 3, 70, 18), "confid.", small);
            CenteredBar(x + 58, y + 2, w - 64, op.Confidence);
            y += 15;
            if (op.Attention != null) GUI.Label(new Rect(x + 150, y - 15, 110, 18), $"<color=#9fdcff>aim: {op.AimSourceName}  look: {op.Attention.Why}</color>  <color={(op.Overloaded ? "#ff8a5c" : "#aaa")}>load {op.Load:0.0}/{op.Capacity:0.0} fov {op.Perception.EffectivePeripheral:0}°</color>", small);
            GUI.Label(new Rect(x, y, w, 18), $"{(op.PlayerKnowsMe ? "<color=#ff8a5c>you know where I am</color>" : "<color=#9fe0a0>unseen by you</color>")}  patches {op.PatchesLeft}", small);
            y += 16;
            GUI.Label(new Rect(x, y, w, 18), $"<color=#ffd27a>target: {(op.Target != null ? op.Target.DisplayName : "none")}</color>  contacts {op.ContactCount}{(op.HostileToPlayer ? "" : "  <color=#9fe0a0>(not hostile to you)</color>")}", small);
            y += 16;
            var wpn = op.Weapon;
            GUI.Label(new Rect(x, y, w, 18), $"ammo {wpn.Magazine}/{op.Archetype.magazine} +{(wpn.Budgeted ? wpn.Reserve.ToString() : "inf")}{(wpn.Dry ? " <color=#ff8a5c>DRY</color>" : "")}{(wpn.Reloading ? " RELOAD" : "")}  err {op.AimError:0.0}°  {(op.InCover ? op.CoverHere.ToString() : "open")}  stam {op.Stamina:0.00}/{1f - 0.5f * op.Fatigue:0.00}{(op.Winded ? " WINDED" : "")} {op.Mode} d{op.Doctrine:0.00}  {op.SpaceHere.Label} {op.FightSpace:0}m [{RoomGraph.Name(op.MyRoom)}{(op.MyRoom != op.ThreatRoom ? " > " + RoomGraph.Name(op.ThreatRoom) : "")}{(op.HoldingDoor ? ", holding door" : "")}{(op.CqbState != "" ? ", CQB " + op.CqbState : "")}{(op.LookingAhead ? ", looking ahead" : "")}] (keep {op.Standoff:0.0}, range {op.RangeMin:0}-{op.RangeMax:0}){(op.Committed ? " <color=#ffcc66>COMMITTED</color>" : "")}{(op.RecentDucks > 0 ? " ducks " + op.RecentDucks : "")}{(op.HoldFire != "" && op.Perception.CanSee ? "  hold: " + op.HoldFire : "")}", small);
            y += 16;
            if (Time.time - op.LastCalloutTime < 4f) GUI.Label(new Rect(x, y, w, 18), "<i>\"" + op.LastCallout + "\"</i>", small);
            if (op.Squad != null)
            {
                var sbT = new System.Text.StringBuilder("<color=#aaa>trusts </color>");
                int shown = 0;
                foreach (var mate in op.Squad.Members)
                {
                    if (mate == null || mate == op || mate.IsDead || shown >= 3) continue;
                    float t = op.TrustIn(mate);
                    sbT.Append($"{mate.Callsign} <color={(t > 0.75f ? "#9fe0a0" : t < 0.45f ? "#ff8a5c" : "#ffe27a")}>{t:0.0}</color>  ");
                    shown++;
                }
                GUI.Label(new Rect(x, y + 15, w, 18), sbT.ToString(), small);
            }
        }

        void SquadPanel(float H)
        {
            var squads = Squad.All;
            if (squads.Count == 0) return;
            float w = 340f, lh = 40f;
            float h = 26 + squads.Count * lh;
            float x = 12, y = H - 40 - h;
            Box(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 8, y + 3, w, 20), "<b>SQUADS</b>  <size=11>confidence: shaken  |  confident</size>", head);
            y += 26;
            foreach (var sq in squads)
            {
                string fac = "-";
                foreach (var m in sq.Members) if (m != null) { fac = m.Faction != null ? m.Faction.displayName : "-"; break; }
                int broken = 0, surr = 0;
                foreach (var m in sq.Members) if (m != null && !m.IsDead) { if (m.Broken) broken++; if (m.Surrendered) surr++; }
                string lead = sq.Leader != null && !sq.Leader.IsDead ? sq.Leader.Callsign : "<color=#ff8a5c>none</color>";
                GUI.Label(new Rect(x + 8, y, w - 10, 18),
                    $"{sq.callsign} <size=11>({fac})</size>  {sq.Fighting}/{sq.StartSize} fighting" +
                    (broken > 0 ? $"  <color=#c9b6ff>{broken} broken</color>" : "") + (surr > 0 ? $"  <color=#bbb>{surr} gave up</color>" : ""), small);
                GUI.Label(new Rect(x + 8, y + 16, 160, 18), $"<size=11>lead {lead}  {sq.LastOrder}</size>", small);
                CenteredBar(x + 170, y + 22, w - 182, sq.Confidence);
                y += lh;
            }
        }

        void CenteredBar(float x, float y, float w, float v)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(x, y, w, 6), white);
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(x + w * 0.5f - 0.5f, y - 2, 1, 10), white);
            GUI.color = ConfColor(v);
            float half = w * 0.5f * Mathf.Clamp01(Mathf.Abs(v));
            GUI.DrawTexture(v >= 0f ? new Rect(x + w * 0.5f, y, half, 6) : new Rect(x + w * 0.5f - half, y, half, 6), white);
            GUI.color = Color.white;
        }

        void Bar(float x, ref float y, string name, float v, Color c)
        {
            GUI.Label(new Rect(x, y - 3, 60, 18), name, small);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(x + 58, y + 2, 186, 8), white);
            GUI.color = c;
            GUI.DrawTexture(new Rect(x + 58, y + 2, 186 * Mathf.Clamp01(v), 8), white);
            GUI.color = Color.white;
            y += 14;
        }

        void OnRenderObject()
        {
            if (!Enabled) return;
            if (lineMat == null)
            {
                lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            lineMat.SetPass(0);
            GL.Begin(GL.LINES);
            foreach (var op in Operative.All)
            {
                if (op == null || op.IsDead || op.Perception == null) continue;
                var b = op.Perception.Belief;
                if (b.Has)
                {
                    // Belief ring: where they think you are, sized by their uncertainty
                    GL.Color(b.Source == BeliefSource.Seen ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.7f, 0.2f));
                    float r = Mathf.Max(0.3f, b.Uncertainty(op.Config.uncertaintyGrowth));
                    var c = b.Predicted + Vector3.up * 0.05f;
                    for (int i = 0; i < 24; i++)
                    {
                        float a0 = i / 24f * Mathf.PI * 2f, a1 = (i + 1) / 24f * Mathf.PI * 2f;
                        GL.Vertex(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r);
                        GL.Vertex(c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r);
                    }
                }
                if (op.Cover.Valid && op.ClaimedSpot.HasValue)
                {
                    GL.Color(new Color(0.3f, 1f, 0.4f));
                    GL.Vertex(op.transform.position + Vector3.up * 0.1f);
                    GL.Vertex(op.Cover.Position + Vector3.up * 0.1f);
                }
                // Hidden route
                if (op.Path.Count > 0)
                {
                    GL.Color(new Color(0.3f, 0.9f, 1f));
                    var prev = op.transform.position + Vector3.up * 0.15f;
                    for (int i = op.PathIndex; i < op.Path.Count; i++)
                    {
                        GL.Vertex(prev);
                        prev = op.Path[i] + Vector3.up * 0.15f;
                        GL.Vertex(prev);
                    }
                }
                // Gaze
                GL.Color(new Color(0.5f, 0.85f, 1f, 0.6f));
                GL.Vertex(op.EyePosition);
                GL.Vertex(op.EyePosition + op.LookForward * 3f);
            }
            // Occupancy maps: where each squad thinks you could be (taller, redder = more likely)
            foreach (var sq in Squad.All)
            {
                var map = sq.Map;
                if (map == null || !map.HasMass || map.MaxP <= 0f) continue;
                for (int i = 0; i < map.P.Length; i++)
                {
                    float q = map.P[i] / map.MaxP;
                    if (q < 0.04f) continue;
                    var p0 = NavGraph.Pos[i] + Vector3.up * 0.05f;
                    GL.Color(Color.Lerp(new Color(1f, 0.85f, 0.2f, 0.5f), new Color(1f, 0.15f, 0.1f, 1f), q));
                    GL.Vertex(p0);
                    GL.Vertex(p0 + Vector3.up * (0.15f + q * 1.2f));
                }
            }
            // Gaze targets (attention)
            GL.Color(new Color(0.6f, 0.9f, 1f));
            foreach (var op in Operative.All)
            {
                if (op == null || op.IsDead || op.Attention == null) continue;
                GL.Vertex(op.EyePosition);
                GL.Vertex(op.Attention.GazePoint);
            }
            // Squad danger map: where they got shot
            GL.Color(new Color(1f, 0.15f, 0.15f));
            foreach (var sq in Squad.All)
                foreach (var (p, t, w) in sq.DangerSpots)
                {
                    if (Time.time - t > 70f) continue;
                    float r = 0.4f + w * 0.3f;
                    var c = p + Vector3.up * 0.08f;
                    GL.Vertex(c + new Vector3(-r, 0f, -r)); GL.Vertex(c + new Vector3(r, 0f, r));
                    GL.Vertex(c + new Vector3(-r, 0f, r)); GL.Vertex(c + new Vector3(r, 0f, -r));
                }
            GL.End();
        }
    }
}
