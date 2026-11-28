using System.Collections.Generic;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Budgets per asset class for imported art. The class is the folder under Art/Models
    /// (Assets/_Project/Art/Models/&lt;Class&gt;/&lt;Asset&gt;/). See docs/art/asset-pipeline.md.
    /// </summary>
    public static class AssetBudgets
    {
        public enum Rig { None, Humanoid, Generic }

        public class Budget
        {
            public string name;
            public int maxTris;        // LOD0 triangles
            public int lodFromTris;    // above this, LOD1+ meshes are expected
            public int maxTexture;     // longest side, pixels
            public float maxSize;      // largest bounding box side, meters (scale sanity)
            public float minSize;
            public Rig rig;
            public bool lightmapUVs;   // static, baked: generate secondary UVs
            public string prefix;      // SM_ static mesh, SK_ skinned
            public bool originAtBase = true;   // false for weapons: their origin is the grip
        }

        static readonly Dictionary<string, Budget> classes = new Dictionary<string, Budget>
        {
            { "Props",        new Budget { name = "Props",        maxTris = 10000, lodFromTris = 2000, maxTexture = 2048, maxSize = 4f,   minSize = 0.02f, rig = Rig.None,     lightmapUVs = false, prefix = "SM_" } },
            { "Weapons",      new Budget { name = "Weapons",      maxTris = 30000, lodFromTris = 8000, maxTexture = 4096, maxSize = 2.5f, minSize = 0.05f, rig = Rig.Generic,  lightmapUVs = false, prefix = "SM_", originAtBase = false } },
            { "Architecture", new Budget { name = "Architecture", maxTris = 8000,  lodFromTris = 3000, maxTexture = 2048, maxSize = 30f,  minSize = 0.05f, rig = Rig.None,     lightmapUVs = true,  prefix = "SM_" } },
            { "Environment",  new Budget { name = "Environment",  maxTris = 20000, lodFromTris = 4000, maxTexture = 4096, maxSize = 40f,  minSize = 0.1f,  rig = Rig.None,     lightmapUVs = true,  prefix = "SM_" } },
            { "Vehicles",     new Budget { name = "Vehicles",     maxTris = 60000, lodFromTris = 10000,maxTexture = 4096, maxSize = 80f,  minSize = 0.5f,  rig = Rig.None,     lightmapUVs = false, prefix = "SM_" } },
            { "Characters",   new Budget { name = "Characters",   maxTris = 60000, lodFromTris = 15000,maxTexture = 4096, maxSize = 2.6f, minSize = 0.8f,  rig = Rig.Humanoid, lightmapUVs = false, prefix = "SK_" } },
            { "Creatures",    new Budget { name = "Creatures",    maxTris = 60000, lodFromTris = 15000,maxTexture = 4096, maxSize = 12f,  minSize = 0.2f,  rig = Rig.Generic,  lightmapUVs = false, prefix = "SK_" } },
            { "Foliage",      new Budget { name = "Foliage",      maxTris = 6000,  lodFromTris = 1500, maxTexture = 2048, maxSize = 15f,  minSize = 0.05f, rig = Rig.None,     lightmapUVs = false, prefix = "SM_" } },
        };

        public const string ModelsRoot = "Assets/_Project/Art/Models/";

        /// <summary>The class and asset name from a path under Art/Models, or null outside it.</summary>
        public static Budget For(string assetPath, out string assetName)
        {
            assetName = null;
            if (!assetPath.StartsWith(ModelsRoot)) return null;
            var parts = assetPath.Substring(ModelsRoot.Length).Split('/');
            if (parts.Length < 3) return null;   // needs <Class>/<Asset>/<file>
            assetName = parts[1];
            return classes.TryGetValue(parts[0], out var b) ? b : null;
        }

        public static IEnumerable<string> ClassNames => classes.Keys;
    }
}
