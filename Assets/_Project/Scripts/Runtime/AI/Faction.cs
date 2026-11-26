using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Who trained them. A faction shifts every role's traits (special operators are calmer, sharper, and more
    /// coordinated than insurgents), sets how much individuals vary, how they talk (quiet radio vs shouting),
    /// and which fighting styles their people tend toward. Layering: faction > role (archetype) > individual.
    /// </summary>
    [CreateAssetMenu(menuName = "Alcax/AI/Faction", fileName = "FAC_")]
    public class Faction : ScriptableObject
    {
        public string displayName = "Insurgents";
        public Color jacket = Color.gray;
        public Color gear = Color.gray;
        [Tooltip("Insurgents wear mismatched clothes: each unit picks one of these instead of the jacket color.")]
        public Color[] jacketVariety;

        [Header("Training: added to the role's traits")]
        [Range(-0.5f, 0.5f)] public float aggression, caution, discipline, patience, teamwork, skill, nerve;
        public float reactionScale = 1f;
        [Tooltip("How much individuals differ from each other (multiplies the role's trait jitter).")]
        public float jitterScale = 1f;

        [Header("Comms")]
        [Tooltip("Radio: squad-wide, quiet, a short delay. Otherwise they shout: only those in earshot hear it, including you.")]
        public bool radio;
        [Range(0f, 1f)] public float calloutChance = 0.85f;
        public float shoutRadius = 40f;
        public float radioDelay = 0.45f;

        [Header("Fighting-style tendencies (relative weights)")]
        public StyleWeights styles = StyleWeights.Even;

        [Header("Morale")]
        [Tooltip("How much the squad's mood swings each member (insurgents ride momentum, operators stay level).")]
        public float volatility = 1f;
        [Tooltip("Added resistance to breaking (running away) when confidence collapses.")]
        [Range(0f, 0.6f)] public float breakResistance = 0f;
        [Tooltip("Broken, cornered members may give up instead of running.")]
        public bool canSurrender = true;

        [Header("Doctrine")]
        [Tooltip("How quickly they learn from what works and what gets them hurt during a fight.")]
        [Range(0f, 1f)] public float adaptability = 0.5f;
        [Tooltip("Plan routes that stay out of your line of sight when flanking, stalking, or falling back.")]
public bool concealedRoutes = true;
        [Tooltip("Factions this one fights on sight (hostility is mutual: either side listing the other is enough).")]
        public Faction[] hostileTo;

        public static bool Hostile(Faction a, Faction b)
        {
            if (a == null || b == null || a == b) return false;
            if (a.hostileTo != null) foreach (var f in a.hostileTo) if (f == b) return true;
            if (b.hostileTo != null) foreach (var f in b.hostileTo) if (f == a) return true;
            return false;
        }
        [Tooltip("0 = walk and shoot, cover when it makes sense; 1 = shoot from cover, bound cover to cover under overwatch.")]
        [Range(0f, 1f)] public float coverDiscipline = 0.5f;
        [Range(0f, 1f)] public float patchUpChance = 0.7f;
        [Tooltip("Squad leaders give orders (push, fall back, flank).")]
        public bool leaderOrders = true;
    }
}
