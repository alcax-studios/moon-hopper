using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// The shared effect materials (Resources/FXLibrary, built by Alcax > Rebuild FX Assets): additive glow, stretched
    /// sparks, lit smoke flipbook, scorch decal, and electric arcs. Follows the art bible's effect rules.
    /// </summary>
    [CreateAssetMenu(menuName = "Alcax/FX Library", fileName = "FXLibrary")]
    public class FXLibrary : ScriptableObject
    {
        public Material glow;        // soft round additive sprite
        public Material spark;       // streak, for stretched billboards
        public Material smoke;       // lit flipbook
        public int smokeTilesX = 8, smokeTilesY = 8;
        public Material decal;       // scorch quad (MaterialPropertyBlock: _Heat, _Fade)
        public Material arc;         // electric arcs and bolt cores (line renderers)
        public Material sandGrain;   // lit soft dot for blowing sand grains
        public Material sandVeil;    // lit smoke flipbook for sand veils, plumes, and dust devils

        static FXLibrary cached;
        public static FXLibrary Get()
        {
            if (cached == null) cached = Resources.Load<FXLibrary>("FXLibrary");
            return cached;
        }
    }
}
