using System.Collections.Generic;
using Alcax.Audio;
using UnityEngine;

namespace Alcax.Combat
{
    /// <summary>
    /// A pane of glass on the Glass layer: everyone sees through it (vision rays skip the layer), bodies can't pass it.
    /// Armored (station exterior, safety railings): stops bullets, each hit leaves a crack that grows into a spiderweb
    /// with repeated hits nearby. Breakable (interior): bullets pass (weakened) and leave holes; enough damage shatters it,
    /// and the opening becomes passable.
    /// </summary>
    public class GlassPane : MonoBehaviour
    {
        public enum Kind { Armored, Breakable }

        public const int Layer = 9;
        public const int Mask = 1 << Layer;

        [SerializeField] Kind kind = Kind.Armored;
        [SerializeField] float health = 70f;
        [Tooltip("Renderer hidden when it shatters (breakable panes have their own).")]
        [SerializeField] Renderer visual;

        public Kind Type => kind;
        public bool Armored => kind == Kind.Armored;
        public bool Broken { get; private set; }
        /// <summary>Hits it has taken (cracked glass is visibly armored: the AI stops shooting through it).</summary>
        public int Hits { get; private set; }

        readonly List<Transform> marks = new List<Transform>();
        static Material crackMat, holeMat, shardMat;

        /// <summary>A bullet arrives. Returns true if it passes through (with a damage multiplier), false if stopped.</summary>
        public bool OnBulletHit(Vector3 point, Vector3 normal, Vector3 dir, float damage, out float passMultiplier)
        {
            passMultiplier = 0f;
            if (Broken) { passMultiplier = 1f; return true; }
            Hits++;
            if (kind == Kind.Armored)
            {
                Mark(point, normal, false);
                SoundManager.PlayAt(Sfx.CasingTink, point, 0.5f, 0.2f);
                return false;
            }
            Mark(point, normal, true);
            SoundManager.PlayAt(Sfx.CasingTink, point, 0.6f, 0.25f);
            health -= damage;
            if (health <= 0f) Shatter(point, dir);
            passMultiplier = 0.75f;
            return true;
        }

        void Mark(Vector3 point, Vector3 normal, bool hole)
        {
            // A new hit next to an old crack grows it into a spiderweb instead of adding another
            foreach (var m in marks)
                if (m != null && (m.position - point).sqrMagnitude < 0.3f * 0.3f)
                {
                    m.localScale = Vector3.Min(m.localScale * 1.35f, Vector3.one * 1.6f);
                    return;
                }
            if (marks.Count > 40) return;
            // One unscaled holder per mark (so the pane's scale doesn't stretch it), with a face on each side
            var n = normal.sqrMagnitude > 0.01f ? normal.normalized : -transform.forward;
            var holder = new GameObject(hole ? "BulletHole" : "Crack").transform;
            holder.position = point;
            holder.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float size = hole ? Random.Range(0.12f, 0.18f) : Random.Range(0.22f, 0.34f);
            holder.localScale = Vector3.one * size;
            var mat = hole ? HoleMaterial() : CrackMaterial();
            for (int side = 0; side < 2; side++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(holder, false);
                q.transform.localPosition = new Vector3(0f, 0f, side == 0 ? -0.004f / size : 0.004f / size);
                q.transform.localRotation = side == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                q.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            marks.Add(holder);
        }

        void OnDestroy()
        {
            foreach (var m in marks) if (m != null) Destroy(m.gameObject);
        }

        void Shatter(Vector3 point, Vector3 dir)
        {
            Broken = true;
            if (visual != null) visual.enabled = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            var obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (obstacle != null) obstacle.enabled = false;   // the opening is walkable now
            foreach (var m in marks) if (m != null) Destroy(m.gameObject);
            marks.Clear();
            var b = visual != null ? visual.bounds : new Bounds(transform.position, Vector3.one);
            for (int i = 0; i < 16; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s.name = "Shard";
                s.transform.position = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
                s.transform.rotation = Random.rotation;
                s.transform.localScale = new Vector3(Random.Range(0.06f, 0.22f), Random.Range(0.06f, 0.22f), 0.01f);
                s.GetComponent<MeshRenderer>().sharedMaterial = ShardMaterial();
                s.layer = 2;   // debris: never blocks bullets or sight
                var rb = s.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                rb.AddForce((dir.normalized * Random.Range(1f, 3f) + Random.insideUnitSphere) * 0.05f, ForceMode.Impulse);
                Destroy(s, Random.Range(5f, 8f));
            }
            for (int i = 0; i < 4; i++) SoundManager.PlayAt(Sfx.CasingTink, point + Random.insideUnitSphere * 0.6f, 0.9f, 0.3f);
            SoundManager.PlayAt(Sfx.TargetClang, point, 0.35f, 0.3f);
        }

        // ---------- Materials (made once, procedurally) ----------

        static Material Transparent(Texture2D tex, Color tint)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            var m = new Material(sh);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetColor("_BaseColor", tint); m.color = tint;
            return m;
        }

        static Material CrackMaterial()
        {
            if (crackMat != null) return crackMat;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            var rng = new System.Random(11);
            // Radial cracks with a few concentric breaks
            for (int r = 0; r < 14; r++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2f), len = 0.25f + (float)rng.NextDouble() * 0.25f;
                for (float t = 0f; t < len; t += 0.003f)
                {
                    a += (float)(rng.NextDouble() - 0.5f) * 0.08f;
                    int x = (int)((0.5f + Mathf.Cos(a) * t) * n), y = (int)((0.5f + Mathf.Sin(a) * t) * n);
                    if (x >= 0 && y >= 0 && x < n && y < n) px[y * n + x] = new Color(1f, 1f, 1f, 0.85f * (1f - t / len) + 0.15f);
                }
            }
            foreach (float rad in new[] { 0.08f, 0.17f, 0.26f })
                for (float a = 0f; a < Mathf.PI * 2f; a += 0.01f)
                {
                    if (rng.NextDouble() < 0.35) continue;
                    int x = (int)((0.5f + Mathf.Cos(a) * rad) * n), y = (int)((0.5f + Mathf.Sin(a) * rad) * n);
                    if (x >= 0 && y >= 0 && x < n && y < n) px[y * n + x] = new Color(1f, 1f, 1f, 0.55f);
                }
            for (int y = n / 2 - 3; y < n / 2 + 3; y++) for (int x = n / 2 - 3; x < n / 2 + 3; x++) px[y * n + x] = new Color(1f, 1f, 1f, 0.9f);
            tex.SetPixels(px); tex.Apply();
            crackMat = Transparent(tex, new Color(0.92f, 0.96f, 1f, 1f));
            return crackMat;
        }

        static Material HoleMaterial()
        {
            if (holeMat != null) return holeMat;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = d < 0.12f ? 0.95f : d < 0.45f ? 0.35f * (1f - d) * ((x * 7 + y * 13) % 5 == 0 ? 2f : 0.6f) : 0f;
                    px[y * n + x] = d < 0.12f ? new Color(0.05f, 0.05f, 0.06f, a) : new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            holeMat = Transparent(tex, Color.white);
            return holeMat;
        }

        static Material ShardMaterial()
        {
            if (shardMat != null) return shardMat;
            var tex = new Texture2D(2, 2);
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); tex.Apply();
            shardMat = Transparent(tex, new Color(0.75f, 0.9f, 1f, 0.45f));
            return shardMat;
        }
    }
}
