using System.Collections.Generic;
using Alcax.Core;
using Alcax.Items;
using UnityEngine;

namespace Alcax.UI
{
    /// <summary>
    /// A hidden photo studio far below the map (layer 30, outside every camera's reach): renders item icons from their
    /// world models, and the character preview, a mannequin dressed in whatever the player wears. Made on first use and
    /// kept across maps.
    /// </summary>
    public class PreviewStudio : MonoBehaviour
    {
        public const int Layer = 30;
        static readonly Vector3 Origin = new Vector3(0f, -4000f, 0f);
        static readonly Vector3 IconSpot = new Vector3(20f, 0f, 0f);

        static PreviewStudio instance;
        public static PreviewStudio Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("PreviewStudio");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<PreviewStudio>();
                    instance.Build();
                }
                return instance;
            }
        }

        Camera iconCam, dollCam;
        RenderTexture dollRT;
        readonly Dictionary<ItemDefinition, RenderTexture> icons = new Dictionary<ItemDefinition, RenderTexture>();
        Transform doll, dollSpin;
        readonly Dictionary<string, Renderer> parts = new Dictionary<string, Renderer>();
        readonly Dictionary<WearStyle, GameObject> overlays = new Dictionary<WearStyle, GameObject>();
        public float Yaw = 160f;

        public RenderTexture DollTexture => dollRT;

        void Build()
        {
            transform.position = Origin;
            foreach (var (pos, intensity, color) in new[] { (new Vector3(2.5f, 3f, 3f), 18f, new Color(1f, 0.92f, 0.82f)), (new Vector3(-3f, 2f, 1.5f), 9f, new Color(0.7f, 0.8f, 1f)), (new Vector3(0f, 2.5f, -3f), 10f, Color.white) })
            {
                foreach (var spot in new[] { Vector3.zero, IconSpot })
                {
                    var l = new GameObject("Light").AddComponent<Light>();
                    l.transform.SetParent(transform, false);
                    l.transform.localPosition = spot + pos;
                    l.type = LightType.Point;
                    l.range = 12f; l.intensity = intensity; l.color = color;
                    l.shadows = LightShadows.None;
                }
            }

            iconCam = MakeCamera("IconCamera", 24f);
            dollCam = MakeCamera("DollCamera", 21f);
            dollRT = new RenderTexture(560, 860, 24, RenderTextureFormat.ARGB32) { name = "DollRT", antiAliasing = 4 };
            dollCam.targetTexture = dollRT;
            dollCam.transform.localPosition = new Vector3(0f, 1.0f, 5.4f);
            dollCam.transform.LookAt(transform.position + new Vector3(0f, 0.92f, 0f));
            BuildMannequin();
        }

        Camera MakeCamera(string name, float fov)
        {
            var cam = new GameObject(name).AddComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f;
            cam.enabled = false;
            cam.allowHDR = false;
            return cam;
        }

        // ---------- Icons ----------

        /// <summary>The item's icon (rendered once from its world model, then cached).</summary>
        public Texture Icon(ItemDefinition def)
        {
            if (def == null) return null;
            if (icons.TryGetValue(def, out var rt) && rt != null) return rt;
            rt = new RenderTexture(160, 160, 16, RenderTextureFormat.ARGB32) { name = "Icon_" + def.id, antiAliasing = 4 };
            icons[def] = rt;
            if (def.worldModel == null) return rt;
            var m = Instantiate(def.worldModel, transform);
            m.transform.localPosition = IconSpot;
            m.transform.localRotation = Quaternion.Euler(0f, def.category == ItemCategory.Weapon ? 90f : 25f, 0f);
            SetLayer(m, Layer);
            NoProbes(m);
            var rs = m.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float size = b.extents.magnitude;
                var dir = new Vector3(0.55f, 0.6f, 1f).normalized;
                float dist = size / Mathf.Sin(iconCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                iconCam.transform.position = b.center + dir * dist;
                iconCam.transform.LookAt(b.center);
            }
            iconCam.targetTexture = rt;
            iconCam.Render();
            iconCam.targetTexture = null;
            DestroyImmediate(m);
            return rt;
        }

        // ---------- The character preview ----------

        /// <summary>Render the mannequin this frame (the inventory calls this while it's open).</summary>
        public void RenderDoll()
        {
            if (dollSpin != null) dollSpin.localRotation = Quaternion.Euler(0f, Yaw, 0f);
            dollCam.Render();
        }

        /// <summary>Dress the mannequin in what the player wears.</summary>
        public void Dress(PlayerInventory inv)
        {
            var skin = Palette.PlayerSkin;
            var under = new Color(0.55f, 0.56f, 0.58f);
            var shorts = new Color(0.22f, 0.24f, 0.28f);
            ItemDefinition W(EquipSlot s) => inv != null ? inv.Worn(s)?.def : null;
            var head = W(EquipSlot.Head); var chest = W(EquipSlot.Chest); var pants = W(EquipSlot.Pants); var shoes = W(EquipSlot.Shoes); var gloves = W(EquipSlot.Gloves);

            Tint("Head", skin); Tint("Neck", skin);
            bool jacket = chest != null && chest.wearStyle == WearStyle.Jacket;
            Tint("Torso", chest != null ? (jacket ? chest.wearColor : under) : under);
            foreach (var p in new[] { "UpperArmL", "UpperArmR" }) Tint(p, jacket ? chest.wearColor : chest != null ? under : skin);
            foreach (var p in new[] { "LowerArmL", "LowerArmR" }) Tint(p, jacket ? chest.wearColor : skin);
            foreach (var p in new[] { "HandL", "HandR" }) Tint(p, gloves != null ? gloves.wearColor : skin);
            Tint("Hips", pants != null ? pants.wearColor : shorts);
            foreach (var p in new[] { "ThighL", "ThighR" }) Tint(p, pants != null ? pants.wearColor : shorts);
            foreach (var p in new[] { "ShinL", "ShinR" }) Tint(p, pants != null ? pants.wearColor : skin);
            foreach (var p in new[] { "FootL", "FootR" })
            {
                Tint(p, shoes != null ? shoes.wearColor : new Color(0.75f, 0.74f, 0.7f));
                if (parts.TryGetValue(p, out var r)) r.transform.localScale = shoes != null ? new Vector3(0.14f, 0.11f, 0.29f) : new Vector3(0.11f, 0.06f, 0.24f);
            }

            foreach (var kv in overlays) kv.Value.SetActive(false);
            void On(ItemDefinition d)
            {
                if (d == null || !overlays.TryGetValue(d.wearStyle, out var o)) return;
                o.SetActive(true);
                foreach (var r in o.GetComponentsInChildren<Renderer>()) if (r.name != "Visor" && r.name != "Accent") SetColor(r, d.wearColor);
            }
            On(head);
            if (chest != null && chest.wearStyle == WearStyle.Vest) On(chest);
        }

        void BuildMannequin()
        {
            doll = new GameObject("Mannequin").transform;
            doll.SetParent(transform, false);
            dollSpin = new GameObject("Spin").transform;
            dollSpin.SetParent(doll, false);
            var t = dollSpin;
            Part("Head", PrimitiveType.Sphere, t, new Vector3(0, 1.67f, 0), new Vector3(0.22f, 0.25f, 0.23f));
            Part("Neck", PrimitiveType.Cylinder, t, new Vector3(0, 1.52f, 0), new Vector3(0.09f, 0.05f, 0.09f));
            Part("Torso", PrimitiveType.Cube, t, new Vector3(0, 1.24f, 0), new Vector3(0.38f, 0.48f, 0.21f));
            Part("Hips", PrimitiveType.Cube, t, new Vector3(0, 0.95f, 0), new Vector3(0.34f, 0.16f, 0.2f));
            foreach (int side in new[] { -1, 1 })
            {
                string s = side < 0 ? "L" : "R";
                var shoulder = new GameObject("Shoulder" + s).transform;
                shoulder.SetParent(t, false);
                shoulder.localPosition = new Vector3(0.245f * side, 1.44f, 0f);
                shoulder.localRotation = Quaternion.Euler(0f, 0f, 9f * side);
                Part("UpperArm" + s, PrimitiveType.Cube, shoulder, new Vector3(0, -0.15f, 0), new Vector3(0.1f, 0.31f, 0.11f));
                Part("LowerArm" + s, PrimitiveType.Cube, shoulder, new Vector3(0, -0.44f, 0.01f), new Vector3(0.088f, 0.28f, 0.095f));
                Part("Hand" + s, PrimitiveType.Cube, shoulder, new Vector3(0, -0.64f, 0.01f), new Vector3(0.07f, 0.12f, 0.1f));
                Part("Thigh" + s, PrimitiveType.Cube, t, new Vector3(0.1f * side, 0.68f, 0), new Vector3(0.15f, 0.42f, 0.16f));
                Part("Shin" + s, PrimitiveType.Cube, t, new Vector3(0.1f * side, 0.29f, 0), new Vector3(0.12f, 0.4f, 0.13f));
                Part("Foot" + s, PrimitiveType.Cube, t, new Vector3(0.1f * side, 0.04f, 0.05f), new Vector3(0.11f, 0.06f, 0.24f));
            }
            // Overlays for wearables with their own shape
            var helmet = Group(WearStyle.Helmet, t);
            Part("Shell", PrimitiveType.Sphere, helmet, new Vector3(0, 1.73f, -0.005f), new Vector3(0.27f, 0.2f, 0.28f));
            var visor = Part("Visor", PrimitiveType.Cube, helmet, new Vector3(0, 1.72f, 0.12f), new Vector3(0.22f, 0.06f, 0.03f));
            SetColor(visor, new Color(0.08f, 0.09f, 0.1f));
            var cap = Group(WearStyle.Cap, t);
            Part("Crown", PrimitiveType.Sphere, cap, new Vector3(0, 1.75f, 0), new Vector3(0.24f, 0.14f, 0.245f));
            Part("Brim", PrimitiveType.Cube, cap, new Vector3(0, 1.73f, 0.14f), new Vector3(0.19f, 0.015f, 0.1f));
            var vest = Group(WearStyle.Vest, t);
            Part("Plate", PrimitiveType.Cube, vest, new Vector3(0, 1.26f, 0), new Vector3(0.42f, 0.42f, 0.27f));
            for (int i = 0; i < 3; i++) Part("Pouch" + i, PrimitiveType.Cube, vest, new Vector3(-0.12f + i * 0.12f, 1.12f, 0.15f), new Vector3(0.1f, 0.12f, 0.05f));
            SetLayer(doll.gameObject, Layer);
            NoProbes(doll.gameObject);
            Dress(null);
        }

        Transform Group(WearStyle style, Transform parent)
        {
            var g = new GameObject(style.ToString()).transform;
            g.SetParent(parent, false);
            overlays[style] = g.gameObject;
            return g;
        }

        Renderer Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            r.sharedMaterial.SetFloat("_Smoothness", 0.2f);
            if (!parts.ContainsKey(name)) parts[name] = r;
            return r;
        }

        void Tint(string part, Color c) { if (parts.TryGetValue(part, out var r)) SetColor(r, c); }

        static void SetColor(Renderer r, Color c)
        {
            r.sharedMaterial.SetColor("_BaseColor", c);
            r.sharedMaterial.color = c;
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        static void NoProbes(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
