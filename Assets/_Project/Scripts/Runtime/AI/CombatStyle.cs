using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Alcax.AI
{
    /// <summary>
    /// A personal way of fighting, on top of traits. Two riflemen with the same numbers still differ: one likes to
    /// hold an angle and wait for you (Ambusher), another to creep around you (Stalker). Each unit rolls a primary
    /// style and a weaker secondary from the faction and role weights.
    /// </summary>
    public enum CombatStyle { Ambusher, Stalker, Brawler, Suppressor, Survivor, Opportunist, Flanker }

    [Serializable]
    public struct StyleWeights
    {
        public float ambusher, stalker, brawler, suppressor, survivor, opportunist, flanker;

        public static StyleWeights Even => new StyleWeights { ambusher = 1, stalker = 1, brawler = 1, suppressor = 1, survivor = 1, opportunist = 1, flanker = 1 };

        public float this[CombatStyle s] => s switch
        {
            CombatStyle.Ambusher => ambusher, CombatStyle.Stalker => stalker, CombatStyle.Brawler => brawler,
            CombatStyle.Suppressor => suppressor, CombatStyle.Survivor => survivor, CombatStyle.Opportunist => opportunist,
            _ => flanker,
        };

        public static StyleWeights operator *(StyleWeights a, StyleWeights b) => new StyleWeights
        {
            ambusher = a.ambusher * b.ambusher, stalker = a.stalker * b.stalker, brawler = a.brawler * b.brawler,
            suppressor = a.suppressor * b.suppressor, survivor = a.survivor * b.survivor,
            opportunist = a.opportunist * b.opportunist, flanker = a.flanker * b.flanker,
        };

        public CombatStyle Roll(CombatStyle? exclude = null)
        {
            float total = 0f;
            var all = (CombatStyle[])Enum.GetValues(typeof(CombatStyle));
            foreach (var s in all) if (s != exclude) total += Mathf.Max(0f, this[s]);
            float pick = Random.value * total;
            foreach (var s in all)
            {
                if (s == exclude) continue;
                pick -= Mathf.Max(0f, this[s]);
                if (pick <= 0f) return s;
            }
            return all[0];
        }
    }

    public static class StyleBias
    {
        /// <summary>Additive score bias a style gives an action (the primary at full strength, the secondary at 40%).</summary>
        public static float For(CombatStyle s, OpAction a) => (s, a) switch
        {
            (CombatStyle.Ambusher, OpAction.HoldCover) => 0.28f,
            (CombatStyle.Ambusher, OpAction.PeekShoot) => 0.05f,
            (CombatStyle.Ambusher, OpAction.Advance) => -0.15f,
            (CombatStyle.Ambusher, OpAction.Rush) => -0.2f,
            (CombatStyle.Ambusher, OpAction.Search) => -0.12f,

            (CombatStyle.Stalker, OpAction.Stalk) => 0.38f,
            (CombatStyle.Stalker, OpAction.Flank) => 0.15f,
            (CombatStyle.Stalker, OpAction.Engage) => -0.1f,
            (CombatStyle.Stalker, OpAction.Search) => 0.08f,

            (CombatStyle.Brawler, OpAction.Rush) => 0.3f,
            (CombatStyle.Brawler, OpAction.Engage) => 0.15f,
            (CombatStyle.Brawler, OpAction.TakeCover) => -0.15f,
            (CombatStyle.Brawler, OpAction.HoldCover) => -0.2f,

            (CombatStyle.Suppressor, OpAction.PeekShoot) => 0.2f,
            (CombatStyle.Suppressor, OpAction.Engage) => 0.05f,
            (CombatStyle.Suppressor, OpAction.Stalk) => -0.2f,

            (CombatStyle.Survivor, OpAction.TakeCover) => 0.15f,
            (CombatStyle.Survivor, OpAction.Retreat) => 0.15f,
            (CombatStyle.Survivor, OpAction.PatchUp) => 0.3f,
            (CombatStyle.Survivor, OpAction.Rush) => -0.25f,

            (CombatStyle.Opportunist, OpAction.Search) => 0.05f,

            (CombatStyle.Flanker, OpAction.Flank) => 0.32f,
            (CombatStyle.Flanker, OpAction.ClearRoom) => 0.1f,
            (CombatStyle.Brawler, OpAction.ClearRoom) => 0.12f,
            (CombatStyle.Ambusher, OpAction.ClearRoom) => -0.15f,
            (CombatStyle.Flanker, OpAction.Advance) => 0.05f,
            (CombatStyle.Flanker, OpAction.HoldCover) => -0.1f,
            _ => 0f,
        };
    }
}
