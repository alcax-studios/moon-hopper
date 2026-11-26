using UnityEngine;

namespace Alcax.Core
{
    /// <summary>
    /// The single source of every color in the game (see docs/design/art-bible.md).
    /// Nothing else in the project should construct literal colors.
    /// </summary>
    public static class Palette
    {
        static Color H(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        // Environment
        public static readonly Color Ground = H("#3B4A32");
        public static readonly Color GroundDark = H("#2C3826");
        public static readonly Color Moss = H("#4F6B3C");
        public static readonly Color Pine = H("#2E5540");
        public static readonly Color PineLight = H("#46704F");
        public static readonly Color Bark = H("#4A3527");
        public static readonly Color DeadWood = H("#5E5246");
        public static readonly Color Stone = H("#646B70");
        public static readonly Color StoneDark = H("#4A5055");
        public static readonly Color CabinWood = H("#6A4E36");
        public static readonly Color CabinDark = H("#3E2E22");
        public static readonly Color Roof = H("#45404A");
        public static readonly Color Canvas = H("#6B6A4E");


        // Player and weapons
        public static readonly Color PlayerSkin = H("#C99A74");
        public static readonly Color PlayerSleeve = H("#3D4F63");
        public static readonly Color Metal = H("#8A96A3");
        public static readonly Color MetalDark = H("#3C4148");
        public static readonly Color WoodLight = H("#9A7450");
        public static readonly Color Grip = H("#2A2A30");
        public static readonly Color Brass = H("#C9A24A");
        public static readonly Color Medkit = H("#B5463C");

        // Blood
        public static readonly Color Blood = H("#7A1F2B");
        public static readonly Color BloodBright = H("#A3283A");

        // Sky, light and atmosphere
        public static readonly Color SkyTop = H("#2B3A55");
        public static readonly Color Horizon = H("#C9846A");
        public static readonly Color Fog = H("#4E5868");
        public static readonly Color Shadow = H("#1E2633");
        public static readonly Color KeyLight = H("#FFC9A0");
        public static readonly Color Flashlight = H("#FFE6C0");
        public static readonly Color AmbientSky = H("#50648A");
        public static readonly Color AmbientEquator = H("#3E4552");
        public static readonly Color AmbientGround = H("#2A2622");
        public static readonly Color Spores = H("#A8C0B0");

        // Accent (loot, fire, lanterns)
        public static readonly Color Accent = H("#F2A23A");
        public static readonly Color AccentHot = H("#FFD27A");
        public static readonly Color Fire = H("#E8642A");

        // UI
        public static readonly Color UIBone = H("#E8E1D0");
        public static readonly Color UIDark = H("#141821");
        public static readonly Color UIPanel = H("#1E2633");
        public static readonly Color UIPanelLight = H("#2E3A4D");
        public static readonly Color UIHealth = H("#B5463C");
        public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        // ---- Station (atrium map) ----
        // Interior: bright, dry, matte concrete and light tech panels, cyan panel light, orange accents.
        // Exterior: warm desert sunset under a huge pale ringed gas giant.
        public static readonly Color StConcrete = H("#8E8B86");
        public static readonly Color StConcreteDark = H("#B4B1AB");
        public static readonly Color StConcreteFloor = H("#C9C5BD");
        public static readonly Color StMetal = H("#B7BCC2");
        public static readonly Color StMetalLight = H("#E4E6E9");
        public static readonly Color StTechCyan = H("#6FE3FF");
        public static readonly Color StPanelLight = H("#F2F6FA");
        public static readonly Color StFloorDark = H("#1D2329");
        public static readonly Color StAccentOrange = H("#FF6A2B");
        public static readonly Color StConcreteTint = H("#E2E0DC");
        public static readonly Color StFabric = H("#CFCBC3");
        public static readonly Color StSteam = H("#C9DCE6");
        public static readonly Color StWarmLamp = H("#FFC98A");
        public static readonly Color StHazard = H("#E0A526");
        public static readonly Color StGlass = H("#9CC3CF");
        public static readonly Color StSun = H("#FFD9B0");
        public static readonly Color StSand = H("#C98A4F");
        public static readonly Color StSandShadow = H("#8C5636");
        public static readonly Color StRock = H("#6E3F2C");
        public static readonly Color StSkyZenith = H("#3E5F82");
        public static readonly Color StSkyHaze = H("#E7B88E");
        public static readonly Color StPlanetA = H("#D8A47C");
        public static readonly Color StPlanetB = H("#9C6A55");
        public static readonly Color StRing = H("#D9C3A5");
        public static readonly Color StFoliage = H("#3E6E3F");
        public static readonly Color StFoliageLight = H("#76A85A");
        public static readonly Color StFoliageDark = H("#24452B");
        public static readonly Color StBark = H("#6B5A49");
        public static readonly Color StSoil = H("#3A2C22");
        public static readonly Color StWater = H("#1C2A30");
        public static readonly Color StWood = H("#8A6A4B");
        public static readonly Color StPilotOlive = H("#5E6B4A");
        public static readonly Color StVisitorTeal = H("#3E7C80");

        // Character skin and hair tones
        public static readonly Color SkinLight = H("#E3BC9A");
        public static readonly Color SkinMid = H("#C99A74");
        public static readonly Color SkinDark = H("#8D5A3B");
        public static readonly Color SkinDeep = H("#5C3A25");
        public static readonly Color HairBrown = H("#4B3426");
        public static readonly Color HairBlonde = H("#C9A86A");
        public static readonly Color HairGrey = H("#9A9A9A");

        // Combat operatives (muted field kit so they read against sand and concrete but don't glow)
        public static readonly Color OpOlive = H("#55593F");
        public static readonly Color OpCharcoal = H("#34373B");
        public static readonly Color OpTan = H("#9C8762");
        public static readonly Color OpBlack = H("#202224");
        public static readonly Color OpSlate = H("#5D6873");
        public static readonly Color OpPlateTan = H("#857252");
        public static readonly Color OpPlateOlive = H("#474C36");
        public static readonly Color OpPlateBlack = H("#2A2C2E");
        public static readonly Color OpGunmetal = H("#2C2E31");
        public static readonly Color OpPolymer = H("#3B3C3E");

        // Factions
        public static readonly Color FacInsurgentA = H("#6B5B45");
        public static readonly Color FacInsurgentB = H("#4F5A5E");
        public static readonly Color FacInsurgentC = H("#7A6A55");
        public static readonly Color FacInsurgentD = H("#3F4A3A");
        public static readonly Color FacInsurgentE = H("#5C4A3D");
        public static readonly Color FacInsurgentGear = H("#8A7A5E");
        public static readonly Color FacSpecOps = H("#2A2D31");
        public static readonly Color FacSpecOpsGear = H("#3B3F38");

        // AI playground (dev map): measured grid surfaces in neutral tones
        public static readonly Color PgGround = H("#C7B596");
        public static readonly Color PgHill = H("#B89E78");
        public static readonly Color PgConcrete = H("#BDBAB4");
        public static readonly Color PgCrate = H("#9A5A34");
        public static readonly Color PgCrateLow = H("#7C8389");
        public static readonly Color PgSteel = H("#8E959C");
        public static readonly Color PgGridLine = H("#6E6A62");

        /// <summary>Neutral multiplier (white). Use only where a texture or tint system carries the real color.</summary>
        public static readonly Color Neutral = new Color(1f, 1f, 1f, 1f);
    }
}
