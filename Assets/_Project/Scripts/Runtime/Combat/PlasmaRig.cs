using Alcax.FX;
using UnityEngine;

namespace Alcax.Combat
{
    /// <summary>
    /// The plasma carbine's live parts and heat. Each shot adds heat; heat bleeds off once you stop firing. Full heat
    /// overheats it: the vents snap open, steam pours out, and it can't fire until it has vented. Coils spin faster
    /// and glow hotter with heat, electric arcs jump between them, the trigger pulls on each shot. On the view model
    /// root; finds its parts by name (Coil1-3, VentFront, VentRear, Trigger).
    /// </summary>
    public class PlasmaRig : MonoBehaviour
    {
        [SerializeField] float coilIdleSpin = 60f, coilHotSpin = 1500f;
        [SerializeField] float ventOpenAngle = 68f;
        [SerializeField] float arcWidth = 0.0016f;

        public float Heat { get; private set; }
        public bool Overheated { get; private set; }
        public bool CanFire => !Overheated;

        WeaponData data;
        Transform[] coils;
        Quaternion[] coilBase;
        Renderer[] coilR;
        Transform ventF, ventR, trigger;
        Quaternion ventFBase, ventRBase, triggerBase;
        float spin, spinBoost, lastFire = -10f, overheatT, triggerK, ventK, flash;
        LineRenderer[] arcs;
        Renderer[] fluids;   // CellFluid, BarrelFluid: the animated plasma fluid (Alcax/Plasma Fluid) gets _Heat and _Boil
        float arcTimer;
        MaterialPropertyBlock mpb;

        public void Init(WeaponData d) => data = d;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            coils = new Transform[3];
            coilBase = new Quaternion[3];
            coilR = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                coils[i] = Find(transform, "Coil" + (i + 1));
                if (coils[i] == null) continue;
                coilBase[i] = coils[i].localRotation;
                coilR[i] = coils[i].GetComponent<Renderer>();
            }
            ventF = Find(transform, "VentFront");
            ventR = Find(transform, "VentRear");
            trigger = Find(transform, "Trigger");
            if (ventF) ventFBase = ventF.localRotation;
            if (ventR) ventRBase = ventR.localRotation;
            if (trigger) triggerBase = trigger.localRotation;
            var fl = new System.Collections.Generic.List<Renderer>();
            foreach (var n in new[] { "CellFluid", "BarrelFluid" })
            {
                var f = Find(transform, n);
                if (f != null && f.TryGetComponent<Renderer>(out var r)) fl.Add(r);
            }
            fluids = fl.ToArray();

            var lib = FXLibrary.Get();
            arcs = new LineRenderer[3];
            for (int i = 0; i < arcs.Length && coils[i] != null; i++)
            {
                var go = new GameObject("Arc" + i);
                go.transform.SetParent(coils[i].parent, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = lib != null ? lib.arc : null;
                lr.useWorldSpace = false;
                lr.positionCount = 7;
                lr.widthMultiplier = arcWidth;
                lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = false;
                arcs[i] = lr;
            }
        }

        static Transform Find(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var f = Find(c, n); if (f != null) return f; }
            return null;
        }

        /// <summary>A shot left the gun.</summary>
        public void OnFire()
        {
            if (data == null) return;
            Heat = Mathf.Min(1f, Heat + data.heatPerShot);
            lastFire = Time.time;
            triggerK = 1f;
            spinBoost += 500f;
            flash = 1f;
            if (Heat >= 1f && !Overheated) { Overheated = true; overheatT = 0f; }
        }

        void OnDisable()
        {
            foreach (var a in arcs) if (a != null) a.enabled = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (data == null) return;

            // Heat: vents dump it while overheated; otherwise it bleeds off shortly after you stop firing
            if (Overheated)
            {
                overheatT += dt;
                Heat = Mathf.Lerp(1f, 0.25f, overheatT / data.overheatTime);
                if (overheatT >= data.overheatTime) Overheated = false;
            }
            else if (Time.time - lastFire > 0.2f) Heat = Mathf.Max(0f, Heat - data.coolRate * dt);

            // Coils spin up with heat (alternate directions), with a kick per shot
            spinBoost = Mathf.MoveTowards(spinBoost, 0f, 900f * dt);
            spin += (Mathf.Lerp(coilIdleSpin, coilHotSpin, Heat * Heat) + spinBoost) * dt;
            for (int i = 0; i < 3; i++)
                if (coils[i] != null) coils[i].localRotation = coilBase[i] * Quaternion.Euler(0f, 0f, spin * (i % 2 == 0 ? 1f : -1.3f));

            // Coil glow: brighter with heat, a flash per shot, white-hot and flickering when overheated
            flash = Mathf.MoveTowards(flash, 0f, dt * 8f);
            float glow = 1.5f + 9f * Heat * Heat + 6f * flash;
            if (Overheated) glow *= 0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 30f, 0.3f);
            var c = Color.Lerp(Color.white, new Color(1.2f, 1.15f, 1f), Heat) * glow;
            for (int i = 0; i < 3; i++)
            {
                if (coilR[i] == null) continue;
                coilR[i].GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", c);
                coilR[i].SetPropertyBlock(mpb);
            }

            // Fluid: churns faster and hotter with heat, boils while overheated
            foreach (var r in fluids)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetFloat("_Heat", Heat);
                mpb.SetFloat("_Boil", Overheated ? 1f : 0f);
                r.SetPropertyBlock(mpb);
            }

            // Vents: snap open on overheat, ease shut after; steam while open
            ventK = Overheated ? Mathf.MoveTowards(ventK, 1f, dt * 9f) : Mathf.MoveTowards(ventK, 0f, dt * 2.2f);
            float va = -ventOpenAngle * EaseOutBack(ventK);
            if (ventF) ventF.localRotation = ventFBase * Quaternion.Euler(va, 0f, 0f);
            if (ventR) ventR.localRotation = ventRBase * Quaternion.Euler(va * 0.85f, 0f, 0f);
            if (ventK > 0.3f)
            {
                float amount = Overheated ? 1f : ventK;
                foreach (var v in new[] { ventF, ventR })
                    if (v != null && Random.value < 0.75f)
                        PlasmaFX.Steam(v.position + v.up * 0.01f, v.up + v.forward * 0.3f, amount);
            }

            // Trigger pull
            triggerK = Mathf.MoveTowards(triggerK, 0f, dt * 10f);
            if (trigger) trigger.localRotation = triggerBase * Quaternion.Euler(16f * triggerK, 0f, 0f);

            UpdateArcs(dt);
        }

        void UpdateArcs(float dt)
        {
            arcTimer -= dt;
            bool live = Heat > 0.2f || flash > 0.3f || Overheated;
            for (int i = 0; i < arcs.Length; i++)
            {
                var a = arcs[i];
                if (a == null) continue;
                if (!live || Random.value > 0.35f + Heat * 0.6f) { if (arcTimer <= 0f) a.enabled = false; continue; }
                if (arcTimer > 0f) continue;
                a.enabled = true;
                // A jagged arc from the coil ring to the next coil (or the barrel), re-struck every few frames
                var from = coils[i].localPosition + Random.insideUnitSphere * 0.005f;
                var ring = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)) * Vector3.up * 0.019f;
                Vector3 p0 = from + ring;
                Vector3 p1 = i < 2 && coils[i + 1] != null ? coils[i + 1].localPosition + Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)) * Vector3.up * 0.019f
                                                         : from + ring * 0.5f;
                for (int k = 0; k < a.positionCount; k++)
                {
                    float t = k / (a.positionCount - 1f);
                    var p = Vector3.Lerp(p0, p1, t);
                    if (k > 0 && k < a.positionCount - 1) p += Random.insideUnitSphere * 0.004f;
                    a.SetPosition(k, p);
                }
                float w = arcWidth * (0.6f + Heat);
                a.widthMultiplier = w;
                var col = Color.Lerp(PlasmaFX.Energy, PlasmaFX.EnergyHot, Random.value);
                a.startColor = a.endColor = col;
            }
            if (arcTimer <= 0f) arcTimer = Random.Range(0.03f, 0.07f);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
