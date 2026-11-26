using System.Collections.Generic;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Layer 2 of space awareness: the map's authored rooms and the doors between them (outside is the null room).
    /// Answers "which room is this point in" and "which door of my room leads toward his". Collected once per scene.
    /// </summary>
    public static class RoomGraph
    {
        static CombatRoom[] rooms;
        static CombatDoor[] doors;
        static bool built;

        public static IReadOnlyList<CombatRoom> Rooms { get { Ensure(); return rooms; } }
        public static IReadOnlyList<CombatDoor> Doors { get { Ensure(); return doors; } }
        public static bool HasRooms { get { Ensure(); return rooms.Length > 0; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            built = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => built = false;
        }

        static void Ensure()
        {
            if (built && rooms != null) return;
            rooms = Object.FindObjectsByType<CombatRoom>(FindObjectsInactive.Exclude);
            doors = Object.FindObjectsByType<CombatDoor>(FindObjectsInactive.Exclude);
            built = true;
        }

        /// <summary>The smallest room containing the point, or null (outside or unmapped).</summary>
        public static CombatRoom RoomAt(Vector3 p)
        {
            Ensure();
            CombatRoom best = null; float bestV = float.MaxValue;
            foreach (var r in rooms)
            {
                if (r == null || !r.Contains(p)) continue;
                float v = r.size.x * r.size.y * r.size.z;
                if (v < bestV) { bestV = v; best = r; }
            }
            return best;
        }

        /// <summary>
        /// The door out of my room on the shortest walk to his room (either may be null = outside), or null if we are in the
        /// same room or the rooms are not connected. Dijkstra over door positions: two doors link if they share a room
        /// (outside included), weighted by distance (stairs count their climb twice).
        /// </summary>
        public static CombatDoor FirstDoorToward(CombatRoom from, CombatRoom to, Vector3 fromPos, Vector3 toPos)
        {
            Ensure();
            if (from == to || doors.Length == 0) return null;
            int n = doors.Length;
            var dist = new float[n]; var first = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = float.MaxValue; first[i] = -1;
                if (doors[i] != null && doors[i].Connects(from)) { dist[i] = Cost(fromPos, doors[i].Position); first[i] = i; }
            }
            float best = float.MaxValue; int bestFirst = -1;
            while (true)
            {
                int u = -1; float du = float.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < du) { du = dist[i]; u = i; }
                if (u < 0 || du >= best) break;
                done[u] = true;
                var du0 = doors[u];
                if (du0.Connects(to)) { float total = du + Cost(du0.Position, toPos); if (total < best) { best = total; bestFirst = first[u]; } }
                for (int j = 0; j < n; j++)
                {
                    if (done[j] || doors[j] == null) continue;
                    var dj = doors[j];
                    bool share = dj.Connects(du0.a) || dj.Connects(du0.b);
                    if (!share) continue;
                    float nd = du + Cost(du0.Position, dj.Position);
                    if (nd < dist[j]) { dist[j] = nd; first[j] = first[u]; }
                }
            }
            return bestFirst >= 0 ? doors[bestFirst] : null;
        }

        static float Cost(Vector3 a, Vector3 b) => Vector3.Distance(a, b) + Mathf.Abs(a.y - b.y);

        public static string Name(CombatRoom r) => r != null ? r.roomName : "outside";
    }
}
