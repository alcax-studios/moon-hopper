using System.Collections;
using Alcax.Combat;
using Alcax.Core;
using Alcax.Items;
using Alcax.Loot;
using Alcax.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>
    /// In-game HUD, built from code: crosshair, hit marker, health bar (tweened, with a trailing chip),
    /// weapon and ammo, wave and kills, objective, interact prompt, pickup toasts, damage vignette, and wave banners.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [Tooltip("When set, replaces the loot/survive objective (maps without zombies).")]
        [SerializeField] string objectiveOverride;

        CanvasGroup group;
        WeaponHolder weapons;
        PlayerInteractor interactor;
        Health health;

        RectTransform[] crossBars;
        Image crossDot;
        Image[] hitBars;
        float hitT = 1f;
        bool hitKill;

        RectTransform healthFill, healthChip;
        TextMeshProUGUI healthText;
        float shownHealth = 1f, chipHealth = 1f, chipDelay;

        TextMeshProUGUI weaponName, ammoMag, ammoReserve, reloadText;
        TextMeshProUGUI waveText, statsText, objectiveText, adminText;
        TextMeshProUGUI prompt;
        Image promptBg;
        TextMeshProUGUI toast;
        Coroutine toastRoutine;
        Image damageVignette;
        float damageFlash;
        TextMeshProUGUI banner, bannerSub;
        Image screenFlash;
        Coroutine bannerRoutine;
        // Hotbar strip, use bar, quest tracker
        readonly Image[] hbBg = new Image[PlayerInventory.HotbarSize];
        readonly RawImage[] hbIcon = new RawImage[PlayerInventory.HotbarSize];
        readonly TextMeshProUGUI[] hbCount = new TextMeshProUGUI[PlayerInventory.HotbarSize];
        Image useBg, useFill;
        TextMeshProUGUI useText, questText, menuHint;
        Image questBg;
        RectTransform staminaFill, staminaChip;
        Image staminaImg;
        float shownStamina = 1f, chipStamina = 1f, chipHold, lastStamina = -1f;
        readonly RectTransform[] needFill = new RectTransform[3];
        readonly Image[] needImg = new Image[3];
        TextMeshProUGUI effectsText, needsText;
        PlayerVitals vitals;
        RectTransform[] grabCorners;
        float grabT;
        HeldItem held;
        readonly System.Collections.Generic.Queue<string> toastQueue = new System.Collections.Generic.Queue<string>();

        void Awake()
        {
            weapons = player.GetComponent<WeaponHolder>();
            interactor = player.GetComponent<PlayerInteractor>();
            health = player.GetComponent<Health>();
            held = player.GetComponent<HeldItem>();
            vitals = player.GetComponent<PlayerVitals>();
            Build();
        }

        void OnEnable()
        {
            health.Damaged += OnPlayerDamaged;
            weapons.HitConfirmed += OnHitConfirmed;
            Pickup.Collected += ShowToast;
            PlayerInventory.Notice += ShowToast;
            QuestLog.Notice += ShowToast;
            Codex.Notice += ShowToast;
        }

        void OnDisable()
        {
            health.Damaged -= OnPlayerDamaged;
            weapons.HitConfirmed -= OnHitConfirmed;
            Pickup.Collected -= ShowToast;
            PlayerInventory.Notice -= ShowToast;
            QuestLog.Notice -= ShowToast;
            Codex.Notice -= ShowToast;
        }

        void Build()
        {
            var canvas = UIFactory.Canvas("HUDCanvas", transform, 0);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            var root = canvas.transform;

            // Damage vignette (behind everything)
            damageVignette = UIFactory.Image("DamageVignette", root, Palette.WithAlpha(Palette.Blood, 0f), UIFactory.RadialEdgeSprite());
            UIFactory.Stretch(damageVignette.rectTransform);

            screenFlash = UIFactory.Image("ScreenFlash", root, Palette.WithAlpha(Palette.Accent, 0f));
            UIFactory.Stretch(screenFlash.rectTransform);

            // Crosshair
            var cross = UIFactory.Rect("Crosshair", root);
            UIFactory.Place(cross, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 80f));
            crossBars = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var bar = UIFactory.Image("Bar" + i, cross, Palette.WithAlpha(Palette.UIBone, 0.9f));
                bar.rectTransform.sizeDelta = i < 2 ? new Vector2(3f, 12f) : new Vector2(12f, 3f);
                crossBars[i] = bar.rectTransform;
                bar.gameObject.AddComponent<Shadow>().effectColor = Palette.WithAlpha(Palette.UIDark, 0.8f);
            }
            crossDot = UIFactory.Image("Dot", cross, Palette.WithAlpha(Palette.UIBone, 0.95f));
            crossDot.rectTransform.sizeDelta = new Vector2(4f, 4f);
            crossDot.gameObject.AddComponent<Shadow>().effectColor = Palette.WithAlpha(Palette.UIDark, 0.8f);

            // Grab cue: four corner brackets that close in around the dot over something movable
            grabCorners = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var corner = UIFactory.Rect("GrabCorner" + i, cross);
                corner.sizeDelta = new Vector2(10f, 10f);
                corner.localRotation = Quaternion.Euler(0f, 0f, -90f * i);
                var h = UIFactory.Image("H", corner, Palette.UIBone);
                UIFactory.Place(h.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(10f, 2.5f), new Vector2(0f, 1f));
                var v = UIFactory.Image("V", corner, Palette.UIBone);
                UIFactory.Place(v.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(2.5f, 10f), new Vector2(0f, 1f));
                h.gameObject.AddComponent<Shadow>().effectColor = Palette.WithAlpha(Palette.UIDark, 0.8f);
                v.gameObject.AddComponent<Shadow>().effectColor = Palette.WithAlpha(Palette.UIDark, 0.8f);
                grabCorners[i] = corner;
            }

            hitBars = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var hb = UIFactory.Image("Hit" + i, cross, Palette.WithAlpha(Palette.UIBone, 0f));
                hb.rectTransform.sizeDelta = new Vector2(3f, 12f);
                float ang = 45f + 90f * i;
                Vector2 d = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
                hb.rectTransform.anchoredPosition = d * 13f;
                hb.rectTransform.localRotation = Quaternion.Euler(0f, 0f, ang - 90f);
                hitBars[i] = hb;
            }

            // Health (bottom-left)
            var hpRoot = UIFactory.Rect("Health", root);
            UIFactory.Place(hpRoot, new Vector2(0f, 0f), new Vector2(64f, 56f), new Vector2(420f, 70f), new Vector2(0f, 0f));
            var hpLabel = UIFactory.Text("Label", hpRoot, "HEALTH", 20f, Palette.WithAlpha(Palette.UIBone, 0.8f), TextAlignmentOptions.BottomLeft, 8f);
            UIFactory.Place(hpLabel.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(200f, 26f), new Vector2(0f, 1f));
            healthText = UIFactory.Text("Value", hpRoot, "100", 30f, Palette.UIBone, TextAlignmentOptions.BottomRight, 2f);
            UIFactory.Place(healthText.rectTransform, new Vector2(1f, 1f), new Vector2(0f, 4f), new Vector2(120f, 34f), new Vector2(1f, 1f));
            var track = UIFactory.Image("Track", hpRoot, Palette.WithAlpha(Palette.UIDark, 0.75f));
            UIFactory.Place(track.rectTransform, new Vector2(0f, 0f), Vector2.zero, new Vector2(420f, 22f), new Vector2(0f, 0f));
            var chip = UIFactory.Image("Chip", track.transform, Palette.AccentHot);
            healthChip = chip.rectTransform;
            var fill = UIFactory.Image("Fill", track.transform, Palette.UIHealth);
            healthFill = fill.rectTransform;
            foreach (var r in new[] { healthChip, healthFill })
            {
                r.anchorMin = new Vector2(0f, 0f);
                r.anchorMax = new Vector2(1f, 1f);
                r.offsetMin = new Vector2(3f, 3f);
                r.offsetMax = new Vector2(-3f, -3f);
            }

            // Weapon + ammo (bottom-right)
            var wRoot = UIFactory.Rect("Weapon", root);
            UIFactory.Place(wRoot, new Vector2(1f, 0f), new Vector2(-64f, 56f), new Vector2(460f, 120f), new Vector2(1f, 0f));
            weaponName = UIFactory.Text("Name", wRoot, "FISTS", 26f, Palette.Accent, TextAlignmentOptions.BottomRight, 8f);
            UIFactory.Place(weaponName.rectTransform, new Vector2(1f, 1f), Vector2.zero, new Vector2(460f, 30f), new Vector2(1f, 1f));
            ammoMag = UIFactory.Text("Mag", wRoot, "", 64f, Palette.UIBone, TextAlignmentOptions.BottomRight, 0f, 0.2f);
            UIFactory.Place(ammoMag.rectTransform, new Vector2(1f, 0f), new Vector2(-120f, -6f), new Vector2(200f, 76f), new Vector2(1f, 0f));
            ammoReserve = UIFactory.Text("Reserve", wRoot, "", 30f, Palette.WithAlpha(Palette.UIBone, 0.7f), TextAlignmentOptions.BottomLeft, 2f);
            UIFactory.Place(ammoReserve.rectTransform, new Vector2(1f, 0f), new Vector2(-110f, 4f), new Vector2(110f, 40f), new Vector2(0f, 0f));
            reloadText = UIFactory.Text("Reload", root, "RELOADING", 22f, Palette.AccentHot, TextAlignmentOptions.Center, 10f);
            UIFactory.Place(reloadText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(400f, 30f));

            // Wave + stats (top-right)
            waveText = UIFactory.Text("Wave", root, "", 44f, Palette.UIBone, TextAlignmentOptions.TopRight, 6f, 0.2f);
            UIFactory.Place(waveText.rectTransform, new Vector2(1f, 1f), new Vector2(-64f, -48f), new Vector2(600f, 54f), new Vector2(1f, 1f));
            statsText = UIFactory.Text("Stats", root, "", 22f, Palette.WithAlpha(Palette.UIBone, 0.75f), TextAlignmentOptions.TopRight, 6f);
            UIFactory.Place(statsText.rectTransform, new Vector2(1f, 1f), new Vector2(-64f, -104f), new Vector2(600f, 30f), new Vector2(1f, 1f));

            // Objective (top-left)
            objectiveText = UIFactory.Text("Objective", root, "", 24f, Palette.Accent, TextAlignmentOptions.TopLeft, 8f);
            UIFactory.Place(objectiveText.rectTransform, new Vector2(0f, 1f), new Vector2(64f, -52f), new Vector2(800f, 34f), new Vector2(0f, 1f));

            adminText = UIFactory.Text("Admin", root, "", 18f, Palette.AccentHot, TextAlignmentOptions.TopLeft, 6f);
            UIFactory.Place(adminText.rectTransform, new Vector2(0f, 1f), new Vector2(64f, -92f), new Vector2(1000f, 26f), new Vector2(0f, 1f));

            // Interact prompt
            promptBg = UIFactory.Image("PromptBg", root, Palette.WithAlpha(Palette.UIDark, 0.7f));
            UIFactory.Place(promptBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(460f, 52f));
            prompt = UIFactory.Text("Prompt", promptBg.transform, "", 26f, Palette.UIBone, TextAlignmentOptions.Center, 6f, 0.1f);
            UIFactory.Stretch(prompt.rectTransform);

// Stamina (a thin bar under health) and the needs (food, water, energy)
            var stTrack = UIFactory.Image("StaminaTrack", root, Palette.WithAlpha(Palette.UIDark, 0.75f));
            UIFactory.Place(stTrack.rectTransform, new Vector2(0f, 0f), new Vector2(64f, 44f), new Vector2(420f, 8f), new Vector2(0f, 0f));
var stChip = UIFactory.Image("StaminaChip", stTrack.transform, Palette.WithAlpha(Palette.UIBone, 0.55f));
            staminaChip = stChip.rectTransform;
            staminaChip.anchorMin = Vector2.zero; staminaChip.anchorMax = Vector2.one; staminaChip.offsetMin = staminaChip.offsetMax = Vector2.zero;
            staminaImg = UIFactory.Image("StaminaFill", stTrack.transform, new Color(0.55f, 0.85f, 0.7f));
            staminaFill = staminaImg.rectTransform;
            staminaFill.anchorMin = Vector2.zero; staminaFill.anchorMax = Vector2.one; staminaFill.offsetMin = staminaFill.offsetMax = Vector2.zero;
            var needNames = new[] { "FOOD", "WATER", "ENERGY" };
            var needColors = new[] { new Color(0.85f, 0.65f, 0.35f), new Color(0.45f, 0.7f, 0.95f), new Color(0.75f, 0.65f, 0.95f) };
            for (int i = 0; i < 3; i++)
            {
                float nx = 64f + i * 142f;
                var lbl = UIFactory.Text("Need" + i, root, needNames[i], 11f, Palette.WithAlpha(Palette.UIBone, 0.6f), TextAlignmentOptions.BottomLeft, 3f, 0.1f);
                UIFactory.Place(lbl.rectTransform, new Vector2(0f, 0f), new Vector2(nx, 24f), new Vector2(130f, 14f), new Vector2(0f, 0f));
                var tr = UIFactory.Image("NeedTrack" + i, root, Palette.WithAlpha(Palette.UIDark, 0.7f));
                UIFactory.Place(tr.rectTransform, new Vector2(0f, 0f), new Vector2(nx, 16f), new Vector2(134f, 5f), new Vector2(0f, 0f));
                needImg[i] = UIFactory.Image("NeedFill" + i, tr.transform, needColors[i]);
                needFill[i] = needImg[i].rectTransform;
                needFill[i].anchorMin = Vector2.zero; needFill[i].anchorMax = Vector2.one; needFill[i].offsetMin = needFill[i].offsetMax = Vector2.zero;
            }
            effectsText = UIFactory.Text("Effects", root, "", 15f, Palette.UIBone, TextAlignmentOptions.BottomLeft, 3f, 0.15f);
            UIFactory.Place(effectsText.rectTransform, new Vector2(0f, 0f), new Vector2(64f, 132f), new Vector2(700f, 24f), new Vector2(0f, 0f));
            needsText = UIFactory.Text("NeedsWarn", root, "", 15f, new Color(0.95f, 0.55f, 0.45f), TextAlignmentOptions.BottomLeft, 4f, 0.15f);
            UIFactory.Place(needsText.rectTransform, new Vector2(0f, 0f), new Vector2(64f, 156f), new Vector2(700f, 24f), new Vector2(0f, 0f));

            // Hotbar (bottom center)
            const float hs = 62f, hg = 6f;
            float hw = PlayerInventory.HotbarSize * (hs + hg) - hg;
            for (int i = 0; i < PlayerInventory.HotbarSize; i++)
            {
                var bg = UIFactory.Image("Hotbar" + i, root, Palette.WithAlpha(Palette.UIDark, 0.55f));
                UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 0f), new Vector2(-hw * 0.5f + i * (hs + hg), 24f), new Vector2(hs, hs), new Vector2(0f, 0f));
                hbBg[i] = bg;
                var ic = UIFactory.Rect("Icon", bg.transform);
                UIFactory.Stretch(ic);
                ic.offsetMin = new Vector2(4f, 4f); ic.offsetMax = new Vector2(-4f, -4f);
                hbIcon[i] = ic.gameObject.AddComponent<RawImage>();
                hbIcon[i].raycastTarget = false;
                var key = UIFactory.Text("Key", bg.transform, (i + 1).ToString(), 13f, Palette.WithAlpha(Palette.AccentHot, 0.8f), TextAlignmentOptions.TopLeft, 0f, 0.2f);
                UIFactory.Stretch(key.rectTransform);
                key.rectTransform.offsetMin = new Vector2(5f, 2f); key.rectTransform.offsetMax = new Vector2(-3f, -3f);
                hbCount[i] = UIFactory.Text("Count", bg.transform, "", 14f, Palette.UIBone, TextAlignmentOptions.BottomRight, 0f, 0.25f);
                UIFactory.Stretch(hbCount[i].rectTransform);
                hbCount[i].rectTransform.offsetMin = new Vector2(3f, 2f); hbCount[i].rectTransform.offsetMax = new Vector2(-5f, -2f);
            }
            menuHint = UIFactory.Text("MenuHint", root, "", 13f, Palette.WithAlpha(Palette.UIBone, 0.4f), TextAlignmentOptions.MidlineLeft, 3f, 0.1f);
            UIFactory.Place(menuHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(hw * 0.5f + 14f, 24f), new Vector2(200f, 20f), new Vector2(0f, 0f));
            // Use bar (above the hotbar)
            useBg = UIFactory.Image("UseBg", root, Palette.WithAlpha(Palette.UIDark, 0.7f));
            UIFactory.Place(useBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(320f, 10f), new Vector2(0.5f, 0f));
            useFill = UIFactory.Image("UseFill", useBg.transform, Palette.Accent);
            UIFactory.Stretch(useFill.rectTransform);
            useText = UIFactory.Text("UseText", root, "", 16f, Palette.UIBone, TextAlignmentOptions.Bottom, 4f, 0.15f);
            UIFactory.Place(useText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 136f), new Vector2(500f, 24f), new Vector2(0.5f, 0f));
            // Quest tracker (top left, under the objective)
            questBg = UIFactory.Image("QuestBg", root, Palette.WithAlpha(Palette.UIDark, 0.55f));
            UIFactory.Place(questBg.rectTransform, new Vector2(0f, 1f), new Vector2(52f, -120f), new Vector2(460f, 120f), new Vector2(0f, 1f));
            questText = UIFactory.Text("Quest", root, "", 18f, Palette.UIBone, TextAlignmentOptions.TopLeft, 2f, 0.15f);
            questText.fontStyle = FontStyles.Normal;
            UIFactory.Place(questText.rectTransform, new Vector2(0f, 1f), new Vector2(64f, -130f), new Vector2(700f, 140f), new Vector2(0f, 1f));

            // Toast
            toast = UIFactory.Text("Toast", root, "", 30f, Palette.AccentHot, TextAlignmentOptions.Center, 8f);
            UIFactory.Place(toast.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(800f, 40f));
            toast.alpha = 0f;

            // Banner
            banner = UIFactory.Text("Banner", root, "", 120f, Palette.Accent, TextAlignmentOptions.Center, 14f, 0.25f);
            UIFactory.Place(banner.rectTransform, new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(1400f, 140f));
            bannerSub = UIFactory.Text("BannerSub", root, "", 28f, Palette.UIBone, TextAlignmentOptions.Center, 12f);
            UIFactory.Place(bannerSub.rectTransform, new Vector2(0.5f, 0.68f), new Vector2(0f, -104f), new Vector2(1200f, 40f));
            banner.rectTransform.localScale = Vector3.zero;
            bannerSub.alpha = 0f;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var gm = GameManager.Instance;
            float targetAlpha = gm == null || gm.State == GameState.Playing || gm.State == GameState.Ended ? 1f : 0f;
            if (gm != null && gm.State == GameState.Ended) targetAlpha = 0.35f;
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, dt / 0.3f);

            UpdateCrosshair(dt);
            UpdateHealth(dt);
            UpdateWeapon();
            UpdateWave();
            UpdatePrompt();
            UpdateHotbar();
            UpdateVitals();

            damageFlash = Mathf.MoveTowards(damageFlash, 0f, dt / 0.6f);
            float lowPulse = health.Normalized < 0.3f && !health.IsDead
                ? (0.25f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f)) * (1f - health.Normalized / 0.3f) : 0f;
            if (health.IsDead) lowPulse = 0.6f;
            damageVignette.color = Palette.WithAlpha(Palette.Blood, Mathf.Max(damageFlash * 0.8f, lowPulse));
        }

        void UpdateCrosshair(float dt)
        {
            var w = weapons.Current;
            bool melee = w == null || w.kind == WeaponKind.Melee;
            float gap = melee ? 0f : (10f + w.spread * 4f + weapons.SpreadBloom * 18f + player.SpeedNormalized * 4f) * Mathf.Lerp(1f, 0.45f, weapons.AimAmount);
            for (int i = 0; i < 4; i++)
            {
                crossBars[i].gameObject.SetActive(!melee);
                Vector2 dir = i switch { 0 => Vector2.up, 1 => Vector2.down, 2 => Vector2.left, _ => Vector2.right };
                crossBars[i].anchoredPosition = dir * (gap + 6f);
            }
            crossDot.rectTransform.sizeDelta = melee ? new Vector2(6f, 6f) : new Vector2(4f, 4f);
            float crossAlpha = 1f;
            crossDot.color = Palette.WithAlpha(Palette.UIBone, 0.95f * crossAlpha);
            foreach (var b in crossBars) b.GetComponent<Image>().color = Palette.WithAlpha(Palette.UIBone, 0.9f * crossAlpha);

            // Grab cue (fades in; tighter and amber while holding)
            bool cue = interactor != null && interactor.GrabCue;
            bool holding = Alcax.Player.PlayerGrabber.Holding;
            grabT = Mathf.MoveTowards(grabT, cue ? 1f : 0f, dt / 0.12f);
            float spread = Mathf.Lerp(26f, holding ? 9f : 14f, Ease.OutCubic(grabT));
            var cueColor = Palette.WithAlpha(holding ? Palette.AccentHot : Palette.UIBone, 0.95f * grabT);
            for (int i = 0; i < 4; i++)
            {
                var d = i switch { 0 => new Vector2(-1f, 1f), 1 => new Vector2(1f, 1f), 2 => new Vector2(1f, -1f), _ => new Vector2(-1f, -1f) };
                grabCorners[i].anchoredPosition = d * spread;
                foreach (var img in grabCorners[i].GetComponentsInChildren<Image>()) img.color = cueColor;
            }
            if (cue) for (int i = 0; i < 4; i++) crossBars[i].gameObject.SetActive(false);

            hitT = Mathf.Min(1f, hitT + dt / 0.18f);
            float a = 1f - Ease.OutCubic(hitT);
            var c = hitKill ? Palette.UIHealth : Palette.UIBone;
            float scale = 1f + (1f - hitT) * 0.4f;
            for (int i = 0; i < 4; i++)
            {
                hitBars[i].color = Palette.WithAlpha(c, a);
                hitBars[i].rectTransform.localScale = Vector3.one * scale;
            }
        }

        void UpdateHealth(float dt)
        {
            float target = health.Normalized;
            shownHealth = Mathf.Lerp(shownHealth, target, 1f - Mathf.Exp(-14f * dt));
            chipDelay -= dt;
            if (chipDelay <= 0f) chipHealth = Mathf.MoveTowards(chipHealth, shownHealth, dt * 0.8f);
            if (chipHealth < shownHealth) chipHealth = shownHealth;
            healthFill.anchorMax = new Vector2(Mathf.Max(0.001f, shownHealth), 1f);
            healthChip.anchorMax = new Vector2(Mathf.Max(0.001f, chipHealth), 1f);
            healthText.text = Mathf.CeilToInt(health.Current).ToString();
        }

        void UpdateWeapon()
        {
            var w = weapons.Current;
            if (w == null) return;
            weaponName.text = w.displayName.ToUpperInvariant();
            if (w.kind == WeaponKind.Hitscan)
            {
                ammoMag.text = weapons.Magazine.ToString();
                ammoMag.color = weapons.Magazine == 0 ? Palette.UIHealth : Palette.UIBone;
                ammoReserve.text = "/ " + weapons.Reserve;
            }
            else
            {
                ammoMag.text = "";
                ammoReserve.text = "";
            }
            bool showReload = weapons.Reloading ||
                              (w.kind == WeaponKind.Hitscan && weapons.Magazine == 0 && weapons.Reserve == 0);
            reloadText.text = weapons.Reloading ? "RELOADING" : "NO AMMO";
            reloadText.alpha = showReload ? 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 8f) : 0f;
        }

        void UpdateWave()
        {
            var gm = GameManager.Instance;
            adminText.text = AdminSettings.AnyActive ? "ADMIN:  " + AdminSettings.ActiveSummary() : "";
            if (!string.IsNullOrEmpty(objectiveOverride)) objectiveText.text = "OBJECTIVE:  " + objectiveOverride;
        }

        void UpdatePrompt()
        {
            var text = interactor.FocusedPrompt;
            bool show = text != null;
            if (show) prompt.text = text;
            float a = Mathf.MoveTowards(promptBg.color.a, show ? 0.7f : 0f, Time.unscaledDeltaTime / 0.12f);
            promptBg.color = Palette.WithAlpha(Palette.UIDark, a);
            prompt.alpha = a / 0.7f;
        }

        void OnPlayerDamaged(DamageInfo info)
        {
            damageFlash = 1f;
            chipDelay = 0.35f;
        }

        void OnHitConfirmed(bool kill, bool headshot)
        {
            hitT = 0f;
            hitKill = kill || headshot;
        }

void ShowToast(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            // Queue behind a showing toast (quest and codex notices arrive together); keep the queue short
            if (toastRoutine != null) { if (toastQueue.Count < 4) toastQueue.Enqueue(msg); return; }
            toastRoutine = StartCoroutine(ToastRoutine(msg));
        }

void UpdateVitals()
        {
// The player's vitals may be added after the HUD wakes up: look them up until found
            if (vitals == null) vitals = PlayerVitals.Instance != null ? PlayerVitals.Instance : player.GetComponent<PlayerVitals>();
            if (vitals == null) return;
            float dt = Time.unscaledDeltaTime;
            float target = Mathf.Clamp01(vitals.Stamina / Mathf.Max(100f, vitals.MaxStamina));
            shownStamina = Mathf.Lerp(shownStamina, target, 1f - Mathf.Exp(-12f * dt));
            bool spending = lastStamina >= 0f && vitals.Stamina < lastStamina - 0.01f;
            bool recovering = lastStamina >= 0f && vitals.Stamina > lastStamina + 0.01f;
            lastStamina = vitals.Stamina;
            // The chip holds a moment where you were, then slides down to the fill (what you just spent)
            if (spending) chipHold = 0.5f;
            chipHold -= dt;
            if (chipHold <= 0f) chipStamina = Mathf.MoveTowards(chipStamina, shownStamina, dt * 0.6f);
            if (chipStamina < shownStamina) chipStamina = shownStamina;
            staminaFill.anchorMax = new Vector2(Mathf.Max(0.001f, shownStamina), 1f);
            staminaChip.anchorMax = new Vector2(Mathf.Max(0.001f, chipStamina), 1f);
            var baseCol = vitals.Has("energized") ? new Color(1f, 0.85f, 0.3f) : new Color(0.55f, 0.85f, 0.7f);
            if (vitals.Exhausted) baseCol = Color.Lerp(new Color(0.85f, 0.35f, 0.3f), new Color(1f, 0.6f, 0.5f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f));
            else if (recovering) baseCol = Color.Lerp(baseCol, Color.white, 0.25f + 0.1f * Mathf.Sin(Time.unscaledTime * 6f));
            else if (target >= 0.999f) baseCol = Palette.WithAlpha(baseCol, 0.55f);   // full and idle: quieter
            staminaImg.color = baseCol;
            var v = new[] { vitals.Hunger, vitals.Thirst, vitals.Energy };
            for (int i = 0; i < 3; i++)
            {
                needFill[i].anchorMax = new Vector2(Mathf.Clamp01(v[i] / 100f), 1f);
                var c = needImg[i].color; c.a = v[i] < PlayerVitals.Low ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f) : 1f; needImg[i].color = c;
            }
            var sb = new System.Text.StringBuilder();
            foreach (var e in vitals.Effects)
                sb.Append(e.debuff ? $"<color=#E86A5A>{e.label} {Mathf.CeilToInt(e.remaining)}</color>   " : $"<color=#9FE0A0>{e.label} {Mathf.CeilToInt(e.remaining)}</color>   ");
            effectsText.text = sb.ToString();
            needsText.text = vitals.NeedsLabel();
        }

        void UpdateHotbar()
        {
            var inv = PlayerInventory.Instance;
            if (inv == null) return;
            for (int i = 0; i < PlayerInventory.HotbarSize; i++)
            {
                var st = inv.Get(new SlotRef(SlotArea.Hotbar, i));
                bool sel = i == inv.Selected;
                hbBg[i].color = sel ? Palette.WithAlpha(Palette.Accent, 0.55f) : Palette.WithAlpha(Palette.UIDark, st != null ? 0.7f : 0.5f);
                hbIcon[i].enabled = st != null;
                if (st != null) hbIcon[i].texture = PreviewStudio.Instance.Icon(st.def);
                hbCount[i].text = st == null ? "" : st.def.Stackable ? st.count.ToString() : "";
                hbBg[i].rectTransform.localScale = Vector3.one * (sel ? 1.08f : 1f);
            }
            float p = held != null ? held.UseProgress : -1f;
            useBg.enabled = useFill.enabled = p >= 0f;
            useFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
            useText.text = p >= 0f && held.Using != null ? "USING " + held.Using.displayName.ToUpperInvariant() : "";

            var q = QuestLog.All.Find(x => x.state == QuestState.Active && x.Tracked);
            questBg.enabled = q != null;
            if (q == null) { questText.text = ""; return; }
            var sb = new System.Text.StringBuilder($"<b><color=#F2A23A>{q.title.ToUpperInvariant()}</color></b>").Append('\n');
            foreach (var o in q.objectives)
                sb.Append(o.Done ? $"<color=#E8E1D066>[x] {o.text}</color>" : $"<color=#E8E1D0>[ ] {o.text}</color>").Append('\n');
            questText.text = sb.ToString();
            questBg.rectTransform.sizeDelta = new Vector2(Mathf.Min(700f, questText.preferredWidth) + 26f, questText.preferredHeight + 18f);
        }

        IEnumerator ToastRoutine(string msg)
        {
            toast.text = msg;
            var rt = toast.rectTransform;
            Vector2 start = new Vector2(0f, -200f);
            rt.localScale = Vector3.one;
            yield return Ease.Tween(0.15f, t => rt.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, t), Ease.OutBack);
            yield return Ease.Tween(0.9f, t =>
            {
                rt.anchoredPosition = start + Vector2.up * (60f * t);
                toast.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            }, Ease.OutCubic);
            toast.alpha = 0f;
            toastRoutine = null;
            if (toastQueue.Count > 0) ShowToast(toastQueue.Dequeue());
        }

        public void ShowBanner(string title, string sub, Color color)
        {
            if (bannerRoutine != null) StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(BannerRoutine(title, sub, color));
        }

        IEnumerator BannerRoutine(string title, string sub, Color color)
        {
            // flash, then banner in (OutBack), hold, banner out
            banner.text = title;
            banner.color = color;
            bannerSub.text = sub;
            StartCoroutine(Ease.Tween(0.1f, t => screenFlash.color = Palette.WithAlpha(Palette.Accent, 0.35f * (1f - t)), Ease.OutCubic));
            yield return new WaitForSecondsRealtime(0.05f);
            yield return Ease.Tween(0.35f, t =>
            {
                banner.rectTransform.localScale = Vector3.one * t;
                bannerSub.alpha = Mathf.Clamp01(t);
            }, Ease.OutBack);
            yield return new WaitForSecondsRealtime(1.8f);
            yield return Ease.Tween(0.25f, t =>
            {
                banner.rectTransform.localScale = Vector3.one * (1f - t);
                bannerSub.alpha = 1f - t;
            }, Ease.OutCubic);
            bannerRoutine = null;
        }
    }
}
