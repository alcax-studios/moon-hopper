using UnityEngine;

namespace Alcax.AI
{
    /// <summary>Global combat-AI tuning. Editable live in Play mode (Assets/_Project/Data/AI/CombatAIConfig).</summary>
    [CreateAssetMenu(menuName = "Alcax/AI/Combat AI Config", fileName = "CombatAIConfig")]
    public class CombatAIConfig : ScriptableObject
    {
        [Header("Vision")]
        public float visionRange = 70f;
        [Tooltip("Full cone of sharp (focal) vision, in degrees.")] public float focalAngle = 40f;
        [Tooltip("Full cone of peripheral vision, in degrees.")] public float peripheralAngle = 150f;
        [Tooltip("Awareness gained per second when the player is fully visible in focal vision at close range.")] public float focalGain = 2.2f;
        public float peripheralGain = 0.55f;
        [Range(0f, 1f)] public float crouchVisibility = 0.55f;
        [Range(0f, 1f)] public float stillVisibility = 0.7f;
        public float firingVisibility = 2.5f;
        public float flashlightVisibility = 1.5f;

        [Header("Awareness")]
        public float awarenessDecay = 0.08f;
        [Tooltip("Decay while in combat is slower (they remember).")] public float combatAwarenessDecay = 0.025f;
        [Tooltip("Decay while alert (heard something worth checking): slow, they keep looking.")] public float alertAwarenessDecay = 0.006f;
        [Tooltip("Judge contact freshness from the best information (own sight, squad reports, noise), add re-acquire peeks and restlessness in cover.")] public bool useInfoEngagement = true;
        [Tooltip("Fighting cover only (no peek-proof hiding unless retreating or dry), commit to a move until arrival, scared units blind-fire or info-peek instead of freezing.")] public bool useFightingCover = true;
        [Tooltip("Movement doctrine: per-move dash / stop-and-shoot / fire-on-move by faction and role cover discipline, plus stamina for sprinting.")] public bool useMoveDoctrine = true;
        [Tooltip("Contact drill: right after contact or while shot at in the open, return fire or reaction-fire toward the shots and dash for cover; never stalk, search, or crouch-walk under fire. Also the fire fixes (rush and blown flanks shoot, long dashes in view plant and shoot, sidestep a squadmate in the line).")] public bool useContactDrill = true;
        [Tooltip("Hit response: a hit while fighting or peeking is a choice between pushing through and ducking (range, nerve, confidence, health, and how often they already ducked), instead of always ducking.")] public bool useHitCommit = true;
        [Tooltip("Space awareness: each NavGraph node knows if it is indoors and how big the space is; standoff, fighting range, cover and flank search radii shrink to fit rooms and corridors.")] public bool useSpaceAwareness = true;
        [Tooltip("Room graph (layer 2): authored rooms and doors; when he's in another room and unseen, hold the doorway toward him.")] public bool useRoomGraph = true;
        [Tooltip("CQB (layer 3): clear rooms through doorways (stack, pie the corner, enter, dominate), split entries across doors, search room by room.")] public bool useCqb = true;
        [Tooltip("Aim from the space: unseen aim follows the walking route to where he probably is and holds the point where it leaves view (a corner, the hall's end, a doorway, the stairs), not a bearing through walls. Attention favors that direction; cues behind walls fade; room-clearing sweeps cover visible doors and corners (a corridor's two ends).")] public bool useSpaceAim = true;
        [Tooltip("Steady moves: shoot on the move when he's in view (sprint gun-low only when unseen); no backward cover runs unless retreating; no open-ground advance while he's in view (except loose aggressive units); unseen, face the route ahead instead of backpedaling.")] public bool useSteadyMoves = true;
        [Tooltip("Counter-peek: hold the exact spot he was last seen, react fast when he reappears there (anticipated), and deny a repeated peek spot with short bursts.")] public bool useCounterPeek = true;
        [Tooltip("Hit-stun limit: only the first hit of a volley breaks the burst and pauses fire; follow-up hits within 0.8 s only flinch (and less each time), so sustained fire can't lock a target out of shooting back.")] public bool useHitStunLimit = true;
        [Tooltip("Peek prefire: learn his hide time between peeks from one spot and fire on the spot just before he's due back; one squad holder per spot, the rest hold their aim (Suppressors may add slow fire).")] public bool usePeekPrefire = true;
        public float suspiciousAt = 0.2f;
        public float alertAt = 0.55f;
        public float combatAt = 0.85f;

        [Header("Belief (memory of the player)")]
        [Tooltip("How fast the uncertainty about the player's position grows while unseen (m/s).")] public float uncertaintyGrowth = 1.4f;
        [Tooltip("Seconds unseen before an NPC treats the player as lost (starts searching).")] public float lostAfter = 7f;

        [Header("Decision making")]
        public float decisionInterval = 0.3f;
        [Tooltip("Per-action score noise, resampled every noiseResample seconds (anti-predictability).")] [Range(0f, 0.3f)] public float scoreNoise = 0.09f;
        public float noiseResample = 3.5f;
        [Tooltip("A new action must beat the current one by this margin (decays the longer the current action runs).")] public float commitMargin = 0.18f;
        public float commitDecaySeconds = 6f;

        [Header("Squad")]
        [Tooltip("Radio delay before allies learn what one member saw.")] public float shareDelay = 0.7f;
        [Tooltip("Error (m) added to shared positions.")] public float shareError = 1.5f;

        [Header("Spatial reasoning (switches for A/B testing against the earlier behavior)")]
        [Tooltip("Gaze picks where to look (likely spots, doorways, unchecked ground) instead of looking ahead and sweeping.")]
        public bool useAttention = true;
        [Tooltip("Keep a standoff distance from the threat and from where it probably is (vantage points, no walking into it).")]
        public bool useStandoff = true;
        [Tooltip("Search by the squad's occupancy map instead of random points around the last known position.")]
        public bool useOccupancySearch = true;
        [Tooltip("While hidden or holding an angle, keep the muzzle on where he most likely appears from the peek spot.")]
        public bool usePreAim = true;
        [Tooltip("Merge squad reports into one belief weighted by confidence (inverse-variance fusion) instead of the newest report replacing it.")]
        public bool fuseReports = true;
        [Tooltip("Weigh reports by trust in the speaker (credibility, listener disposition, and learned track record).")]
        public bool useTrust = true;
        [Tooltip("Brain-like filters: habituation to repeated stimuli, stress narrowing sight and hearing, and limited capacity under load.")]
        public bool useCognitiveFilters = true;
        [Tooltip("Cover repertoire: info peeks, blind fire, leans, and full step-outs (off = full step-out only).")]
        public bool useCoverRepertoire = true;
        [Tooltip("Finite spare ammo per operative (off = unlimited magazines).")]
        public bool useAmmoBudget = true;
        [Tooltip("Assumed player speed for spreading the occupancy map while unseen (m/s).")]
        public float occupancySpread = 3f;

        [Header("Cover")]
        public float coverSearchRadius = 18f;
        public float allySpacing = 2.6f;
    }
}
