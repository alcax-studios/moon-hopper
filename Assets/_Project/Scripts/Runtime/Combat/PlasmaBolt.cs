using System;
using Alcax.Core;
using Alcax.FX;
using UnityEngine;

namespace Alcax.Combat
{
    /// <summary>
    /// A plasma bolt in flight: a white-hot core with a cyan glow, a short trail, shed sparks, and its own light.
    /// It travels from the camera along the aim (so it hits what the crosshair is on) while its visual starts at the
    /// muzzle and converges onto the true path over the first few meters. Sphere-casts each frame; passes through
    /// breakable glass weakened, stops on armored glass; damages Health (hit zones apply) and pushes rigidbodies.
    /// Pooled.
    /// </summary>
    public class PlasmaBolt : MonoBehaviour
    {
        const float Radius = 0.025f, ConvergeDistance = 4f, Length = 0.45f;

        public struct Shot
        {
            public Vector3 origin, dir, muzzle;
            public float speed, range, damage, knockback;
            public int mask;
            public GameObject source;
            public Health ignore;
            public Action<Health, bool, bool> onHit;   // (target, killed, headshot)
        }

        static readonly System.Collections.Generic.Stack<PlasmaBolt> pool = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() => pool.Clear();

        Shot shot;
        Vector3 pos, offset;
        float traveled, glassMul, shedAcc;
        LineRenderer core, halo;
        TrailRenderer trail;
        Light glowLight;

        public static void Fire(Shot s)
        {
            PlasmaBolt b = null;
            while (pool.Count > 0 && b == null) b = pool.Pop();
            if (b == null) b = Create();
            b.Launch(s);
        }

        static PlasmaBolt Create()
        {
            var lib = FXLibrary.Get();
            var go = new GameObject("PlasmaBolt");
            var b = go.AddComponent<PlasmaBolt>();
            b.core = Line(go.transform, "Core", lib != null ? lib.arc : null, 0.03f, PlasmaFX.EnergyHot);
            b.halo = Line(go.transform, "Halo", lib != null ? lib.glow : null, 0.16f, PlasmaFX.Energy * 0.55f);
            b.trail = go.AddComponent<TrailRenderer>();
            b.trail.sharedMaterial = lib != null ? lib.glow : null;
            b.trail.time = 0.07f;
            b.trail.minVertexDistance = 0.05f;
            b.trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.09f), new Keyframe(1f, 0f));
            b.trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(PlasmaFX.Energy, 0f), new GradientColorKey(PlasmaFX.Energy, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            b.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.trail.receiveShadows = false;
            b.glowLight = new GameObject("Light").AddComponent<Light>();
            b.glowLight.transform.SetParent(go.transform, false);
            b.glowLight.type = LightType.Point;
            b.glowLight.color = PlasmaFX.Energy;
            b.glowLight.range = 3.5f;
            b.glowLight.intensity = 2.2f;
            b.glowLight.shadows = LightShadows.None;
            return b;
        }

        static LineRenderer Line(Transform parent, string name, Material m, float width, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = m;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0.6f));
            lr.numCapVertices = 4;
            lr.alignment = LineAlignment.View;
            lr.startColor = new Color(c.r, c.g, c.b, 0.15f);
            lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        void Launch(Shot s)
        {
            shot = s;
            pos = s.origin;
            offset = s.muzzle - s.origin;
            traveled = 0f;
            glassMul = 1f;
            shedAcc = 0f;
            gameObject.SetActive(true);
            var v = Visual();
            transform.position = v;
            trail.Clear();
            Draw(v);
        }

        Vector3 Visual()
        {
            float k = Mathf.Clamp01(1f - traveled / ConvergeDistance);
            return pos + offset * (k * k * (3f - 2f * k));
        }

        void Draw(Vector3 head)
        {
            var tail = head - shot.dir * Mathf.Min(Length, traveled + 0.05f);
            core.SetPosition(0, tail); core.SetPosition(1, head);
            halo.SetPosition(0, tail); halo.SetPosition(1, head);
        }

        void Update()
        {
            float step = shot.speed * Time.deltaTime;
            for (int guard = 0; guard < 4 && step > 0f; guard++)
            {
                if (!Physics.SphereCast(pos, Radius, shot.dir, out var hit, step, shot.mask, QueryTriggerInteraction.Ignore))
                {
                    pos += shot.dir * step;
                    traveled += step;
                    step = 0f;
                    break;
                }
                pos = hit.point;
                traveled += hit.distance;
                step -= hit.distance;
                // Glass: breakable lets it through weakened, armored stops it
                if (hit.collider.TryGetComponent<GlassPane>(out var pane))
                {
                    if (pane.OnBulletHit(hit.point, hit.normal, shot.dir, shot.damage * glassMul, out float pass))
                    {
                        glassMul *= pass;
                        pos = hit.point + shot.dir * 0.03f;
                        continue;
                    }
                    PlasmaFX.Impact(hit.point, hit.normal, shot.dir, false, false);
                    Finish();
                    return;
                }
                Hit(hit);
                return;
            }
            if (traveled >= shot.range) { Finish(); return; }

            var v = Visual();
            transform.position = v;
            Draw(v);
            shedAcc += shot.speed * Time.deltaTime;
            while (shedAcc > 2.5f) { shedAcc -= 2.5f; PlasmaFX.Shed(v, shot.dir * shot.speed); }
        }

        void Hit(RaycastHit hit)
        {
            var h = hit.collider.GetComponentInParent<Health>();
            bool flesh = h != null && h != shot.ignore && !h.IsDead;
            if (flesh)
            {
                var zone = hit.collider.GetComponent<HitZone>();
                bool head = zone != null && zone.isHead;
                float dmg = shot.damage * glassMul * (zone != null ? zone.damageMultiplier : 1f);
                h.TakeDamage(new DamageInfo
                {
                    Amount = AdminSettings.OneHitKills ? 100000f : dmg,
                    Point = hit.point,
                    Direction = shot.dir,
                    Knockback = shot.knockback,
                    Headshot = head,
                    Source = shot.source,
                });
                FXManager.Blood(hit.point, hit.normal, 8);
                shot.onHit?.Invoke(h, h.IsDead, head);
            }
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(shot.dir * 6f * shot.knockback, hit.point, ForceMode.Impulse);
            // Scorch only on things that stay put
            bool mark = !flesh && hit.rigidbody == null && hit.collider.gameObject.layer != 8;
            PlasmaFX.Impact(hit.point, hit.normal, shot.dir, flesh || h != null, mark);
            Finish();
        }

        void Finish()
        {
            gameObject.SetActive(false);
            pool.Push(this);
        }
    }
}
