using Alcax.AI;
using Alcax.Dialogue;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Alcax.Items
{
    /// <summary>
    /// Feeds the quest log and the codex from what happens in the game: the starting quest on the station, item
    /// entries when you pick things up, people when you talk to them, factions when you first see their units.
    /// Created once at startup and kept across maps. The content here is a first pass until lore is written.
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        float nextLook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<StoryDirector>() != null) return;
            var go = new GameObject("StoryDirector");
            DontDestroyOnLoad(go);
            go.AddComponent<StoryDirector>();
        }

        void Awake()
        {
            PlayerInventory.ItemGained += OnGained;
            PlayerInventory.ItemUsed += OnUsed;
            DialogueUI.Opened += OnTalk;
            SceneManager.sceneLoaded += OnScene;
            OnScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnDestroy()
        {
            PlayerInventory.ItemGained -= OnGained;
            PlayerInventory.ItemUsed -= OnUsed;
            DialogueUI.Opened -= OnTalk;
            SceneManager.sceneLoaded -= OnScene;
        }

        void OnScene(Scene s, LoadSceneMode mode)
        {
            Codex.Unlock("station", "Station", "The station",
                "A transit station on a desert moon, under a pale ringed gas giant. Ships land on the pads outside the " +
                "atrium; the south wing holds the range and the armory, the food court is through the west doors, and an " +
                "airport hotel sits above the mezzanine.", announce: false);
            if (s.name == "Atrium")
            {
                Codex.Unlock("atrium", "Station", "The atrium",
                    "The station's great hall: a glass wall onto the landing pads, a grand stair to the mezzanine, and an " +
                    "information booth where guest services helps arrivals find their way.", announce: false);
                if (!QuestLog.Has("orientation"))
                    QuestLog.Add(new Quest
                    {
                        id = "orientation", title = "Orientation", giver = "Station",
                        summary = "New on the station. Get your bearings: meet the people, gear up, and learn to patch yourself up.",
                    }
                    .Objective("Talk to someone at the station", "talk")
                    .Objective("Get a weapon from the armory", "get:weapon")
                    .Objective("Put on something protective", "wear:armor")
                    .Objective("Use a bandage or a medkit", "use:aid"));
            }
            if (s.name == "Playground")
                Codex.Unlock("playground", "Station", "The proving ground",
                    "An open-air test range outside the station: walls, a maze, and a close-quarters house. Nobody asks what it's for.", announce: false);
        }

        void OnGained(ItemDefinition d, int n)
        {
            Codex.UnlockItem(d);
            if (d.category == ItemCategory.Weapon) QuestLog.Progress("get:weapon");
        }

        void OnUsed(ItemDefinition d)
        {
            if (d.category == ItemCategory.Aid) QuestLog.Progress("use:aid");
            if (d.category == ItemCategory.Food || d.category == ItemCategory.Drink) QuestLog.Progress("use:food");
        }

        void OnTalk(Npc n)
        {
            if (n == null) return;
            Codex.Unlock("person:" + n.displayName, "People", n.displayName,
                $"{n.role}. Met on the station.");
            QuestLog.Progress("talk");
        }

        void Update()
        {
            // Armor: worn protective gear (checked here so dragging it onto the body counts too)
            var inv = PlayerInventory.Instance;
            if (inv != null && (inv.Armor(true) >= 10f || inv.Armor(false) >= 20f)) QuestLog.Progress("wear:armor");

            // Factions: the first time a unit of one is in plain view, close enough to make out
            if (Time.time < nextLook) return;
            nextLook = Time.time + 1f;
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var op in Operative.All)
            {
                if (op == null || op.IsDead || op.Faction == null || Codex.Has("faction:" + op.Faction.displayName)) continue;
                var to = op.EyePosition - cam.transform.position;
                if (to.magnitude > 35f || Vector3.Angle(cam.transform.forward, to) > 30f) continue;
                if (Physics.Linecast(cam.transform.position, op.EyePosition, AILayers.World, QueryTriggerInteraction.Ignore)) continue;
                Codex.Unlock("faction:" + op.Faction.displayName, "Factions", op.Faction.displayName, FactionText(op.Faction.displayName));
            }
        }

        static string FactionText(string name) => name switch
        {
            "Station Security" => "The station's own guards. Pistols, patrols, and a strict line: shoot outside the range, or at one of them, and you're the problem. They stand down once you're out of sight long enough.",
            "Insurgents" => "Loose cells probing the outer locks. Aggressive, mobile, and short on discipline; they like to shoot on the move.",
            "Special Ops" => "Professional operators. They fight from cover, move in bounds, and talk to each other constantly.",
            _ => "An armed group on the station.",
        };
    }
}
