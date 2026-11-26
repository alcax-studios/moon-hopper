using Alcax.Audio;
using Alcax.Combat;
using Alcax.FX;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// An operative's gun under the same rules as the player's: a magazine and reload time, a spread
    /// cone that blooms per shot and recovers, and muzzle climb that the shooter must pull back down.
    /// Bullets are hitscan from the muzzle and can hit the world, the player, or a squadmate in the way.
    /// </summary>
    public class OperativeWeapon
    {
        readonly Operative op;
        readonly EnemyArchetype a;

        public int Magazine { get; private set; }
        public bool Reloading { get; private set; }
        public float ReloadProgress => Reloading ? Mathf.Clamp01(1f - (reloadEnd - Time.time) / a.reloadTime) : 0f;
        public float LastShotTime { get; private set; } = -100f;
        public float Bloom { get; private set; }          // degrees added to the base spread
        /// <summary>Muzzle climb (degrees pitch up, yaw) that the aim has to fight.</summary>
        public Vector2 Kick;
        public float Ammo01 => Magazine / (float)a.magazine;
        /// <summary>Spare rounds left (unlimited when the ammo budget is off).</summary>
        public int Reserve { get; private set; }
        public bool Budgeted => op.Config.useAmmoBudget;
        /// <summary>Everything left, loaded and spare, as a fraction of a full load.</summary>
        public float Total01 => Budgeted ? (Magazine + Reserve) / (float)Mathf.Max(1, a.magazine + a.reserveRounds) : 1f;
        /// <summary>Out of ammo entirely: empty magazine and nothing to reload with.</summary>
        public bool Dry => Budgeted && Magazine <= 0 && Reserve <= 0 && !Reloading;
        public int ShotsFired { get; private set; }

        float nextShot, reloadEnd;

        public OperativeWeapon(Operative op, EnemyArchetype archetype)
        {
            this.op = op;
            a = archetype;
            Magazine = a.magazine;
            Reserve = a.reserveRounds;
        }

        public bool CanFire => !Reloading && Magazine > 0 && Time.time >= nextShot;

        public void StartReload()
        {
            if (Reloading || Magazine >= a.magazine) return;
            if (Budgeted && Reserve <= 0) return;
            Reloading = true;
            reloadEnd = Time.time + a.reloadTime * Random.Range(0.92f, 1.12f) * Mathf.Lerp(1.15f, 0.85f, op.Traits.skill);
            SoundManager.PlayAt(Sfx.MagOut, op.MuzzlePosition, 0.6f);
            Hearing.Emit(op.transform.position + Vector3.up, 8f, NoiseKind.Reload, op.gameObject);
            op.OnReloadStarted();
        }

        public void CancelReload() { Reloading = false; }

        public void Tick(float dt, bool moving)
        {
            if (Reloading && Time.time >= reloadEnd)
            {
                Reloading = false;
                int take = Budgeted ? Mathf.Min(a.magazine - Magazine, Reserve) : a.magazine - Magazine;
                if (Budgeted) Reserve -= take;
                Magazine += take;
                SoundManager.PlayAt(Sfx.MagIn, op.MuzzlePosition, 0.6f);
            }
            float recover = a.spreadRecovery * (moving ? 0.6f : 1f);
            Bloom = Mathf.MoveTowards(Bloom, 0f, recover * dt);
            // Recoil control: skill decides how fast the climb is dragged back down
            float control = Mathf.Lerp(5f, 16f, op.Traits.skill) * (1f - op.Emotion.stress * 0.35f);
            Kick = Vector2.Lerp(Kick, Vector2.zero, 1f - Mathf.Exp(-control * dt));
        }

        /// <summary>Fire one round along the (already aimed) direction. Spread, recoil, and movement apply here.</summary>
        public void Fire(Vector3 aimDir, float movementSpread)
        {
            if (!CanFire) return;
            nextShot = Time.time + a.fireInterval * Random.Range(0.95f, 1.08f);
            Magazine--;
            ShotsFired++;
            LastShotTime = Time.time;

            var muzzle = op.MuzzlePosition;
            float spread = a.spreadBase + Bloom + movementSpread;
            var player = PlayerTarget.Instance;
            bool hitPlayer = false;
            for (int i = 0; i < a.pellets; i++)
            {
                var r = Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
                var right = Vector3.Cross(Vector3.up, aimDir).normalized;
                var up = Vector3.Cross(aimDir, right);
                var dir = (aimDir + right * r.x + up * r.y).normalized;
                hitPlayer |= Shoot(muzzle, dir, player);
            }

            Bloom = Mathf.Min(a.spreadMax, Bloom + a.spreadPerShot);
            Kick += new Vector2(a.recoilPerShot * Random.Range(0.7f, 1.3f), a.recoilPerShot * Random.Range(-0.45f, 0.45f));

            SoundManager.PlayAt(a.shotSound, muzzle, 1f, 0.05f);
            FXManager.MuzzleFlash(muzzle, aimDir, a.pellets > 1 ? 1.4f : 0.9f);
            Hearing.Emit(muzzle, a.noiseRadius, NoiseKind.Gunshot, op.gameObject);
            if (Magazine <= 0) { op.Mind.OnMagazineEmpty(); if (Budgeted && Reserve <= 0) op.OnOutOfAmmo(); }
        }

        bool Shoot(Vector3 origin, Vector3 dir, PlayerTarget player)
        {
            float range = a.range;
            // Start the ray just ahead of our own body so it never hits ourselves
            origin += dir * 0.05f;
            bool world = Physics.Raycast(origin, dir, out var hit, range, AILayers.Bullet, QueryTriggerInteraction.Ignore);
            while (world && hit.collider.transform.IsChildOf(op.transform))
            {
                var o2 = hit.point + dir * 0.02f;
                world = Physics.Raycast(o2, dir, out hit, range - Vector3.Distance(origin, o2), AILayers.Bullet, QueryTriggerInteraction.Ignore);
            }
            float worldDist = world ? Vector3.Distance(origin, hit.point) : range;

            if (player != null && player.Raycast(origin, dir, worldDist, out float pd, out bool head, out float bodyZone))   // the player stands on Ignore Raycast: test his capsule
            {
                var point = origin + dir * pd;
                player.Health.TakeDamage(new DamageInfo
                {
                    Amount = a.damage * bodyZone,
                    Point = point, Direction = dir, Knockback = 0.6f, Headshot = head, Source = op.gameObject,
                });
                player.AddPressure(0.35f);
                FXManager.Blood(point, -dir, 6);
                FXManager.Tracer(op.MuzzlePosition, point);
                if (op.IsHostile(player)) op.OnHitPlayer();   // a hit on a friendly player is an accident, not a success
                return true;
            }

            var end = origin + dir * worldDist;
            if (world)
            {
                var h = hit.collider.GetComponentInParent<Health>();
                if (h != null && !h.IsDead)
                {
                    // Friendly fire is real: a squadmate in the line of fire takes the round
                    var zone = hit.collider.GetComponent<HitZone>();
                    h.TakeDamage(new DamageInfo
                    {
                        Amount = a.damage * (zone != null ? zone.damageMultiplier : 1f),
                        Point = hit.point, Direction = dir, Knockback = 0.5f, Headshot = zone != null && zone.isHead, Source = op.gameObject,
                    });
                    FXManager.Blood(hit.point, hit.normal, 8);
                    SoundManager.PlayAt(Sfx.BodyHit, hit.point, 0.7f);
                    var victim = h.GetComponent<Operative>();
                    if (victim != null && op.IsHostile(victim)) op.OnHitPlayer();   // a hit on the target counts the same, whoever it is
                }
                else
                {
                    FXManager.Dust(hit.point, hit.normal, 6);
                    if (Random.value < 0.35f) Hearing.Emit(hit.point, 6f, NoiseKind.Impact, op.gameObject);
                }
            }
            FXManager.Tracer(op.MuzzlePosition, end);

// Rounds cracking past other operatives suppress them (and tell them who's shooting)
            foreach (var other in Operative.All)
            {
                if (other == op || other.IsDead || (other.Squad != null && other.Squad == op.Squad)) continue;
                var chest = other.ChestPosition;
                var seg = end - origin;
                float tt = Mathf.Clamp01(Vector3.Dot(chest - origin, seg) / Mathf.Max(0.0001f, seg.sqrMagnitude));
                float m2 = Vector3.Distance(origin + seg * tt, chest);
                if (m2 < 2.2f && tt > 0.02f) other.OnNearMiss(1f - m2 / 2.2f, op.MuzzlePosition, op);
            }

            // Near miss: the supersonic crack past your head
            if (player != null && !player.IsDead)
            {
                float miss = player.MissDistance(origin, end, out var closest);
                if (miss < 2.2f)
                {
                    SoundManager.PlayAt(Sfx.BulletFlyby, closest, Mathf.Lerp(0.9f, 0.35f, miss / 2.2f), 0.15f);
                    player.AddPressure(0.12f * (1f - miss / 2.2f));
                }
            }
            return false;
        }
    }
}
