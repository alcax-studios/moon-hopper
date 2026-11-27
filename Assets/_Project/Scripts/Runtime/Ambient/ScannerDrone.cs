using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Combat;
using Alcax.Dialogue;
using Alcax.Items;
using UnityEngine;

namespace Alcax.Ambient
{
    /// <summary>
    /// A free-flying station scanner. It drifts through the open air of a FlightVolume (smooth acceleration, banking
    /// into turns, a gentle bob, feeling ahead for walls and glass), picks something interesting nearby (a person,
    /// the player, a prop, a container, a steam vent), flies to a hover point beside it, and scans it: a flat fan of
    /// light from its eye sweeps up and down over the target for a few seconds. Then it moves on. It can be shot down.
    /// </summary>
    public class ScannerDrone : MonoBehaviour
    {
        enum Mode { Wander, Approach, Scan, Rest }

        const float MaxSpeed = 3.2f, Accel = 2.2f, Radius = 0.35f;
        static readonly int Mask = ~((1 << 2) | (1 << 8));   // the world and glass, not people (they move)

        Mode mode;
        Vector3 velocity, goal;
        float modeUntil, bobSeed, scanStart, nextLook;
        Transform body, eye, fanPivot;
        MeshRenderer fanRenderer;
        Light eyeLight;
        Health health;
        bool dead;

        // What it's looking at
        Transform target;
        Vector3 targetPoint;
        float targetTop, targetBottom, targetWidth;
readonly Dictionary<Object, float> recentlyScanned = new Dictionary<Object, float>();

        // Scanning the player: stolen goods are found partway through (the beam turns red), and reported when the scan
        // completes. Breaking line of sight or getting away before then loses it. Dropping the goods doesn't help.
        const float DetectAt = 0.4f;
        public static readonly List<ScannerDrone> All = new List<ScannerDrone>();
        bool flagged;
        int flaggedValue;
        float lostFor;
        MaterialPropertyBlock beamTint;
        public bool ScanningPlayer => !dead && mode == Mode.Scan && target != null && AI.PlayerTarget.Instance != null && target == AI.PlayerTarget.Instance.transform;
        public static bool AnyScanningPlayer { get { foreach (var d in All) if (d != null && d.ScanningPlayer) return true; return false; } }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        Bounds Volume
        {
            get
            {
                var v = FlightVolume.Main;
                return v != null ? v.Bounds : new Bounds(home, new Vector3(30f, 8f, 30f));
            }
        }
        Vector3 home;

        void Awake()
        {
            home = transform.position;
            bobSeed = Random.value * 10f;
            BuildModel();
            health = gameObject.AddComponent<Health>();
            health.SetMax(40f);
            health.Died += OnDied;
            health.Damaged += _ => { if (!dead) { Wander(); SoundManager.PlayAt(Sfx.CasingTink, transform.position, 0.6f, 0.2f); FX.FXManager.Sparks(transform.position, Random.onUnitSphere, 6); } };
            Wander();
        }

        // ---------- Model (an original design: a lens-eyed sensor pod with two stabilizer rings) ----------

        void BuildModel()
        {
            var shell = AmbientArt.Lit("DroneShell", new Color(0.72f, 0.74f, 0.76f), 0.6f, 0.7f);
            var dark = AmbientArt.Lit("DroneDark", new Color(0.12f, 0.13f, 0.15f), 0.4f, 0.6f);
            var accent = AmbientArt.Lit("DroneAccent", new Color(0.95f, 0.55f, 0.15f), 0.5f, 0.2f);
            var glow = AmbientArt.Glow("DroneEye", new Color(0.45f, 0.95f, 1f), 4f);
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            AmbientArt.Part(PrimitiveType.Sphere, "Pod", body, Vector3.zero, new Vector3(0.5f, 0.42f, 0.56f), shell);
            AmbientArt.Part(PrimitiveType.Cylinder, "Band", body, Vector3.zero, new Vector3(0.53f, 0.035f, 0.53f), dark);
            AmbientArt.Part(PrimitiveType.Cube, "Stripe", body, new Vector3(0f, 0.12f, -0.1f), new Vector3(0.06f, 0.06f, 0.3f), accent);
            AmbientArt.Part(PrimitiveType.Cylinder, "LensHousing", body, new Vector3(0f, 0f, 0.25f), new Vector3(0.2f, 0.05f, 0.2f), dark, new Vector3(90f, 0f, 0f));
            var lens = AmbientArt.Part(PrimitiveType.Sphere, "Lens", body, new Vector3(0f, 0f, 0.29f), new Vector3(0.12f, 0.12f, 0.06f), glow);
            eye = lens.transform;
            foreach (int s in new[] { -1, 1 })
            {
                AmbientArt.Part(PrimitiveType.Cylinder, "Ring" + s, body, new Vector3(0.34f * s, -0.02f, -0.02f), new Vector3(0.26f, 0.02f, 0.26f), dark, new Vector3(0f, 0f, 90f));
                AmbientArt.Part(PrimitiveType.Cube, "Strut" + s, body, new Vector3(0.24f * s, -0.02f, -0.02f), new Vector3(0.12f, 0.03f, 0.05f), shell);
                AmbientArt.Part(PrimitiveType.Sphere, "Thruster" + s, body, new Vector3(0.34f * s, -0.12f, -0.02f), new Vector3(0.07f, 0.03f, 0.07f), AmbientArt.Glow("DroneThrust", new Color(0.4f, 0.8f, 1f), 2.5f));
                // Thruster wash: a soft pale jet pushed down out of each ring
                AmbientArt.Jet(body, new Vector3(0.34f * s, -0.14f, -0.02f), new Vector3(90f, 0f, 0f), new Color(0.75f, 0.92f, 1f, 0.22f), 22f, 1.6f, 0.12f, 0.45f, 10f);
            }
            AmbientArt.Part(PrimitiveType.Cylinder, "Antenna", body, new Vector3(-0.08f, 0.28f, -0.12f), new Vector3(0.012f, 0.1f, 0.012f), dark);

            // The scan fan, hinged at the eye
            fanPivot = new GameObject("ScanFan").transform;
            fanPivot.SetParent(transform, false);
            var mf = fanPivot.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = AmbientArt.Fan();
            fanRenderer = fanPivot.gameObject.AddComponent<MeshRenderer>();
            fanRenderer.sharedMaterial = AmbientArt.Beam();
            fanRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fanRenderer.enabled = false;

            eyeLight = new GameObject("EyeLight").AddComponent<Light>();
            eyeLight.transform.SetParent(eye, false);
            eyeLight.type = LightType.Point; eyeLight.range = 4f; eyeLight.intensity = 0.4f; eyeLight.color = new Color(0.5f, 0.95f, 1f);
            eyeLight.shadows = LightShadows.None;

            var col = gameObject.AddComponent<SphereCollider>();
            col.radius = Radius;
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // ---------- Deciding ----------

        void Wander()
        {
            mode = Mode.Wander;
            target = null;
            fanRenderer.enabled = false;
            goal = PickOpenPoint(transform.position, 4f, 14f);
            modeUntil = Time.time + Random.Range(6f, 12f);
        }

        Vector3 PickOpenPoint(Vector3 from, float minDist, float maxDist)
        {
            var b = Volume;
            for (int i = 0; i < 24; i++)
            {
                var p = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
                float d = Vector3.Distance(p, from);
                if (d < minDist || d > maxDist) continue;
                if (Physics.CheckSphere(p, Radius + 0.4f, Mask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.SphereCast(from, Radius, (p - from).normalized, out _, d, Mask, QueryTriggerInteraction.Ignore)) continue;
                return p;
            }
            return from + Vector3.up * 0.5f;
        }

        /// <summary>Something worth a look: people first, then things. Not what it scanned in the last minute.</summary>
        bool PickTarget()
        {
            var options = new List<(Transform t, Object key, float score)>();
            void Consider(Transform t, Object key, float weight)
            {
                if (t == null) return;
                if (recentlyScanned.TryGetValue(key, out var at) && Time.time - at < 60f) return;
                float d = Vector3.Distance(t.position, transform.position);
                if (d > 22f || !Volume.Contains(new Vector3(t.position.x, Mathf.Clamp(t.position.y + 1.5f, Volume.min.y, Volume.max.y), t.position.z))) return;
                options.Add((t, key, weight * Random.Range(0.6f, 1.4f) / (1f + d * 0.08f)));
            }
            foreach (var n in Npc.Active) if (n != null && n.GetComponent<InfoBot>() == null) Consider(n.transform, n, 3f);
            var pt = AI.PlayerTarget.Instance;
            if (pt != null && !pt.IsDead) Consider(pt.transform, pt, 2.5f);
            foreach (var v in SteamVent.All) Consider(v.transform, v, 2.2f);
            foreach (var g in Grabbable.Active) if (g.GetComponent<RagdollCorpse>() == null && g.GetComponentInParent<RagdollCorpse>() == null) Consider(g.transform, g, 1.2f);
            foreach (var c in LootContainer.All) if (!c.IsBody) Consider(c.transform, c, 0.8f); else if (c.Available) Consider(c.transform, c, 3.5f);   // a body is very interesting
            if (options.Count == 0) return false;
            options.Sort((a, b2) => b2.score.CompareTo(a.score));
            var pick = options[Random.Range(0, Mathf.Min(3, options.Count))];
            target = pick.t;
            recentlyScanned[pick.key] = Time.time;
            MeasureTarget();
            return HoverPointFor(out goal);
        }

        void MeasureTarget()
        {
            var rs = target.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(target.position + Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f));
            bool first = true;
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            if (target.GetComponent<SteamVent>() != null) b = new Bounds(target.position + Vector3.up * 0.8f, new Vector3(0.9f, 1.6f, 0.9f));
            targetPoint = b.center;
            targetTop = b.max.y;
            targetBottom = b.min.y + 0.05f;
            targetWidth = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z), 0.4f, 2.2f);
        }

        bool HoverPointFor(out Vector3 p)
        {
            var b = Volume;
            float baseAng = Mathf.Atan2(transform.position.z - targetPoint.z, transform.position.x - targetPoint.x);
            for (int i = 0; i < 10; i++)
            {
                float ang = baseAng + (i % 2 == 0 ? 1 : -1) * (i / 2) * 0.6f;
                float dist = Random.Range(2.2f, 3f);
                var c = targetPoint + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist + Vector3.up * Random.Range(0.6f, 1.3f);
                c.y = Mathf.Clamp(c.y, b.min.y, b.max.y);
                if (Physics.CheckSphere(c, Radius + 0.3f, Mask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Linecast(c, targetPoint, out var hit, Mask, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(target)) continue;
                p = c;
                return true;
            }
            p = default;
            return false;
        }

        // ---------- Flying ----------

        void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;
            switch (mode)
            {
                case Mode.Wander:
                    if (Vector3.Distance(transform.position, goal) < 0.8f || Time.time > modeUntil)
                    {
                        if (Random.value < 0.65f && PickTarget()) { mode = Mode.Approach; modeUntil = Time.time + 15f; }
                        else if (Random.value < 0.3f) { mode = Mode.Rest; modeUntil = Time.time + Random.Range(2f, 4f); }
                        else Wander();
                    }
                    break;
                case Mode.Rest:
                    if (Time.time > modeUntil) Wander();
                    break;
                case Mode.Approach:
                    if (target == null) { Wander(); break; }
                    MeasureTarget();
                    if (Time.time > nextLook) { nextLook = Time.time + 0.6f; if (Vector3.Distance(goal, targetPoint) > 3.6f) HoverPointFor(out goal); }
                    if (Vector3.Distance(transform.position, goal) < 0.6f) { mode = Mode.Scan; scanStart = Time.time; modeUntil = Time.time + Random.Range(3.5f, 5.5f); flagged = false; lostFor = 0f; SoundManager.PlayAt(Sfx.TalkBlip, transform.position, 0.5f, 0.3f); }
                    else if (Time.time > modeUntil) Wander();
                    break;
                case Mode.Scan:
if (target == null || Time.time > modeUntil) { FinishScan(); break; }
                    if (ScanningPlayer && !UpdatePlayerScan()) break;
                    MeasureTarget();
                    // It walked off: keep scanning while following it (a fresh hover point beside it now and then)
                    if (Time.time > nextLook && Vector3.Distance(goal, targetPoint) > 3.4f) { nextLook = Time.time + 0.5f; if (HoverPointFor(out var g2)) goal = g2; }
                    if (Vector3.Distance(transform.position, targetPoint) > 14f) { Wander(); break; }   // lost it
                    break;
            }
            Fly(dt);
            AnimateScan();
        }

/// <summary>The scan on the player: find stolen goods partway, lose them if they break sight. False when it gave up.</summary>
        bool UpdatePlayerScan()
        {
            var p = AI.PlayerTarget.Instance;
            var inv = Items.PlayerInventory.Instance;
            float progress = (Time.time - scanStart) / Mathf.Max(0.1f, modeUntil - scanStart);
            if (!flagged && progress >= DetectAt && inv != null && inv.StolenValue() > 0)
            {
                flagged = true;
                flaggedValue = inv.StolenValue();
                SoundManager.PlayAt(Sfx.Click, transform.position, 0.9f, 0f);
                SoundManager.PlayAt(Sfx.TalkBlip, transform.position, 0.9f, 0f);
            }
            // Out of sight long enough (behind something, around a corner): it lost you
            bool blocked = Physics.Linecast(eye.position, p.HeadPosition, out var hit, AI.AILayers.World, QueryTriggerInteraction.Ignore);
            lostFor = blocked ? lostFor + Time.deltaTime : 0f;
            if (lostFor > 1.2f) { flagged = false; Wander(); return false; }
            return true;
        }

        void FinishScan()
        {
            if (flagged && ScanningPlayerTarget())
            {
                AI.StationSecurity.Instance?.ReportTheft(flaggedValue, "a scanner drone");
                Items.PlayerInventory.Instance?.Say("REPORTED");
                SoundManager.PlayAt(Sfx.Radio, transform.position, 1f, 0f);
            }
            else SoundManager.PlayAt(Sfx.UIClick, transform.position, 0.3f, 0.2f);
            flagged = false;
            Wander();
        }

        bool ScanningPlayerTarget() => target != null && AI.PlayerTarget.Instance != null && target == AI.PlayerTarget.Instance.transform;

        /// <summary>Something stolen was dropped while I was scanning: that's a find too.</summary>
        public void SawDrop(int value)
        {
            if (!ScanningPlayer) return;
            flagged = true;
            flaggedValue = Mathf.Max(flaggedValue, value);
        }

        void Fly(float dt)
        {
            var pos = transform.position;
            var to = goal - pos;
            float dist = to.magnitude;
            // Arrive: ease off near the goal; hold station while scanning
            float top = mode == Mode.Scan ? (flagged ? MaxSpeed * 1.4f : ScanningPlayer ? MaxSpeed : MaxSpeed * 0.8f) : MaxSpeed;
            float speed = mode == Mode.Rest ? 0f : Mathf.Min(top, dist * 0.9f);
            var want = dist > 0.01f ? to / dist * speed : Vector3.zero;
            // Feel ahead: steer off walls, glass, and the ceiling
            if (velocity.sqrMagnitude > 0.05f && Physics.SphereCast(pos, Radius, velocity.normalized, out var hit, 1.8f, Mask, QueryTriggerInteraction.Ignore))
            {
                want += hit.normal * MaxSpeed * (1f - hit.distance / 1.8f) * 1.5f;
                if (hit.distance < 0.6f && mode == Mode.Wander) Wander();
            }
            // Personal space
            var pt = AI.PlayerTarget.Instance;
            if (pt != null)
            {
                var away = pos - pt.HeadPosition;
                if (away.magnitude < 1.4f) want += away.normalized * 2f;
            }
            velocity = Vector3.MoveTowards(velocity, want, Accel * dt);
// Hover isn't perfect: an uneven drift up and down and a little side sway, always (idle too)
            float tb = Time.time + bobSeed;
            var bob = new Vector3((Mathf.PerlinNoise(bobSeed, tb * 0.35f) - 0.5f) * 0.1f,
                                  (Mathf.PerlinNoise(tb * 0.4f, bobSeed) - 0.5f) * 0.24f + Mathf.Sin(tb * 1.3f) * 0.03f,
                                  (Mathf.PerlinNoise(bobSeed + 5f, tb * 0.3f) - 0.5f) * 0.1f);
            var next = pos + velocity * dt;
            var b = Volume;
            next = new Vector3(Mathf.Clamp(next.x, b.min.x, b.max.x), Mathf.Clamp(next.y, b.min.y, b.max.y), Mathf.Clamp(next.z, b.min.z, b.max.z));
            transform.position = next;
            body.localPosition = bob;
            body.localRotation = Quaternion.Euler((Mathf.PerlinNoise(tb * 0.5f, bobSeed + 2f) - 0.5f) * 6f, 0f, (Mathf.PerlinNoise(bobSeed + 3f, tb * 0.45f) - 0.5f) * 7f);

            // Face: the target while approaching or scanning, else where it's going; bank into turns, pitch with speed
            Vector3 face = target != null && mode != Mode.Wander ? targetPoint - pos : velocity.sqrMagnitude > 0.05f ? velocity : transform.forward;
            face.y *= 0.35f;
            if (face.sqrMagnitude > 0.001f)
            {
                var yaw = Quaternion.LookRotation(face.normalized, Vector3.up);
                var local = transform.InverseTransformDirection(velocity);
                var tilt = Quaternion.Euler(Mathf.Clamp(local.z * 6f, -14f, 14f), 0f, Mathf.Clamp(-local.x * 10f, -22f, 22f));
                transform.rotation = Quaternion.Slerp(transform.rotation, yaw * tilt, 1f - Mathf.Exp(-3f * dt));
            }
        }

        void AnimateScan()
        {
            bool scanning = mode == Mode.Scan && target != null;
            fanRenderer.enabled = scanning;
eyeLight.intensity = Mathf.MoveTowards(eyeLight.intensity, scanning ? (flagged ? 2.6f : 1.6f) : 0.4f, Time.deltaTime * 4f);
            var tint = flagged && scanning ? new Color(1f, 0.25f, 0.2f, 1f) : Color.white;
            eyeLight.color = flagged && scanning ? new Color(1f, 0.25f, 0.2f) : new Color(0.5f, 0.95f, 1f);
            beamTint ??= new MaterialPropertyBlock();
            fanRenderer.GetPropertyBlock(beamTint); beamTint.SetColor("_Color", tint); fanRenderer.SetPropertyBlock(beamTint);
            if (!scanning) return;
            // Sweep: down and up over the target, faster at first, then a steady pass
            float t = Time.time - scanStart;
            float k = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 1.1f);
            var aim = new Vector3(targetPoint.x, Mathf.Lerp(targetTop, targetBottom, k), targetPoint.z);
            var from = eye.position;
            var dir = aim - from;
            fanPivot.position = from;
            fanPivot.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            float len = dir.magnitude;
            fanPivot.localScale = new Vector3(targetWidth * (len / Mathf.Max(0.5f, len)) * 1.1f, 1f, len);
        }

        // ---------- Down ----------

void OnDied()
        {
            dead = true;
            fanRenderer.enabled = false;
            // A small blast (mostly show), a ring and the lens torn off, and the scorched pod drops
            Explosion.Blast(transform.position, 2.5f, 25f, gameObject, 0.6f);
            AmbientArt.Wreck(gameObject, new[] { "Ring1", "Lens", "Antenna", "Stripe" }, health);
            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = 6f;
            rb.linearVelocity = velocity + Vector3.up * 1.5f;
            rb.angularVelocity = Random.insideUnitSphere * 6f;
            enabled = false;
        }
    }
}
