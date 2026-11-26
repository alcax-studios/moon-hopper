using System.Collections;
using Alcax.Audio;
using Alcax.Core;
using UnityEngine;

namespace Alcax.Combat
{
    /// <summary>
    /// A steel practice target: it clangs, flashes, and swings back on its hinge when hit.
    /// After taking enough damage it falls flat, then resets a moment later. Good for testing weapons.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TargetPlate : MonoBehaviour
    {
        [SerializeField] Transform hinge;
        [SerializeField] Renderer plate;
        [SerializeField] float resetDelay = 1.6f;

        Health health;
        float swing, swingVel;
        float flash;
        bool down;
        MaterialPropertyBlock mpb;
        Color baseColor;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            health = GetComponent<Health>();
            health.Damaged += OnHit;
            health.Died += OnDown;
            mpb = new MaterialPropertyBlock();
            if (plate != null) baseColor = plate.sharedMaterial.GetColor(BaseColorId);
        }

        void OnHit(DamageInfo info)
        {
            swingVel += Mathf.Clamp(info.Amount, 5f, 60f) * 9f;
            flash = 1f;
            SoundManager.PlayAt(Sfx.TargetClang, transform.position + Vector3.up, Mathf.Clamp01(0.4f + info.Amount / 60f), 0.08f);
        }

        void OnDown()
        {
            down = true;
            StartCoroutine(ResetLater());
        }

        IEnumerator ResetLater()
        {
            yield return new WaitForSeconds(resetDelay);
            health.Revive();
            down = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float target = down ? 88f : 0f;
            // Damped spring around the hinge
            swingVel += ((target - swing) * 90f - swingVel * 9f) * dt;
            swing += swingVel * dt;
            swing = Mathf.Clamp(swing, -20f, 92f);
            if (hinge != null) hinge.localRotation = Quaternion.Euler(swing, 0f, 0f);

            if (plate != null && flash > 0f)
            {
                flash = Mathf.MoveTowards(flash, 0f, dt / 0.15f);
                plate.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, Color.Lerp(baseColor, Palette.StAccentOrange, flash));
                plate.SetPropertyBlock(mpb);
            }
        }
    }
}
