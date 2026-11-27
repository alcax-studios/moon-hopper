using Alcax.AI;
using Alcax.Core;
using Alcax.Items;
using Alcax.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>The Status tab: health, armor, what's in hand, the station's view of you, and the session's numbers.</summary>
    public class StatusPanel : MonoBehaviour
    {
        TextMeshProUGUI left, right;
        Image healthFill;

        public void Build(RectTransform root)
        {
            var hb = UIFactory.Image("HealthBg", root, Palette.WithAlpha(Palette.UIDark, 0.8f));
            UIFactory.Place(hb.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -60f), new Vector2(700f, 26f), new Vector2(0f, 1f));
            healthFill = UIFactory.Image("HealthFill", hb.transform, Palette.UIHealth);
            UIFactory.Stretch(healthFill.rectTransform);
            healthFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            var hl = UIFactory.Text("HealthLabel", root, "HEALTH", 18f, Palette.WithAlpha(Palette.UIBone, 0.8f), TextAlignmentOptions.MidlineLeft, 5f, 0f);
            UIFactory.Place(hl.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -20f), new Vector2(400f, 30f), new Vector2(0f, 1f));

            left = UIFactory.Body("Left", root, "", 22f, Palette.UIBone);
            UIFactory.Place(left.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -120f), new Vector2(700f, 600f), new Vector2(0f, 1f));
            var box = UIFactory.Image("Right", root, Palette.WithAlpha(Palette.UIDark, 0.6f));
            UIFactory.Place(box.rectTransform, new Vector2(0f, 1f), new Vector2(820f, -8f), new Vector2(740f, 720f), new Vector2(0f, 1f));
            right = UIFactory.Body("RightText", box.transform, "", 21f, Palette.UIBone);
            UIFactory.Place(right.rectTransform, new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(680f, 660f), new Vector2(0f, 1f));
        }

        void Update()
        {
            var inv = PlayerInventory.Instance;
            var pc = inv != null ? inv.GetComponent<PlayerController>() : null;
            var gm = GameManager.Instance;
            if (pc != null)
            {
                var h = pc.Health;
                healthFill.rectTransform.anchorMax = new Vector2(h.Normalized, 1f);
                var hand = inv.SelectedStack;
                var wh = inv.GetComponent<WeaponHolder>();
                string inHand = hand == null ? "Empty hands" : hand.def.displayName + (hand.def.weapon != null && hand.def.weapon.ammoType != Combat.AmmoType.None ? $"   ({wh.Magazine} / {wh.Reserve})" : hand.count > 1 ? $"   x{hand.count}" : "");
                left.text =
                    $"<size=30><b>{h.Current:0}</b></size> <color=#E8E1D088>/ {h.Max:0}</color>\n\n" +
                    $"<color=#E8E1D099><size=16>ARMOR</size></color>\nHead {inv.Armor(true):0}   ·   Body {inv.Armor(false):0}   <color=#E8E1D088>(damage x{100f / (100f + inv.Armor(false)):0.00} body, x{100f / (100f + inv.Armor(true)):0.00} head)</color>\n\n" +
                    $"<color=#E8E1D099><size=16>CREDITS</size></color>\n<color=#FFD27A>{inv.Credits} CR</color>{(inv.StolenValue() > 0 ? $"   <color=#E86A5A>(carrying {inv.StolenValue()} CR of stolen goods)</color>" : "")}\n\n" +
                    $"<color=#E8E1D099><size=16>NEEDS</size></color>\n{Needs()}\n\n" +
                    $"<color=#E8E1D099><size=16>IN HAND</size></color>\n{inHand}\n\n" +
                    $"<color=#E8E1D099><size=16>WEARING</size></color>\n{Wearing(inv)}";
            }
            var sec = StationSecurity.Instance;
            string standing = sec == null ? "No station security on this map." : sec.Wanted ? "<color=#E86A5A>WANTED</color>: security is hunting you. Break line of sight and wait it out." : sec.ThreatActive ? "<color=#F2A23A>SECURITY ALERT</color>: hostiles on the station. Shooting them is fine; hitting a guard or civilian is not." : "Clear. Live fire is allowed on the range only.";
            int alive = 0; foreach (var op in Operative.All) if (!op.IsDead) alive++;
            right.text =
                $"<color=#E8E1D099><size=16>STATION STANDING</size></color>\n{standing}\n\n" +
                $"<color=#E8E1D099><size=16>SESSION</size></color>\nTime  {Clock(gm != null ? gm.TimeSurvived : 0f)}\nKills  {(gm != null ? gm.Kills : 0)}\nArmed units on the map  {alive}\n\n" +
                $"<color=#E8E1D099><size=16>QUESTS</size></color>\n{QuestLog.All.FindAll(q => q.state == QuestState.Active).Count} active   ·   {QuestLog.All.FindAll(q => q.state == QuestState.Completed).Count} completed\n\n" +
                $"<color=#E8E1D099><size=16>CODEX</size></color>\n{Codex.Unlocked.Count} entries" +
                (AdminSettings.AnyActive ? $"\n\n<color=#F2A23A><size=16>CHEATS ON</size></color>\n{AdminSettings.ActiveSummary()}" : "");
        }

static string Needs()
        {
            var v = PlayerVitals.Instance;
            if (v == null) return "-";
            string fx = "";
            foreach (var e in v.Effects) fx += $"   {(e.debuff ? "<color=#E86A5A>" : "<color=#9FE0A0>")}{e.label} {Mathf.CeilToInt(e.remaining)} s</color>";
            return $"Food {v.Hunger:0}   ·   Water {v.Thirst:0}   ·   Energy {v.Energy:0}   ·   Stamina {v.Stamina:0}/{v.MaxStamina:0}{fx}";
        }

        static string Wearing(PlayerInventory inv)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in PlayerInventory.BodySlots)
            {
                var w = inv.Worn(s);
                sb.Append($"<color=#E8E1D088>{s}</color>  {(w != null ? w.def.displayName : "-")}\n");
            }
            return sb.ToString();
        }

        static string Clock(float t) => $"{(int)(t / 60f):00}:{(int)(t % 60f):00}";
    }
}
