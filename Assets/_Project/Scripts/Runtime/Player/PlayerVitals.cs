using System;
using System.Collections.Generic;
using Alcax.Combat;
using Alcax.Core;
using Alcax.Items;
using UnityEngine;

namespace Alcax.Player
{
    /// <summary>
    /// The player's body: stamina (sprinting and jumping use it, it recovers after a short pause; empty means no
    /// sprinting until it's back to 30%), and the survival needs (hunger, thirst, energy) that drain slowly. Low
    /// needs slow stamina recovery; an empty stomach or no water costs health. Timed effects (buffs and debuffs)
    /// come from consumables and sleep: Energized (no stamina drain, then a Crash), Caffeinated, Well rested.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerVitals : MonoBehaviour
    {
        public static PlayerVitals Instance { get; private set; }

        // Stamina
        const float SprintDrain = 16f, JumpCost = 10f, Regen = 16f, RegenDelay = 1.0f, ExhaustedUntil = 0.3f;   // fraction of max
        // Needs drain to zero over: hunger 40 min, thirst 25 min, energy 60 min
        const float HungerRate = 100f / (40f * 60f), ThirstRate = 100f / (25f * 60f), EnergyRate = 100f / (60f * 60f);
        public const float Low = 25f;

        public const float BaseStamina = 200f;
        public float Stamina { get; private set; } = BaseStamina;
        public float Hunger { get; private set; } = 85f;
        public float Thirst { get; private set; } = 80f;
        public float Energy { get; private set; } = 90f;
        public bool Exhausted { get; private set; }
        public float MaxStamina => BaseStamina * (Energy < Low ? 0.6f : 1f) + (Has("well_rested") ? BaseStamina * 0.2f : 0f);
        public bool CanSprint => !Exhausted;

        public class Effect
        {
            public string id, label;
            public float remaining, duration;
            public bool debuff;
        }

        readonly List<Effect> effects = new List<Effect>();
        public IReadOnlyList<Effect> Effects => effects;

        PlayerController controller;
        Health health;
        float lastDrain = -10f, starveTick;

        static readonly Dictionary<string, (string label, float seconds, bool debuff)> Library = new Dictionary<string, (string, float, bool)>
        {
            ["energized"] = ("ENERGIZED", 12f, false),     // no stamina drain; ends in a crash
            ["crash"] = ("CRASH", 3f, true),               // stamina recovers at 40%
            ["caffeinated"] = ("CAFFEINATED", 45f, false), // stamina recovers 25% faster
            ["well_rested"] = ("WELL RESTED", 180f, false), // +20 max stamina, recovers 20% faster
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() { }

        void Awake()
        {
            Instance = this;
            controller = GetComponent<PlayerController>();
            health = GetComponent<Health>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public bool Has(string id) => effects.Exists(e => e.id == id);

        public void AddEffect(string id)
        {
            if (!Library.TryGetValue(id, out var def)) return;
            var e = effects.Find(x => x.id == id);
            if (e == null) { e = new Effect { id = id, label = def.label, debuff = def.debuff }; effects.Add(e); }
            e.duration = e.remaining = def.seconds;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (controller.Health.IsDead) return;

            // Effects tick down; Energized ends in a Crash
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var e = effects[i];
                e.remaining -= dt;
                if (e.remaining > 0f) continue;
                effects.RemoveAt(i);
                if (e.id == "energized") AddEffect("crash");
            }

            // Needs
            if (!AdminSettings.NoNeeds && GameManager.IsPlaying)
            {
                float run = controller.IsSprinting ? 1.6f : 1f;
                Hunger = Mathf.Max(0f, Hunger - HungerRate * run * dt);
                Thirst = Mathf.Max(0f, Thirst - ThirstRate * run * dt);
                Energy = Mathf.Max(0f, Energy - EnergyRate * dt);
                if (Hunger <= 0f || Thirst <= 0f)
                {
                    starveTick += dt;
                    if (starveTick > 2f)
                    {
                        starveTick = 0f;
                        if (health.Current > 5f) health.TakeDamage(new DamageInfo { Amount = 1f, Point = transform.position, Direction = Vector3.down });
                    }
                }
            }

            // Stamina
            bool free = Has("energized") || AdminSettings.InfiniteStamina;
            if (controller.IsSprinting && !free)
            {
                Stamina = Mathf.Max(0f, Stamina - SprintDrain * dt);
                lastDrain = Time.time;
                if (Stamina <= 1f && !Exhausted) { Exhausted = true; Audio.SoundManager.Play2D(Audio.Sfx.PlayerHurt, 0.25f, 0.1f); }
            }
            else if (Time.time - lastDrain > RegenDelay)
                Stamina = Mathf.Min(MaxStamina, Stamina + Regen * RegenFactor() * dt);
            if (Stamina > MaxStamina) Stamina = MaxStamina;
            if (Exhausted && Stamina >= ExhaustedUntil * MaxStamina) Exhausted = false;
        }

        float RegenFactor()
        {
            float k = 1f;
            if (Has("crash")) k *= 0.4f;
            if (Has("caffeinated")) k *= 1.25f;
            if (Has("well_rested")) k *= 1.2f;
            if (Hunger < Low) k *= 0.7f;
            if (Thirst < Low) k *= 0.7f;
            if (Energy < Low) k *= 0.6f;
            if (controller.IsCrouching || controller.PlanarVelocity.sqrMagnitude < 0.1f) k *= 1.25f;   // resting recovers faster
            return k;
        }

        /// <summary>A jump costs stamina (none when it's free).</summary>
        public void OnJump()
        {
            if (Has("energized") || AdminSettings.InfiniteStamina) return;
            Stamina = Mathf.Max(0f, Stamina - JumpCost);
            lastDrain = Time.time;
            if (Stamina <= 1f) Exhausted = true;
        }

        /// <summary>What eating or drinking something does to the needs and effects.</summary>
        public bool WouldHelp(ItemDefinition d) => d != null && (d.hunger > 0f && Hunger < 99f || d.thirst > 0f && Thirst < 99f || d.energy > 0f && Energy < 99f || !string.IsNullOrEmpty(d.effect));

        public void Apply(ItemDefinition d)
        {
            if (d == null) return;
            Hunger = Mathf.Clamp(Hunger + d.hunger, 0f, 100f);
            Thirst = Mathf.Clamp(Thirst + d.thirst, 0f, 100f);
            Energy = Mathf.Clamp(Energy + d.energy, 0f, 100f);
            if (!string.IsNullOrEmpty(d.effect)) AddEffect(d.effect);
        }

        /// <summary>A night's (or an afternoon's) sleep: rested, a little hungrier and thirstier, patched up a bit.</summary>
        public void Sleep()
        {
            Energy = 100f;
            Hunger = Mathf.Max(5f, Hunger - 12f);
            Thirst = Mathf.Max(5f, Thirst - 18f);
            health.Heal(30f);
            effects.RemoveAll(e => e.debuff);
            AddEffect("well_rested");
            Stamina = MaxStamina;
            Exhausted = false;
        }

        /// <summary>Drinking from a tap. False when not thirsty.</summary>
        public bool Drink(float amount)
        {
            if (Thirst >= 99f) return false;
            Thirst = Mathf.Min(100f, Thirst + amount);
            return true;
        }

        public void Fill()
        {
            Hunger = Thirst = Energy = 100f;
            Stamina = MaxStamina;
            Exhausted = false;
        }

        /// <summary>Short labels for low needs ("HUNGRY", "DEHYDRATED"...), for the HUD.</summary>
        public string NeedsLabel()
        {
            var l = new List<string>();
            if (Hunger <= 0f) l.Add("STARVING"); else if (Hunger < Low) l.Add("HUNGRY");
            if (Thirst <= 0f) l.Add("DEHYDRATED"); else if (Thirst < Low) l.Add("THIRSTY");
            if (Energy < Low) l.Add("TIRED");
            return string.Join("   ", l);
        }
    }
}
