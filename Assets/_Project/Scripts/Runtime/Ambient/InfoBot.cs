using Alcax.Audio;
using Alcax.Combat;
using Alcax.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.Ambient
{
    /// <summary>
    /// A tall, floating information robot (an airport kiosk that learned to hover). It drifts slowly around the floor,
    /// rolls up to people to offer help nobody needs, apologizes to planters it bumps into, and can be talked to,
    /// with answers that are technically correct. It doesn't witness crimes. It can be knocked over.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class InfoBot : MonoBehaviour
    {
        enum Mode { Drift, Greet, Pause }
        const float EyeOpen = 0.085f;

        static readonly string[] Greetings =
        {
            "WELCOME TO ATRIUM C. YOU ARE HERE.",
            "HELLO. HAVE YOU TRIED WALKING?",
            "YOUR GATE IS: UNKNOWN. GOOD LUCK.",
            "PLEASE KEEP YOUR LIMBS INSIDE YOUR BODY.",
            "THE TIME IS NOW.",
            "I AM HERE TO HELP. I AM NOT SURE HOW.",
            "REMINDER: THE FLOOR IS DOWN.",
            "HAVE A NAVIGATIONALLY SOUND DAY.",
        };
        static readonly string[] Bumps = { "PARDON ME.", "EXCUSE ME, PLANT.", "OBSTACLE DETECTED. OBSTACLE FORGIVEN.", "SORRY." };

        NavMeshAgent agent;
        Npc npc;
        Transform visual, screen;
        TextMeshPro bubble;
        Renderer[] eyes;
        Health health;
        Mode mode;
        float modeUntil, bubbleUntil, nextBlink, blinkUntil, stuckFor, nextGreet;
        Transform greeting;
        Vector3 home;
        bool dead;
        float hoverSeed;

        void Awake()
        {
            home = transform.position;
            hoverSeed = Random.value * 50f;
            agent = GetComponent<NavMeshAgent>();
            agent.speed = 0.9f; agent.angularSpeed = 140f; agent.acceleration = 1.5f;
            agent.radius = 0.32f; agent.height = 1.85f; agent.baseOffset = 0f;
            agent.stoppingDistance = 0.3f; agent.avoidancePriority = 60;
            BuildModel();
            npc = gameObject.AddComponent<Npc>();
            npc.displayName = "Guide Unit 7";
            npc.role = "Passenger Assistance";
            npc.drivesBody = false;
            npc.witness = false;
            npc.dialogue = BuildDialogue();
            health = gameObject.AddComponent<Health>();
            health.SetMax(80f);
            health.Died += OnDied;
            health.Damaged += _ => { if (!dead) Say("PLEASE DO NOT DO THAT."); };
            Drift();
        }

        // ---------- Model ----------

        void BuildModel()
        {
            var shell = AmbientArt.Lit("BotShell", new Color(0.86f, 0.87f, 0.85f), 0.55f, 0.2f);
            var dark = AmbientArt.Lit("BotDark", new Color(0.16f, 0.17f, 0.19f), 0.4f, 0.5f);
            var accent = AmbientArt.Lit("BotAccent", new Color(0.2f, 0.55f, 0.85f), 0.5f, 0.2f);
            var glass = AmbientArt.Lit("BotScreen", new Color(0.03f, 0.05f, 0.07f), 0.9f, 0f);
            var glow = AmbientArt.Glow("BotGlow", new Color(0.45f, 0.9f, 1f), 3f);
            // The names Body/Head/Jaw are what Npc expects; it only reads them (drivesBody is off)
            visual = new GameObject("Body").transform;
            visual.SetParent(transform, false);
            AmbientArt.Part(PrimitiveType.Cylinder, "Can", visual, new Vector3(0f, 0.95f, 0f), new Vector3(0.6f, 0.62f, 0.6f), shell);
            AmbientArt.Part(PrimitiveType.Cylinder, "Skirt", visual, new Vector3(0f, 0.3f, 0f), new Vector3(0.66f, 0.06f, 0.66f), dark);
            AmbientArt.Part(PrimitiveType.Cylinder, "Hover", visual, new Vector3(0f, 0.22f, 0f), new Vector3(0.5f, 0.02f, 0.5f), glow);
            // Hover air: a wide, flat cone of faint puffs spreading out across the floor
            AmbientArt.Jet(visual, new Vector3(0f, 0.2f, 0f), new Vector3(90f, 0f, 0f), new Color(0.85f, 0.95f, 1f, 0.14f), 26f, 1.1f, 0.22f, 0.7f, 70f, 0.18f);
            AmbientArt.Part(PrimitiveType.Cylinder, "Stripe", visual, new Vector3(0f, 0.62f, 0f), new Vector3(0.61f, 0.04f, 0.61f), accent);
            var head = new GameObject("Head").transform;
            head.SetParent(visual, false);
            head.localPosition = new Vector3(0f, 1.62f, 0f);
            AmbientArt.Part(PrimitiveType.Sphere, "Dome", head, Vector3.zero, new Vector3(0.6f, 0.34f, 0.6f), shell);
            AmbientArt.Part(PrimitiveType.Cylinder, "Beacon", head, new Vector3(0f, 0.18f, 0f), new Vector3(0.08f, 0.04f, 0.08f), AmbientArt.Glow("BotBeacon", new Color(1f, 0.6f, 0.2f), 3f));
            new GameObject("Jaw").transform.SetParent(head, false);
            // The face: a dark screen with two simple eyes
// The face screen sits proud of the can's front, the eyes and mouth just in front of it
            screen = AmbientArt.Part(PrimitiveType.Cube, "Screen", visual, new Vector3(0f, 1.25f, 0.29f), new Vector3(0.36f, 0.24f, 0.04f), glass).transform;
            eyes = new Renderer[2];
            for (int i = 0; i < 2; i++)
                eyes[i] = AmbientArt.Part(PrimitiveType.Cube, "Eye" + i, visual, new Vector3(i == 0 ? -0.075f : 0.075f, 1.275f, 0.314f), new Vector3(0.05f, EyeOpen, 0.01f), glow).GetComponent<Renderer>();
            AmbientArt.Part(PrimitiveType.Cube, "Mouth", visual, new Vector3(0f, 1.19f, 0.314f), new Vector3(0.12f, 0.014f, 0.01f), glow);
            // Speech bubble
            var b = new GameObject("Bubble");
            b.transform.SetParent(transform, false);
            b.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            bubble = b.AddComponent<TextMeshPro>();
            bubble.fontSize = 0.9f;
            bubble.alignment = TextAlignmentOptions.Center;
            bubble.color = new Color(0.6f, 0.95f, 1f);
            bubble.rectTransform.sizeDelta = new Vector2(1.8f, 0.6f);
            bubble.textWrappingMode = TextWrappingModes.Normal;
            bubble.text = "";

            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.32f; col.height = 1.85f; col.center = new Vector3(0f, 0.95f, 0f);
        }

        DialogueTree BuildDialogue()
        {
            var t = ScriptableObject.CreateInstance<DialogueTree>();
            t.startNode = "start";
            t.Add("start", "GREETINGS, TRAVELER. I AM GUIDE UNIT 7. HOW MAY I ASSIST?",
                ("Where am I?", "where"), ("Where's the hotel?", "hotel"), ("Can you help me?", "help"), ("Never mind.", "bye"));
            t.Add("where", "YOU ARE HERE.", ("Where is here?", "here"), ("Thanks, I guess.", "start"));
            t.Add("here", "HERE IS WHERE YOU ARE. I HOPE THIS HELPS.", ("It doesn't.", "start"), ("Goodbye.", "bye"));
            t.Add("hotel", "THE HOTEL IS LOCATED IN THE HOTEL. ALSO UP THE STAIRS, THROUGH THE EAST HALL.", ("That second part helped.", "start"), ("Bye.", "bye"));
            t.Add("help", "I CAN HELP YOU. PLEASE SPECIFY HELP.", ("I need a weapon.", "weapon"), ("I'm hungry.", "food"), ("Forget it.", "bye"));
            t.Add("weapon", "WEAPONS ARE DANGEROUS. THE ARMORY IS SOUTH-EAST. PLEASE BE DANGEROUS RESPONSIBLY.", ("Thanks.", "start"));
            t.Add("food", "FOOD IS IN THE FOOD COURT. THIS IS WHY IT IS CALLED THAT.", ("Thanks.", "start"));
            t.Add("bye", "HAVE A NAVIGATIONALLY SOUND DAY.");
            return t;
        }

        // ---------- Behavior ----------

        void Drift()
        {
            mode = Mode.Drift;
            greeting = null;
            for (int i = 0; i < 12; i++)
            {
                var p = home + new Vector3(Random.Range(-12f, 12f), 0f, Random.Range(-10f, 10f));
                if (NavMesh.SamplePosition(p, out var h, 1.5f, NavMesh.AllAreas) && Mathf.Abs(h.position.y - transform.position.y) < 0.6f)
                {
                    if (agent.isOnNavMesh) agent.SetDestination(h.position);
                    break;
                }
            }
            modeUntil = Time.time + Random.Range(10f, 20f);
        }

        void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;
            // Hover and face screen life
// Hovering, not perfectly: a slow uneven rise and fall with a faint wobble
            float tb = Time.time + hoverSeed;
            visual.localPosition = new Vector3(0f, 0.12f + (Mathf.PerlinNoise(tb * 0.45f, hoverSeed) - 0.5f) * 0.08f + Mathf.Sin(tb * 1.1f) * 0.012f, 0f);
            visual.localRotation = Quaternion.Euler((Mathf.PerlinNoise(hoverSeed, tb * 0.4f) - 0.5f) * 3f, 0f, (Mathf.PerlinNoise(tb * 0.35f, hoverSeed + 4f) - 0.5f) * 3f);
            Blink();
            if (bubble.text.Length > 0)
            {
                var cam = Camera.main;
                if (cam != null) bubble.transform.rotation = Quaternion.LookRotation(bubble.transform.position - cam.transform.position);
                bubble.alpha = Mathf.Clamp01((bubbleUntil - Time.time) * 2f);
                if (Time.time > bubbleUntil) bubble.text = "";
            }

            // Talking with the player: stop and face them
            if (npc.InConversation)
            {
                if (agent.isOnNavMesh) agent.isStopped = true;
                Face(Camera.main != null ? Camera.main.transform.position : transform.position + transform.forward);
                return;
            }
            if (agent.isOnNavMesh) agent.isStopped = mode == Mode.Pause || mode == Mode.Greet && greeting != null && Vector3.Distance(transform.position, greeting.position) < 1.8f;

            switch (mode)
            {
                case Mode.Drift:
                    if (Time.time > nextGreet && FindSomeone(out var who)) { greeting = who; mode = Mode.Greet; modeUntil = Time.time + 12f; if (agent.isOnNavMesh) agent.SetDestination(who.position); break; }
                    if (!agent.pathPending && agent.isOnNavMesh && agent.remainingDistance < 0.5f || Time.time > modeUntil) { mode = Mode.Pause; modeUntil = Time.time + Random.Range(2f, 5f); }
                    // Bumped into something: apologize to it
                    if (agent.hasPath && agent.velocity.magnitude < 0.08f) stuckFor += dt; else stuckFor = 0f;
                    if (stuckFor > 1.5f) { stuckFor = 0f; Say(Bumps[Random.Range(0, Bumps.Length)]); Drift(); }
                    break;
                case Mode.Greet:
                    if (greeting == null || Time.time > modeUntil) { Drift(); break; }
                    float d = Vector3.Distance(transform.position, greeting.position);
                    if (d > 1.8f) { if (agent.isOnNavMesh && Time.frameCount % 20 == 0) agent.SetDestination(greeting.position); break; }
                    Face(greeting.position + Vector3.up * 1.5f);
                    if (bubble.text.Length == 0) { Say(Greetings[Random.Range(0, Greetings.Length)]); modeUntil = Time.time + 4f; nextGreet = Time.time + Random.Range(12f, 25f); }
                    break;
                case Mode.Pause:
                    if (Time.time > modeUntil) Drift();
                    break;
            }
        }

        bool FindSomeone(out Transform who)
        {
            who = null;
            float best = 7f;
            var pt = AI.PlayerTarget.Instance;
            if (pt != null && !pt.IsDead && Mathf.Abs(pt.Position.y - transform.position.y) < 1f)
            {
                float d = Vector3.Distance(pt.Position, transform.position);
                if (d < best && Random.value < 0.6f) { best = d; who = pt.transform; }
            }
            foreach (var n in Npc.Active)
            {
                if (n == null || n == npc || n.InConversation) continue;
                float d = Vector3.Distance(n.transform.position, transform.position);
                if (d < best && Mathf.Abs(n.transform.position.y - transform.position.y) < 1f) { best = d; who = n.transform; }
            }
            return who != null;
        }

        void Face(Vector3 p)
        {
            var to = p - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        void Blink()
        {
            if (Time.time > nextBlink) { blinkUntil = Time.time + 0.12f; nextBlink = Time.time + Random.Range(2f, 6f); }
            bool shut = Time.time < blinkUntil;
            foreach (var e in eyes) e.transform.localScale = new Vector3(0.05f, shut ? 0.012f : EyeOpen, 0.01f);
        }

        void Say(string line)
        {
            bubble.text = line;
            bubbleUntil = Time.time + 3.5f;
            SoundManager.PlayAt(Sfx.TalkBlip, transform.position + Vector3.up * 1.5f, 0.45f, 0.35f);
        }

        void OnDied()
        {
dead = true;
            bubble.text = "";
            agent.enabled = false;
            npc.CanTalk = false;
            npc.enabled = false;
            // A proper blast (it hurts anyone close), the dome and face torn off, and the scorched can topples
            Explosion.Blast(transform.position + Vector3.up * 1.1f, 4f, 55f, gameObject, 1.2f);
            AmbientArt.Wreck(gameObject, new[] { "Dome", "Beacon", "Screen", "Eye0", "Eye1", "Mouth", "Skirt" }, health);
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 60f;
            rb.AddForceAtPosition(Random.insideUnitSphere.normalized * 160f + Vector3.up * 40f, transform.position + Vector3.up * 1.7f, ForceMode.Impulse);
            enabled = false;
        }
    }
}
