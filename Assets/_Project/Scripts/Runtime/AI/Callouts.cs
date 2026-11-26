using System;
using System.Collections.Generic;
using Alcax.Audio;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Alcax.AI
{
    public enum Callout
    {
        Contact, ThereHeIs, Reloading, CoverMe, Moving, Flanking, Suppressing, HesReloading, HesHit,
        ImHit, NeedHelp, LastStand, ManDown, LostHim, Push, FallBack, FlankOrder,
        Breaking, Surrender, LeaderDown, Rally, Taunt,
        Engaging, ContactConfirm, Copy, OutOfAmmo, Breaching, RoomClear,
    }

    /// <summary>
    /// Spoken information. A callout is never just flavor: it changes what the listeners know and do.
    /// Shouts carry to everyone in earshot (allies of any squad, and you: you can overhear plans and positions).
    /// Radio reaches every unit of the faction (its own squad first) after a short delay and is nearly silent, but you hear the squelch up close.
    /// Every callout shows as a subtitle when you can hear it.
    /// </summary>
    public static class Callouts
    {
        /// <summary>(speaker, line, radio, audible to the player)</summary>
        public static event Action<Operative, string, bool> Heard;
        /// <summary>Every callout, heard or not (for the debug comms log).</summary>
        public static readonly List<(float t, string text)> Log = new();

        static readonly Dictionary<(Squad, Callout), float> squadCooldown = new();

        static readonly Dictionary<Callout, string[]> Shouted = new()
        {
            [Callout.Contact] = new[] { "Contact!", "There! Over there!", "I see him!", "Enemy!" },
            [Callout.ThereHeIs] = new[] { "He's {0}!", "Over there, {0}!", "He moved, {0}!" },
            [Callout.Reloading] = new[] { "Reloading!", "I'm out! Cover me!", "Changing mag!" },
            [Callout.CoverMe] = new[] { "Cover me!", "Keep him down!" },
            [Callout.Moving] = new[] { "Moving!", "I'm moving up!", "Going!" },
            [Callout.Flanking] = new[] { "I'm going around {0}!", "Flanking {0}!" },
            [Callout.Suppressing] = new[] { "Keep firing!", "Suppressing!", "Light him up!" },
            [Callout.HesReloading] = new[] { "He's reloading! Push!", "He's empty! Go!" },
            [Callout.HesHit] = new[] { "I hit him!", "He's hurt! Push him!" },
            [Callout.ImHit] = new[] { "I'm hit!", "Argh! I'm hit!", "He got me!" },
            [Callout.NeedHelp] = new[] { "I need help!", "I'm bleeding out!", "Help me!" },
            [Callout.LastStand] = new[] { "Come on then!", "You want me? Come get me!", "I'm not running!" },
            [Callout.ManDown] = new[] { "Man down!", "He got {0}!", "We lost one!" },
            [Callout.LostHim] = new[] { "Where'd he go?", "I lost him!", "Anyone see him?" },
            [Callout.Push] = new[] { "Push! Push!", "Go, go, go!", "Move in!" },
            [Callout.FallBack] = new[] { "Fall back!", "Pull back! Pull back!" },
            [Callout.FlankOrder] = new[] { "{0}, get around {1}!", "{0}, flank him {1}!" },
            [Callout.Breaking] = new[] { "I'm out of here!", "Run! Run!", "Forget this!", "I'm not dying here!" },
            [Callout.Surrender] = new[] { "Don't shoot! I give up!", "Okay! Okay! I'm done!", "Please, don't shoot!" },
            [Callout.LeaderDown] = new[] { "They got {0}!", "{0}'s down! What do we do?" },
            [Callout.Rally] = new[] { "Hold together! Stay on him!", "Steady! He's one man!", "Nobody runs!" },
            [Callout.Taunt] = new[] { "He's alone! Get him!", "We've got him!", "He's finished!" },
            [Callout.Engaging] = new[] { "Engaging!" },
            [Callout.ContactConfirm] = new[] { "Contact, confirmed!" },
            [Callout.Copy] = new[] { "Copy!" },
            [Callout.OutOfAmmo] = new[] { "I'm out!", "I'm dry!" },
            [Callout.Breaching] = new[] { "Going in!", "Moving in!" },
            [Callout.RoomClear] = new[] { "Clear!", "Room clear!" },
        };

        static readonly Dictionary<Callout, string[]> Radioed = new()
        {
            [Callout.Contact] = new[] { "Contact, {0}.", "Tango, {0}." },
            [Callout.ThereHeIs] = new[] { "Target moving, {0}.", "Tango relocating, {0}." },
            [Callout.Reloading] = new[] { "Reloading.", "Changing." },
            [Callout.CoverMe] = new[] { "Cover.", "Need cover." },
            [Callout.Moving] = new[] { "Moving.", "Bounding." },
            [Callout.Flanking] = new[] { "Flanking {0}.", "Going {0}." },
            [Callout.Suppressing] = new[] { "Suppressing.", "Covering." },
            [Callout.HesReloading] = new[] { "Tango's dry. Push.", "He's reloading." },
            [Callout.HesHit] = new[] { "Hit on tango.", "Tango hit." },
            [Callout.ImHit] = new[] { "I'm hit.", "Taking fire, I'm hit." },
            [Callout.NeedHelp] = new[] { "Man hurt, need support.", "Need assist." },
            [Callout.LastStand] = new[] { "Holding here.", "Not moving." },
            [Callout.ManDown] = new[] { "{0} is down.", "Man down." },
            [Callout.LostHim] = new[] { "Lost visual.", "No visual." },
            [Callout.Push] = new[] { "Push.", "Execute. Move in." },
            [Callout.FallBack] = new[] { "Fall back.", "Break contact." },
            [Callout.FlankOrder] = new[] { "{0}, flank {1}.", "{0}, take {1}." },
            [Callout.Breaking] = new[] { "Breaking contact.", "Falling back, can't hold." },
            [Callout.Surrender] = new[] { "Don't shoot! I'm done!", "Hands up! Hands up!" },
            [Callout.LeaderDown] = new[] { "Lead is down. {0} is down.", "Actual is down." },
            [Callout.Rally] = new[] { "Hold your sectors.", "Steady. Stay on task." },
            [Callout.Taunt] = new[] { "Tango is isolated. Close in.", "We have him. Tighten up." },
            [Callout.Engaging] = new[] { "Engaging." },
            [Callout.ContactConfirm] = new[] { "Contact confirmed." },
            [Callout.Copy] = new[] { "Copy." },
            [Callout.OutOfAmmo] = new[] { "Winchester.", "I'm dry." },
            [Callout.Breaching] = new[] { "Breaching.", "Entering." },
            [Callout.RoomClear] = new[] { "Room clear.", "Clear." },
        };

        /// <summary>Recorded voice lines per callout, with the subtitle that matches the recording (null keeps the text line).</summary>
        static readonly Dictionary<Callout, (Sfx voice, string subtitle)> Recorded = new()
        {
            [Callout.Contact] = (Sfx.VoContact, "Contact!"),
            [Callout.ContactConfirm] = (Sfx.VoContactConfirm, "Contact, confirmed!"),
            [Callout.Copy] = (Sfx.VoCopy, "Copy!"),
            [Callout.Reloading] = (Sfx.VoCover, "Cover!"),
            [Callout.CoverMe] = (Sfx.VoCover, "Cover!"),
            [Callout.OutOfAmmo] = (Sfx.VoCover, "Cover! I'm out!"),
            [Callout.Engaging] = (Sfx.VoEngaging, "Engaging!"),
            [Callout.Moving] = (Sfx.VoChatter, null),
            [Callout.Flanking] = (Sfx.VoChatter, null),
            [Callout.ThereHeIs] = (Sfx.VoChatter, null),
            [Callout.LostHim] = (Sfx.VoChatter, null),
            [Callout.Suppressing] = (Sfx.VoChatter, null),
        };

        /// <summary>When someone last called contact or a position, per squad (for "contact confirmed").</summary>
        public static readonly Dictionary<Squad, float> LastContactCall = new();

        /// <summary>Speak a callout. Returns false if it was not said (cooldown, discipline, or chance).</summary>
        public static bool Say(Operative op, Callout c, Vector3? where = null, string arg0 = null, string arg1 = null, Operative addressee = null, bool force = false)
        {
            if (op == null || op.IsDead) return false;
            if (op.Surrendered && c != Callout.Surrender) return false;
            var f = op.Faction;
            if (!force)
            {
                if (Time.time < op.NextCalloutTime) return false;
                if (Random.value > (f != null ? f.calloutChance : 0.8f)) return false;
                if (op.Squad != null && squadCooldown.TryGetValue((op.Squad, c), out var until) && Time.time < until) return false;
                // A stalker does not give themselves away
                if (op.Mind.Current == OpAction.Stalk && c != Callout.ManDown) return false;
            }
            op.NextCalloutTime = Time.time + Random.Range(1.6f, 3f);
            if (op.Squad != null) squadCooldown[(op.Squad, c)] = Time.time + (c == Callout.ThereHeIs || c == Callout.Contact ? 3.5f : 5f);

            bool radio = f != null && f.radio;
            var lines = (radio ? Radioed : Shouted)[c];
            string dir = where.HasValue ? Direction(op, where.Value) : "ahead";
            string line = string.Format(lines[Random.Range(0, lines.Length)], arg0 ?? dir, arg1 ?? dir);

            // Voice: a real recording if there is one for this callout, else the synthesized bark
            var mouth = op.EyePosition;
            float shoutRadius = f != null ? f.shoutRadius : 40f;
            if (Recorded.TryGetValue(c, out var rec) && SoundManager.HasOverride(rec.voice))
            {
                if (rec.subtitle != null) line = rec.subtitle;
                // A gentle per-speaker pitch keeps one recording from sounding like the same man every time
                float pitch = Mathf.Lerp(0.94f, 1.06f, Mathf.InverseLerp(0.86f, 1.14f, op.VoicePitch));
                var clips = SoundManager.ClipCount(rec.voice);
                SoundManager.PlayVoiceAt(rec.voice, Random.Range(0, Mathf.Max(1, clips)), mouth, radio ? 0.45f : 1f, pitch);
            }
            // No recording for this callout: no sound (the synthesized bark was removed). It still shows as a subtitle,
            // and a shout still carries to listeners in earshot.
            if ((c == Callout.Contact || c == Callout.ThereHeIs) && op.Squad != null) LastContactCall[op.Squad] = Time.time;
            if (!radio) Hearing.Emit(mouth, shoutRadius, NoiseKind.Voice, op.gameObject);
            op.LastCallout = line;
            op.LastCalloutTime = Time.time;

            // Can the player hear it?
            var player = PlayerTarget.Instance;
            bool audible = false;
            if (player != null)
            {
                var e = new NoiseEvent { Position = mouth, Radius = radio ? 7f : shoutRadius, Kind = NoiseKind.Voice, Source = op.gameObject };
                audible = Hearing.Loudness(e, player.HeadPosition) > 0.08f;
            }
            if (audible) Heard?.Invoke(op, line, radio);
            Log.Add((Time.time, $"{(radio ? "<color=#8fd3ff>R</color>" : "<color=#ffb27a>S</color>")} {(audible ? "" : "<color=#777>(unheard)</color> ")}{op.Callsign}: {line}"));
            if (Log.Count > 10) Log.RemoveAt(0);

            // Deliver to listeners
            var b = op.Perception.Belief;
            foreach (var other in Operative.All)
            {
                if (other == op || other.IsDead) continue;
                bool sameSquad = other.Squad != null && other.Squad == op.Squad;
                float delay;
                if (radio)
                {
                    // One radio net for the whole faction: other squads hear it too, a bit later
                    if (other.Faction != f) continue;
                    delay = f.radioDelay * (sameSquad ? 1f : 1.6f);
                }
                else
                {
                    var e = new NoiseEvent { Position = mouth, Radius = shoutRadius, Kind = NoiseKind.Voice, Source = op.gameObject };
                    if (Hearing.Loudness(e, other.EyePosition) < 0.06f) continue;
                    delay = Random.Range(0.2f, 0.5f);   // hearing it and understanding it
                }
                other.QueueCallout(new HeardCallout { From = op, Kind = c, At = Time.time + delay, Belief = b, Addressee = addressee });
            }
            return true;
        }

        /// <summary>Relative direction words from the speaker's point of view.</summary>
        static string Direction(Operative op, Vector3 where)
        {
            var to = (where - op.transform.position).Flat();
            if (where.y - op.transform.position.y > 2.5f && to.magnitude < 30f) return "up high";
            float ang = Vector3.SignedAngle(op.transform.forward.Flat(), to, Vector3.up);
            if (Mathf.Abs(ang) < 35f) return to.magnitude < 12f ? "close, front" : "front";
            if (Mathf.Abs(ang) > 140f) return "behind us";
            return ang > 0f ? "right" : "left";
        }
    }

    public struct HeardCallout
    {
        public Operative From;
        public Callout Kind;
        public float At;
        public Belief Belief;
        public Operative Addressee;
    }
}
