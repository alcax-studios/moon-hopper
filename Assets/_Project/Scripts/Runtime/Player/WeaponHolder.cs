using System;
using System.Collections;
using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Combat;
using Alcax.Core;
using Alcax.FX;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.Player
{
    /// <summary>
    /// The player's weapons: inventory, switching, melee and hitscan attacks, reloads,
    /// and all procedural animation of the held weapon: sway, bob, swings, recoil kick,
    /// slide and pump cycling, magazine and shell reloads, ejected casings, and the equip raise.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class WeaponHolder : MonoBehaviour
    {
        [SerializeField] WeaponData fists;
        [Tooltip("Every weapon, for the admin All Weapons cheat (falls back to the loot table).")]
        [SerializeField] WeaponData[] allWeapons;
        [SerializeField] Transform viewRoot;
        [SerializeField] Camera aimCamera;
        [SerializeField] LayerMask hitMask = ~0;

        [Header("Feel")]
        [SerializeField] float swayAmount = 0.035f;
        [SerializeField] float swayRotate = 2.2f;
        [SerializeField] float viewBobScale = 1.6f;
        [Tooltip("The forehand swing rotates the weapon around this point in camera space (the right shoulder).")]
        [SerializeField] Vector3 shoulderPivot = new Vector3(0.24f, -0.42f, 0.02f);
        [Tooltip("The backhand swing rotates around this point (the chest), so it doesn't mirror around the right shoulder.")]
        [SerializeField] Vector3 chestPivot = new Vector3(0.02f, -0.4f, 0.05f);
        [SerializeField] float strikeTime = 0.09f;
        [SerializeField] float swingTiltVariation = 6f;

        [Header("Aim down sights (hold right mouse)")]
        [SerializeField] float hipFov = 72f;
        [SerializeField] float gunAimFov = 56f;
        [SerializeField] float meleeAimFov = 66f;
        [SerializeField] float aimTime = 0.14f;
        [Tooltip("How far the gun is pulled toward the eye while aiming (meters).")]
        [SerializeField] float aimPullIn = 0.06f;
        [Tooltip("Camera-space offset the gun eases to while aiming (a heuristic aim pose).")]
        [SerializeField] Vector3 aimPose = new Vector3(-0.075f, 0.035f, -0.045f);

        [Header("Debug drive (verification only)")]
        public bool debugFire;
        public bool debugAim;

        class Owned
        {
            public WeaponData Data;
            public GameObject View;
            public Transform Muzzle;
            public int Magazine;
            public Vector3 BasePos;
            public Quaternion BaseRot;
            public Transform Grip, HandR, HandL;          // HandR/HandL: direct children (fists)
            public Quaternion GripRot, HandRRot, HandLRot;
            public Vector3 HandRPos, HandLPos;
            public TrailRenderer Trail;
            public Vector3 AimOffset;      // root offset that puts the front sight on the screen center
            public bool HasSight;
            // Gun parts (all optional)
            public Transform Slide, Pump, Mag, SupportHand, ShellVis, CasingVis;
            public Vector3 SlidePos, PumpPos, MagPos, SupportPos, ShellVisPos;
        }

        readonly List<Owned> owned = new();
        readonly Dictionary<AmmoType, int> reserve = new() { { AmmoType.Light, 0 }, { AmmoType.Shells, 0 }, { AmmoType.Rifle, 0 } };

        PlayerController controller;
        CharacterController body;
        InputAction attack;
        int current = -1;
        float nextAttackTime;
        bool reloading, reloadInterrupt;
        float reloadBlend;
        float actionT = 1f;      // 0..1 progress of the current swing/kick
        float sprintBlend;       // 0..1 eased sprint pose
        AudioSource lastShotVoice;
        // Automatic-weapon kick: a damped spring (position in m, rotation in degrees) so sustained fire settles into
        // a steady pushed-back position with a little shake, and recovers smoothly when the trigger is released.
        Vector3 kickPos, kickPosVel, kickRot, kickRotVel;
        [SerializeField] float kickSpring = 26f;      // natural frequency (rad/s)
        [SerializeField] float kickDamping = 0.78f;   // damping ratio (1 = no overshoot)
        float actionDuration = 0.3f;
        float equipT = 1f;
        Vector3 swayPos, swayVel;
        Vector2 swayRot;
        float spreadBloom;
        bool rightJab, backhand;
        float swingTilt;

        // Bounds-checked: a script reload during Play mode empties the inventory but can keep the index
        public WeaponData Current => current >= 0 && current < owned.Count ? owned[current].Data : null;
        public int Magazine => current >= 0 && current < owned.Count ? owned[current].Magazine : 0;
        public int Reserve => Current != null && Current.ammoType != AmmoType.None ? reserve[Current.ammoType] : 0;
        public bool Reloading => reloading;
        public bool HasRealWeapon => owned.Count > 1;
        public float SpreadBloom => spreadBloom;
        /// <summary>0 at the hip, 1 fully aimed down the sights.</summary>
        public float AimAmount => aim;
        public bool IsAiming => aimHeld;

        float aim;
        bool aimHeld;
        CinemachineCamera vcam;
        public int OwnedCount => owned.Count;

        public event Action Changed;
        public event Action<bool, bool> HitConfirmed;   // (killed, headshot)
        /// <summary>Raised on every attack (true for melee). Combat AI hears it.</summary>
        public event Action<bool> Fired;

        void Awake()
        {
            controller = GetComponent<PlayerController>();
            body = GetComponent<CharacterController>();
            attack = controller.Actions.FindActionMap("Player", true).FindAction("Attack", true);
            if (aimCamera == null) aimCamera = Camera.main;
            hitMask &= ~(1 << 2); // never hit the player or ejected debris (Ignore Raycast layer)
        }

        void Start()
        {
            foreach (var c in FindObjectsByType<CinemachineCamera>())
                if (c.Follow == controller.Head) vcam = c;
            if (vcam != null) hipFov = vcam.Lens.FieldOfView;
            Give(fists, silent: true);
            if (AdminSettings.AllWeapons) GiveAll();
        }

        void OnEnable()
        {
            AdminSettings.GiveAllWeaponsRequested += GiveAll;
            AdminSettings.AddAmmoRequested += AddAdminAmmo;
        }

        void OnDisable()
        {
            AdminSettings.GiveAllWeaponsRequested -= GiveAll;
            AdminSettings.AddAmmoRequested -= AddAdminAmmo;
        }

        /// <summary>Admin: give every weapon in the loot table, keeping the current one equipped.</summary>
        void GiveAll()
        {
            if (current < 0) return;
            var keep = current;
            var list = new List<WeaponData>();
            if (allWeapons != null) foreach (var w in allWeapons) if (w != null && w != fists && !list.Contains(w)) list.Add(w);
            foreach (var w in list)
            {
                bool have = owned.Exists(o => o.Data == w);
                if (!have) Give(w, silent: true);
            }
            Equip(keep, silent: true);
        }

        void AddAdminAmmo()
        {
            AddAmmo(AmmoType.Light, 50);
            AddAmmo(AmmoType.Shells, 50);
            AddAmmo(AmmoType.Rifle, 90);
        }

        static float Dmg(float amount) => AdminSettings.OneHitKills ? 100000f : amount;

        bool InputLive => GameManager.InputLive && !controller.Health.IsDead;

        void Update()
        {
            float dt = Time.deltaTime;
            spreadBloom = Mathf.MoveTowards(spreadBloom, 0f, dt * 2.5f);
            if (current < 0) return;
            if (controller.Health.IsDead)
            {
                if (viewRoot.gameObject.activeSelf) viewRoot.gameObject.SetActive(false);
                return;
            }

            // Bare hands only show while throwing a punch
            if (Current == fists && owned[current].View != null)
            {
                bool show = actionT < 1f;
                if (owned[current].View.activeSelf != show) owned[current].View.SetActive(show);
            }

            if (InputLive)
            {
                HandleSwitching();

                var data = Current;
                if (data == null) return;   // inventory lost to a script reload during Play mode
                bool wantsAttack = debugFire ||
                    (data.automatic || data.kind == WeaponKind.Melee ? attack.IsPressed() : attack.WasPressedThisFrame());

                // Firing interrupts a shell-by-shell reload once there's a round to shoot.
                if (wantsAttack && reloading && data.reloadPerShell && owned[current].Magazine > 0)
                    reloadInterrupt = true;

                if (wantsAttack && Time.time >= nextAttackTime && !reloading && equipT >= 0.6f)
                    TryAttack();

                var kb = Keyboard.current;
                if (kb != null && kb.rKey.wasPressedThisFrame) TryReload();
            }

            AnimateView(dt);
        }

        void HandleSwitching()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (AdminSettings.DevMenuOpen) { /* number keys belong to the dev menu */ }
                else if (kb.digit1Key.wasPressedThisFrame) Equip(0);
                if (kb.digit2Key.wasPressedThisFrame) Equip(1);
                if (kb.digit3Key.wasPressedThisFrame) Equip(2);
                if (kb.digit4Key.wasPressedThisFrame) Equip(3);
                if (kb.digit5Key.wasPressedThisFrame) Equip(4);
            }
            var mouse = Mouse.current;
            if (mouse != null && owned.Count > 1)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll > 0.1f) Equip((current + 1) % owned.Count);
                else if (scroll < -0.1f) Equip((current - 1 + owned.Count) % owned.Count);
            }
        }

        // ---------- Inventory ----------

        /// <summary>Adds a weapon, or turns a duplicate into ammo. Returns the toast text.</summary>
        public string Give(WeaponData data, bool silent = false)
        {
            foreach (var o in owned)
            {
                if (o.Data != data) continue;
                if (data.ammoType == AmmoType.None) return null;
                AddAmmo(data.ammoType, data.magazineSize);
                return $"+{data.magazineSize} {AmmoName(data.ammoType)}";
            }

            var view = Instantiate(data.viewModel, viewRoot, false);
            view.name = data.viewModel.name;
            foreach (var r in view.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            var e = new Owned
            {
                Data = data,
                View = view,
                Muzzle = FindDeep(view.transform, "Muzzle"),
                Magazine = data.magazineSize,
                BasePos = view.transform.localPosition,
                BaseRot = view.transform.localRotation,
            };
            // Aim point: the front sight (pistol) or bead (shotgun), expressed in camera space at the base pose
            var sight = FindDeep(view.transform, "Sight") ?? FindDeep(view.transform, "Bead");
            if (sight != null)
            {
                var sp = viewRoot.InverseTransformPoint(sight.position) - view.transform.localPosition + e.BasePos;
                e.AimOffset = new Vector3(-sp.x, -sp.y + 0.012f, -aimPullIn);
                e.HasSight = true;
            }
            e.Grip = view.transform.Find("Grip");
            if (e.Grip != null) e.GripRot = e.Grip.localRotation;
            e.HandR = view.transform.Find("HandR");
            e.HandL = view.transform.Find("HandL");
            if (e.HandR != null) { e.HandRPos = e.HandR.localPosition; e.HandRRot = e.HandR.localRotation; }
            if (e.HandL != null) { e.HandLPos = e.HandL.localPosition; e.HandLRot = e.HandL.localRotation; }

            e.Slide = FindDeep(view.transform, "Slide");
            e.Pump = FindDeep(view.transform, "Pump");
            e.Mag = FindDeep(view.transform, "Mag");
            e.ShellVis = FindDeep(view.transform, "ShellVis");
            e.CasingVis = FindDeep(view.transform, "CasingVis");
            e.SupportHand = e.HandL == null ? FindDeep(view.transform, "HandL") : null;
            if (e.Slide != null) e.SlidePos = e.Slide.localPosition;
            if (e.Pump != null) e.PumpPos = e.Pump.localPosition;
            if (e.Mag != null) e.MagPos = e.Mag.localPosition;
            if (e.SupportHand != null) e.SupportPos = e.SupportHand.localPosition;
            if (e.ShellVis != null) { e.ShellVisPos = e.ShellVis.localPosition; e.ShellVis.gameObject.SetActive(false); }
            if (e.CasingVis != null) e.CasingVis.gameObject.SetActive(false);

            var tip = FindDeep(view.transform, "Tip");
            if (tip != null && FXManager.ParticleMaterial != null) e.Trail = MakeTrail(tip);
            view.SetActive(false);
            owned.Add(e);
            if (data.ammoType != AmmoType.None) reserve[data.ammoType] += data.startingReserve;

            Equip(owned.Count - 1, silent);
            return data.displayName.ToUpperInvariant();
        }

        public bool AddAmmo(AmmoType type, int amount)
        {
            if (type == AmmoType.None) return false;
            reserve[type] += amount;
            Changed?.Invoke();
            return true;
        }

        public static string AmmoName(AmmoType t) => t == AmmoType.Shells ? "SHELLS" : t == AmmoType.Rifle ? "RIFLE ROUNDS" : "ROUNDS";

        void Equip(int index, bool silent = false)
        {
            if (index < 0 || index >= owned.Count || index == current) return;
            StopAllCoroutines();
            reloading = false;
            reloadInterrupt = false;
            if (current >= 0)
            {
                ResetParts(owned[current]);
                owned[current].View.SetActive(false);
            }
            current = index;
            owned[current].View.SetActive(true);
            equipT = 0f;
            actionT = 1f;
            nextAttackTime = Time.time + 0.2f;
            if (!silent) SoundManager.Play2D(Sfx.Equip, 0.6f);
            Changed?.Invoke();
        }

        static void ResetParts(Owned o)
        {
            if (o.Slide != null) o.Slide.localPosition = o.SlidePos;
            if (o.Pump != null) o.Pump.localPosition = o.PumpPos;
            if (o.Mag != null) { o.Mag.localPosition = o.MagPos; o.Mag.gameObject.SetActive(true); }
            if (o.SupportHand != null) o.SupportHand.localPosition = o.SupportPos;
            if (o.ShellVis != null) o.ShellVis.gameObject.SetActive(false);
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        // ---------- Attacks ----------

        void TryAttack()
        {
            var o = owned[current];
            var data = o.Data;

            if (data.kind == WeaponKind.Hitscan && o.Magazine <= 0)
            {
                SoundManager.Play2D(Sfx.Click, 0.7f);
                nextAttackTime = Time.time + 0.3f;
                TryReload();
                return;
            }

            nextAttackTime = Time.time + data.cooldown;
            actionT = 0f;
            actionDuration = data.kind == WeaponKind.Melee
                ? Mathf.Min(data.cooldown, data.windup + strikeTime + 0.3f)
                : Mathf.Min(data.cooldown, 0.22f);
            if (data.kind == WeaponKind.Melee)
            {
                rightJab = !rightJab;
                backhand = !backhand;
                swingTilt = UnityEngine.Random.Range(-swingTiltVariation, swingTiltVariation);
            }

            if (data.kind == WeaponKind.Melee) StartCoroutine(MeleeRoutine(data));
            else FireHitscan(o);
            Fired?.Invoke(data.kind == WeaponKind.Melee);
        }

        IEnumerator MeleeRoutine(WeaponData data)
        {
            // Wind up, then the whoosh as the strike starts. The hit lands mid-strike.
            yield return new WaitForSeconds(data.windup);
            SoundManager.Play2D(Sfx.Swing, 0.6f, 0.1f);
            yield return new WaitForSeconds(strikeTime * 0.5f);

            var cam = aimCamera.transform;
            Vector3 origin = cam.position;
            Vector3 fwd = cam.forward;
            var hits = Physics.OverlapSphere(origin + fwd * data.range * 0.5f, data.range * 0.65f, hitMask,
                QueryTriggerInteraction.Ignore);

            var targets = new List<(Health h, Collider c, float d)>();
            foreach (var col in hits)
            {
                var h = col.GetComponentInParent<Health>();
                if (h == null || h == controller.Health || h.IsDead) continue;
                if (targets.Exists(t => t.h == h)) continue;
                Vector3 to = col.bounds.center - origin;
                float dist = to.magnitude;
                if (dist > data.range + 0.6f) continue;
                if (Vector3.Angle(fwd, to) > data.swingAngle * 0.5f && dist > 0.9f) continue;
                targets.Add((h, col, dist));
            }
            targets.Sort((a, b) => a.d.CompareTo(b.d));

            int count = Mathf.Min(targets.Count, data.maxTargets);
            bool anyKill = false;
            for (int i = 0; i < count; i++)
            {
                var (h, col, _) = targets[i];
                Vector3 point = col.ClosestPoint(origin + fwd * 0.5f);
                Vector3 dir = (col.bounds.center - origin).normalized;
                h.TakeDamage(new DamageInfo
                {
                    Amount = Dmg(data.damage),
                    Point = point,
                    Direction = dir,
                    Knockback = data.knockback,
                    Source = gameObject,
                });
                FXManager.Blood(point, -fwd + dir * 0.3f, 12);
                anyKill |= h.IsDead;
            }

            if (count > 0)
            {
                var hitSfx = data.sound switch
                {
                    WeaponSound.Blade => Sfx.BladeHit,
                    WeaponSound.Blunt => Sfx.BluntHit,
                    _ => Sfx.Punch,
                };
                SoundManager.Play2D(hitSfx, 0.9f, 0.08f);
                FeelManager.HitStop(data.hitStop * (anyKill ? 1.6f : 1f));
                FeelManager.Shake(data.shake * (anyKill ? 1.5f : 1f));
                HitConfirmed?.Invoke(anyKill, false);
            }
            else if (Physics.Raycast(origin, fwd, out var env, data.range, hitMask, QueryTriggerInteraction.Ignore))
            {
                FXManager.Dust(env.point, env.normal, 6);
                SoundManager.PlayAt(Sfx.Punch, env.point, 0.4f);
            }
        }

        void FireHitscan(Owned o)
        {
            var data = o.Data;
            if (!AdminSettings.InfiniteAmmo) o.Magazine--;

            var cam = aimCamera.transform;
            Vector3 origin = cam.position;
            Vector3 muzzle = o.Muzzle != null ? o.Muzzle.position : origin + cam.forward * 0.6f;
            float spreadDeg = (data.spread + spreadBloom * 2.5f) * Mathf.Lerp(1f, 0.35f, aim);

            var damageByTarget = new Dictionary<Health, (float dmg, Vector3 point, Vector3 dir, bool head)>();
            for (int i = 0; i < data.pellets; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spreadDeg * Mathf.Deg2Rad);
                Vector3 dir = (cam.forward + cam.right * r.x + cam.up * r.y).normalized;
                Vector3 end = origin + dir * data.range;

                if (Physics.Raycast(origin, dir, out var hit, data.range, hitMask, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    var h = hit.collider.GetComponentInParent<Health>();
                    if (h != null && h != controller.Health && !h.IsDead)
                    {
                        var zone = hit.collider.GetComponent<HitZone>();
                        float dmg = data.damage * (zone != null ? zone.damageMultiplier : 1f);
                        bool head = zone != null && zone.isHead;
                        if (damageByTarget.TryGetValue(h, out var acc))
                            damageByTarget[h] = (acc.dmg + dmg, acc.point, acc.dir, acc.head || head);
                        else
                            damageByTarget[h] = (dmg, hit.point, dir, head);
                        FXManager.Blood(hit.point, hit.normal, data.pellets > 1 ? 5 : 12);
                    }
                    else
                    {
                        FXManager.Dust(hit.point, hit.normal, data.pellets > 1 ? 3 : 8);
                    }
                }
                FXManager.Tracer(muzzle, end);
            }

            bool anyHit = false, anyKill = false, anyHead = false;
            foreach (var kv in damageByTarget)
            {
                var v = kv.Value;
                kv.Key.TakeDamage(new DamageInfo
                {
                    Amount = Dmg(v.dmg),
                    Point = v.point,
                    Direction = v.dir,
                    Knockback = data.knockback,
                    Headshot = v.head,
                    Source = gameObject,
                });
                anyHit = true;
                anyKill |= kv.Key.IsDead;
                anyHead |= v.head;
            }
            if (anyHit)
            {
                HitConfirmed?.Invoke(anyKill, anyHead);
                if (anyKill) FeelManager.HitStop(data.hitStop);
            }

            // Automatic fire: each new shot ducks the previous shot's echo tail, so tails don't pile up into clipping
            // (the last shot of a burst still rings out in full)
            if (data.automatic && lastShotVoice != null && SoundManager.LimitVoices) SoundManager.Duck(lastShotVoice, 0.3f, 0.08f);
            lastShotVoice = SoundManager.Play2D(data.sound == WeaponSound.Shotgun ? Sfx.Shotgun : data.sound == WeaponSound.Rifle ? Sfx.RifleShot : Sfx.Pistol, data.sound == WeaponSound.Rifle ? 0.9f : 1f, 0.06f);
            FXManager.MuzzleFlash(muzzle, cam.forward, data.pellets > 1 ? 1.5f : 1f);
            FeelManager.Shake(data.shake);
            if (data.automatic)
            {
                controller.AddRecoil(data.recoil * Mathf.Lerp(1f, 0.7f, aim), 3.2f, 14f);
                // Gun-model kick: mostly back, some up, a little random roll and yaw (a spring settles it)
                float aimK = Mathf.Lerp(1f, 0.6f, aim);
                kickPosVel += new Vector3(UnityEngine.Random.Range(-0.15f, 0.15f), 0.35f, -2.6f) * aimK;
                kickRotVel += new Vector3(-190f, UnityEngine.Random.Range(-70f, 70f), UnityEngine.Random.Range(-110f, 110f)) * aimK;
            }
            else controller.AddRecoil(data.recoil * Mathf.Lerp(1f, 0.7f, aim));
            spreadBloom = Mathf.Min(1f, spreadBloom + 0.35f);
            Changed?.Invoke();

            if (o.Slide != null) StartCoroutine(SlideKick(o));
            if (o.Pump != null) StartCoroutine(PumpCycle(o, 0.22f, ejectShell: true));

            if (o.Magazine <= 0) StartCoroutine(AutoReloadSoon());
        }

        IEnumerator AutoReloadSoon()
        {
            yield return new WaitForSeconds(0.4f);
            TryReload();
        }

        // ---------- Gun mechanics ----------

        /// <summary>The pistol slide snaps back, kicks out a casing, and returns.</summary>
        IEnumerator SlideKick(Owned o)
        {
            yield return Ease.Tween(0.03f, k => o.Slide.localPosition = o.SlidePos + Vector3.back * 0.045f * k, Ease.Linear, unscaled: false);
            EjectCasing(o);
            bool empty = o.Magazine <= 0;
            // Slide locks back on an empty magazine until the reload racks it.
            if (empty) yield break;
            yield return Ease.Tween(0.07f, k => o.Slide.localPosition = o.SlidePos + Vector3.back * 0.045f * (1f - k), Ease.OutCubic, unscaled: false);
        }

        /// <summary>Rack the shotgun pump back and forward, ejecting the spent shell.</summary>
        IEnumerator PumpCycle(Owned o, float delay, bool ejectShell)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            SoundManager.Play2D(Sfx.PumpBack, 0.75f, 0.04f);
            yield return Ease.Tween(0.1f, k =>
            {
                o.Pump.localPosition = o.PumpPos + Vector3.back * 0.09f * k;
                if (o.SupportHand != null) o.SupportHand.localPosition = o.SupportPos + Vector3.back * 0.09f * k;
            }, Ease.OutCubic, unscaled: false);
            if (ejectShell && o.ShellVis != null)
                SpawnDebris(o.ShellVis, o.View.transform.TransformDirection(new Vector3(1.4f, 1.6f, -0.3f)), Sfx.ShellDrop, 1.2f);
            yield return new WaitForSeconds(0.03f);
            SoundManager.Play2D(Sfx.PumpForward, 0.75f, 0.04f);
            yield return Ease.Tween(0.1f, k =>
            {
                o.Pump.localPosition = o.PumpPos + Vector3.back * 0.09f * (1f - k);
                if (o.SupportHand != null) o.SupportHand.localPosition = o.SupportPos + Vector3.back * 0.09f * (1f - k);
            }, Ease.OutCubic, unscaled: false);
        }

        void EjectCasing(Owned o)
        {
            if (o.CasingVis == null) return;
            var right = o.View.transform.TransformDirection(new Vector3(1.6f, 1.4f, -0.4f));
            SpawnDebris(o.CasingVis, right, Sfx.CasingTink, 1f);
        }

        /// <summary>Drops a physical copy of a view-model part into the world (magazine, casing, shell).</summary>
        void SpawnDebris(Transform template, Vector3 velocity, Sfx impact, float scale)
        {
            var go = Instantiate(template.gameObject, template.position, template.rotation);
            go.SetActive(true);
            go.transform.localScale = template.lossyScale * scale;
            go.name = template.name + "_Debris";
            SetLayerRecursive(go, 2);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
                if (r.GetComponent<Collider>() == null) r.gameObject.AddComponent<BoxCollider>();
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(body, c);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = controller.PlanarVelocity + velocity;
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 18f;
            go.AddComponent<Debris>().Init(impact, 6f);
        }

        void TryReload()
        {
            if (reloading || current < 0) return;
            var o = owned[current];
            var data = o.Data;
            if (data.kind != WeaponKind.Hitscan || o.Magazine >= data.magazineSize) return;
            if (reserve[data.ammoType] <= 0) return;
            reloadInterrupt = false;
            StartCoroutine(data.reloadPerShell ? ReloadShells(o) : ReloadMagazine(o));
        }

        /// <summary>
        /// Pistol: the old magazine drops out, the support hand fetches a new one, seats it,
        /// and racks the slide if the gun was empty.
        /// </summary>
        IEnumerator ReloadMagazine(Owned o)
        {
            reloading = true;
            Changed?.Invoke();
            float T = o.Data.reloadTime;
            bool wasEmpty = o.Magazine <= 0;
            Vector3 handDown = new Vector3(-0.02f, -0.24f, -0.06f);
            Vector3 magBelow = new Vector3(0f, -0.2f, 0f);

            yield return new WaitForSeconds(T * 0.16f);
            if (o.Mag != null)
            {
                SoundManager.Play2D(Sfx.MagOut, 0.8f, 0.04f);
                SpawnDebris(o.Mag, Vector3.down * 1.2f + aimCamera.transform.forward * 0.3f, Sfx.MagDrop, 1f);
                o.Mag.gameObject.SetActive(false);
            }

            // Hand drops to grab a fresh magazine.
            yield return Ease.Tween(T * 0.3f, k =>
            {
                if (o.SupportHand != null) o.SupportHand.localPosition = o.SupportPos + handDown * k;
            }, Ease.InOutCubic, unscaled: false);

            // The new mag rises with the hand and seats.
            if (o.Mag != null) o.Mag.gameObject.SetActive(true);
            yield return Ease.Tween(T * 0.28f, k =>
            {
                if (o.Mag != null) o.Mag.localPosition = o.MagPos + magBelow * (1f - k);
                if (o.SupportHand != null) o.SupportHand.localPosition = o.SupportPos + Vector3.Lerp(handDown, Vector3.zero, k);
            }, Ease.OutCubic, unscaled: false);
            SoundManager.Play2D(Sfx.MagIn, 0.85f, 0.04f);

            int take = Mathf.Min(o.Data.magazineSize - o.Magazine, reserve[o.Data.ammoType]);
            reserve[o.Data.ammoType] -= take;
            o.Magazine += take;
            Changed?.Invoke();

            if (wasEmpty && o.Slide != null)
            {
                yield return new WaitForSeconds(T * 0.06f);
                SoundManager.Play2D(Sfx.SlideRack, 0.85f, 0.04f);
                yield return Ease.Tween(0.08f, k => o.Slide.localPosition = o.SlidePos + Vector3.back * 0.045f * (1f - k), Ease.OutCubic, unscaled: false);
            }
            else
            {
                yield return new WaitForSeconds(T * 0.1f);
            }

            ResetParts(o);
            reloading = false;
            Changed?.Invoke();
        }

        /// <summary>
        /// Shotgun: load one shell at a time into the port under the receiver.
        /// Firing interrupts it once there's a shell in.
        /// </summary>
        IEnumerator ReloadShells(Owned o)
        {
            reloading = true;
            Changed?.Invoke();
            bool wasEmpty = o.Magazine <= 0;
            float per = o.Data.reloadTime;
            Vector3 handToPort = new Vector3(0.0f, -0.05f, -0.24f);
            Vector3 shellBelow = new Vector3(0f, -0.06f, -0.02f);

            yield return new WaitForSeconds(0.12f);
            while (o.Magazine < o.Data.magazineSize && reserve[o.Data.ammoType] > 0 && !reloadInterrupt)
            {
                // Hand moves from the pump to the port, holding a shell.
                if (o.ShellVis != null)
                {
                    o.ShellVis.gameObject.SetActive(true);
                    o.ShellVis.localPosition = o.ShellVisPos + shellBelow;
                }
                yield return Ease.Tween(per * 0.4f, k =>
                {
                    if (o.SupportHand != null) o.SupportHand.localPosition = o.SupportPos + handToPort * k;
                }, Ease.InOutCubic, unscaled: false);

                // Push the shell up into the port.
                yield return Ease.Tween(per * 0.25f, k =>
                {
                    if (o.ShellVis != null) o.ShellVis.localPosition = o.ShellVisPos + shellBelow * (1f - k);
                    if (o.SupportHand != null) o.SupportHand.localPosition = o.SupportPos + handToPort + Vector3.up * 0.05f * k;
                }, Ease.OutCubic, unscaled: false);
                if (o.ShellVis != null) o.ShellVis.gameObject.SetActive(false);
                SoundManager.Play2D(Sfx.ShellInsert, 0.8f, 0.06f);
                reserve[o.Data.ammoType]--;
                o.Magazine++;
                Changed?.Invoke();

                // Hand returns toward the pump.
                yield return Ease.Tween(per * 0.35f, k =>
                {
                    if (o.SupportHand != null)
                        o.SupportHand.localPosition = o.SupportPos + Vector3.Lerp(handToPort + Vector3.up * 0.05f, Vector3.zero, k);
                }, Ease.InOutCubic, unscaled: false);
            }

            if (wasEmpty && o.Pump != null && o.Magazine > 0)
                yield return PumpCycle(o, 0.05f, ejectShell: false);

            ResetParts(o);
            reloading = false;
            reloadInterrupt = false;
            Changed?.Invoke();
        }

        // ---------- View model animation ----------

        /// <summary>
        /// A real swing that alternates two variants:
        /// the forehand (the user-approved diagonal, upper right to lower left, around the right shoulder)
        /// and the backhand (upper left to lower right, pivoting from the chest so it doesn't
        /// mirror unnaturally). Each swing gets a small random tilt.
        /// </summary>
        void SwingPose(Owned o, ref Vector3 pos, ref Quaternion rot)
        {
            float t = actionT;
            float w = Mathf.Clamp(o.Data.windup / actionDuration, 0.05f, 0.5f);
            float s = Mathf.Clamp(strikeTime / actionDuration, 0.08f, 0.35f);
            float f = Mathf.Min(0.78f, w + s + 0.14f);

            Vector3 pivot, wind, strike, follow;
            float cockWind, cockStrike, cockFollow;
            if (!backhand)
            {
                // The user-approved forehand. Keep these numbers.
                pivot = shoulderPivot;
                wind = new Vector3(-36f, 40f, 26f);
                strike = new Vector3(26f, -60f, -36f);
                follow = new Vector3(32f, -72f, -42f);
                cockWind = -60f; cockStrike = 45f; cockFollow = 55f;
            }
            else
            {
                // The backhand: the head is laid across the body to the upper left, pointing forward (away from the camera),
                // then sweeps down to the right head-first. The cock stays forward so the head, not the butt end, leads.
                pivot = chestPivot;
                wind = new Vector3(-28f, -30f, 58f);
                strike = new Vector3(22f, 34f, -14f);
                follow = new Vector3(28f, 44f, -24f);
                cockWind = 12f; cockStrike = 55f; cockFollow = 62f;
            }
            Vector3 tilt = new Vector3(swingTilt * 0.5f, 0f, swingTilt);
            wind += tilt; strike += tilt; follow += tilt;

            Vector3 e;
            float cock;
            if (t < w)
            {
                float k = Ease.OutCubic(t / w);
                e = wind * k; cock = cockWind * k;
            }
            else if (t < w + s)
            {
                float k = (t - w) / s;
                k *= k; // accelerate into the hit
                e = Vector3.Lerp(wind, strike, k); cock = Mathf.Lerp(cockWind, cockStrike, k);
            }
            else if (t < f)
            {
                float k = Ease.OutCubic((t - w - s) / (f - w - s));
                e = Vector3.Lerp(strike, follow, k); cock = Mathf.Lerp(cockStrike, cockFollow, k);
            }
            else
            {
                float k = Ease.InOutCubic((t - f) / (1f - f));
                e = Vector3.Lerp(follow, Vector3.zero, k); cock = Mathf.Lerp(cockFollow, 0f, k);
            }

            var r = Quaternion.Euler(e);
            pos = pivot + r * (pos - pivot);
            rot = r * rot;
            if (o.Grip != null) o.Grip.localRotation = o.GripRot * Quaternion.Euler(cock, 0f, 0f);
            if (o.Trail != null) o.Trail.emitting = t >= w * 0.85f && t < w + s + 0.1f;
        }

        /// <summary>Alternating jabs: one fist snaps forward toward the center, then retracts.</summary>
        void AnimatePunch(Owned o, float t)
        {
            var hand = rightJab ? o.HandR : o.HandL;
            var other = rightJab ? o.HandL : o.HandR;
            other.localPosition = rightJab ? o.HandLPos : o.HandRPos;
            other.localRotation = rightJab ? o.HandLRot : o.HandRRot;
            Vector3 p0 = rightJab ? o.HandRPos : o.HandLPos;
            Quaternion r0 = rightJab ? o.HandRRot : o.HandLRot;
            float k = t < 0.3f ? Ease.OutCubic(t / 0.3f) : t < 0.42f ? 1f : 1f - Ease.InOutCubic((t - 0.42f) / 0.58f);
            float toCenter = rightJab ? -1f : 1f;
            hand.localPosition = p0 + new Vector3(0.08f * toCenter, 0.07f, 0.24f) * k;
            hand.localRotation = r0 * Quaternion.Euler(-14f * k, 10f * toCenter * k, 30f * toCenter * k);
        }

        static TrailRenderer MakeTrail(Transform tip)
        {
            var tr = tip.gameObject.AddComponent<TrailRenderer>();
            tr.sharedMaterial = FXManager.ParticleMaterial;
            tr.time = 0.1f;
            tr.minVertexDistance = 0.01f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.07f), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Palette.UIBone, 0f), new GradientColorKey(Palette.Spores, 1f) },
                new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.alignment = LineAlignment.View;
            tr.emitting = false;
            return tr;
        }

        void UpdateAim(float dt, Owned o)
        {
            var mouse = Mouse.current;
            aimHeld = InputLive && !controller.IsSprinting && !reloading && (debugAim || (mouse != null && mouse.rightButton.isPressed));
            aim = Mathf.MoveTowards(aim, aimHeld ? 1f : 0f, dt / aimTime);
            controller.AimSlowdown = Mathf.Lerp(1f, 0.7f, aim);
            controller.AimSensitivity = Mathf.Lerp(1f, 0.75f, aim);
            if (vcam != null)
            {
                float target = o.Data.kind == WeaponKind.Melee ? meleeAimFov : gunAimFov;
                vcam.Lens.FieldOfView = Mathf.Lerp(hipFov, target, Ease.InOutCubic(aim));
            }
        }

        void AnimateView(float dt)
        {
            var o = owned[current];
            var data = o.Data;
            var t = o.View.transform;
            UpdateAim(dt, o);
            float steady = Mathf.Lerp(1f, 0.25f, aim);   // aiming calms sway and bob

            // Sway: the weapon lags behind look and movement.
            Vector2 look = Vector2.ClampMagnitude(controller.LookDelta * 0.02f, 1f);
            Vector3 localVel = transform.InverseTransformDirection(controller.PlanarVelocity);
            Vector3 swayTarget = new Vector3(-look.x - localVel.x * 0.15f, -look.y, -localVel.z * 0.08f) * swayAmount;
            swayPos = Vector3.SmoothDamp(swayPos, swayTarget, ref swayVel, 0.08f, Mathf.Infinity, dt);
            swayRot = Vector2.Lerp(swayRot, new Vector2(look.y, -look.x) * swayRotate * 4f, 1f - Mathf.Exp(-10f * dt));

            Vector3 pos = o.BasePos + swayPos * steady + controller.BobOffset * viewBobScale * steady;
            // Heuristic aim pose: a slight move toward center, a touch up and closer (no true sight alignment)
            if (data.kind == WeaponKind.Hitscan) pos += aimPose * Ease.InOutCubic(aim);
            Quaternion rot = o.BaseRot * Quaternion.Euler(swayRot.x * steady, swayRot.y * steady, 0f);

            // Sprint pose: eased in and out instead of snapping (faster out when firing, so shots stay responsive)
            {
                bool wantSprint = controller.IsSprinting && actionT >= 1f && !reloading;
                float speed = wantSprint ? 1f / 0.22f : actionT < 1f ? 1f / 0.1f : 1f / 0.2f;
                sprintBlend = Mathf.MoveTowards(sprintBlend, wantSprint ? 1f : 0f, dt * speed);
                float sk = Ease.InOutCubic(sprintBlend);
                pos += new Vector3(0.04f, -0.05f, 0f) * sk;
                rot *= Quaternion.Euler(8f * sk, -18f * sk, 6f * sk);
            }

            if (o.Grip != null) o.Grip.localRotation = o.GripRot;
            if (o.Trail != null && actionT >= 1f) o.Trail.emitting = false;

            // Attack animation
            if (actionT < 1f)
            {
                actionT = Mathf.Min(1f, actionT + dt / Mathf.Max(0.05f, actionDuration));
                if (data.kind == WeaponKind.Melee)
                {
                    if (o.HandR != null && o.HandL != null) AnimatePunch(o, actionT);
                    else SwingPose(o, ref pos, ref rot);
                }
                else if (!data.automatic)
                {
                    float k = 1f - Ease.OutCubic(actionT);
                    float strength = data.pellets > 1 ? 1.6f : 1f;
                    pos += new Vector3(0f, 0.02f, -0.09f) * k * strength;
                    rot *= Quaternion.Euler(-14f * k * strength, 0f, 2f * k);
                }
            }

            // Automatic kick spring (semi-implicit, two substeps for stability)
            {
                float w = kickSpring, z = kickDamping, h = dt * 0.5f;
                for (int i = 0; i < 2; i++)
                {
                    kickPosVel += (-w * w * kickPos - 2f * z * w * kickPosVel) * h;
                    kickPos += kickPosVel * h;
                    kickRotVel += (-w * w * kickRot - 2f * z * w * kickRotVel) * h;
                    kickRot += kickRotVel * h;
                }
                pos += kickPos;
                rot *= Quaternion.Euler(kickRot);
            }

            // Reload pose: tilt the gun to show the magazine well or the loading port.
            reloadBlend = Mathf.MoveTowards(reloadBlend, reloading ? 1f : 0f, dt / 0.18f);
            if (reloadBlend > 0f)
            {
                float k = Ease.InOutCubic(reloadBlend);
                if (data.reloadPerShell)
                {
                    pos += new Vector3(-0.04f, -0.03f, -0.02f) * k;
                    rot *= Quaternion.Euler(-6f * k, -18f * k, 32f * k);
                }
                else
                {
                    pos += new Vector3(-0.05f, -0.02f, -0.03f) * k;
                    rot *= Quaternion.Euler(14f * k, -22f * k, 24f * k);
                }
            }

            // Equip raise
            if (equipT < 1f)
            {
                equipT = Mathf.Min(1f, equipT + dt / 0.3f);
                float k = 1f - Ease.OutBack(equipT);
                pos += new Vector3(0f, -0.35f, 0f) * k;
                rot *= Quaternion.Euler(40f * k, 0f, 0f);
            }

            t.localPosition = pos;
            t.localRotation = rot;
        }
    }
}
