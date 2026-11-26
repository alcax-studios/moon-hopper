using Alcax.Audio;
using Alcax.Player;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// A two-or-more-stop elevator: a car that rides a shaft between floor heights. Each floor has a landing door
    /// (an AutoDoor) that only unlocks while the car is parked there. Buttons (ElevatorButton) call it to a floor or
    /// step it up or down. The player riding inside is carried with the car.
    /// </summary>
    public class Elevator : MonoBehaviour
    {
        [SerializeField] Transform car;
        [Tooltip("World height of the car floor at each stop, bottom to top.")]
        [SerializeField] float[] floors = { 6f, 12f };
        [SerializeField] AutoDoor[] landingDoors;
        [SerializeField] Vector3 interior = new Vector3(1.7f, 2.5f, 1.7f);
        [SerializeField] float speed = 2f;
        [SerializeField] float doorCloseDelay = 0.6f;
[SerializeField] int startFloor;
        [Tooltip("The car's own door leaves (they ride with it). Each opens at one floor (carDoorFloor), sliding by carDoorOffsets, in step with that floor's landing door.")]
        [SerializeField] Transform[] carDoorLeaves;
        [SerializeField] int[] carDoorFloor;
        [SerializeField] Vector3[] carDoorOffsets;
        Vector3[] carDoorClosed;

        int at, target;
        bool moving;
        float departAt;
        CharacterController player;
        bool riding;
        float rideOffset;

        public int Floor => at;
        public bool Moving => moving;
        public int FloorCount => floors != null ? floors.Length : 0;

        void Start()
        {
            at = target = Mathf.Clamp(startFloor, 0, floors.Length - 1);
            if (car != null) car.position = new Vector3(car.position.x, floors[at], car.position.z);
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.GetComponent<CharacterController>();
            ApplyDoors();
            if (carDoorLeaves != null)
            {
                carDoorClosed = new Vector3[carDoorLeaves.Length];
                for (int i = 0; i < carDoorLeaves.Length; i++) if (carDoorLeaves[i] != null) carDoorClosed[i] = carDoorLeaves[i].localPosition;
            }
        }

        void Update()
        {
            // The car's doors follow the landing door of the floor they face (shut while riding)
            if (carDoorLeaves == null || carDoorClosed == null) return;
            for (int i = 0; i < carDoorLeaves.Length; i++)
            {
                if (carDoorLeaves[i] == null) continue;
                int f = carDoorFloor != null && i < carDoorFloor.Length ? carDoorFloor[i] : 0;
                float open = !moving && at == f && landingDoors != null && f < landingDoors.Length && landingDoors[f] != null ? landingDoors[f].Openness : 0f;
                carDoorLeaves[i].localPosition = carDoorClosed[i] + (carDoorOffsets != null && i < carDoorOffsets.Length ? carDoorOffsets[i] : Vector3.zero) * open;
            }
        }

        /// <summary>Send the car to a floor (index). Ignored while it's already moving.</summary>
        public void Request(int floor)
        {
            if (floors == null || floor < 0 || floor >= floors.Length || moving) return;
            if (floor == at) { ApplyDoors(); return; }
            target = floor;
            moving = true;
            departAt = Time.time + doorCloseDelay;   // let the doors close first
            ApplyDoors();
            SoundManager.PlayAt(Sfx.Click, car.position + Vector3.up * 1.2f, 0.5f);
        }

        /// <summary>One floor up (+1) or down (-1).</summary>
        public void Step(int dir) => Request(Mathf.Clamp(at + dir, 0, floors.Length - 1));

        void ApplyDoors()
        {
            if (landingDoors == null) return;
            for (int i = 0; i < landingDoors.Length; i++)
                if (landingDoors[i] != null) landingDoors[i].Locked = moving || i != at;
        }

        bool PlayerInside()
        {
            if (player == null || car == null) return false;
            var local = car.InverseTransformPoint(player.transform.position + Vector3.up * 0.3f);
            return Mathf.Abs(local.x) < interior.x * 0.5f && Mathf.Abs(local.z) < interior.z * 0.5f && local.y > -0.4f && local.y < interior.y;
        }

        void LateUpdate()
        {
            if (!moving || car == null || Time.time < departAt) return;
            float y = car.position.y, ty = floors[target];
            // Ease in and out over the ride
            float remaining = Mathf.Abs(ty - y);
            float v = speed * Mathf.Clamp(remaining / 0.8f, 0.25f, 1f);
            float ny = Mathf.MoveTowards(y, ty, v * Time.deltaTime);
            bool carry = PlayerInside();
            if (carry && !riding) { riding = true; rideOffset = Mathf.Clamp(player.transform.position.y - y, 0f, 0.3f); }
            if (!carry) riding = false;
            car.position = new Vector3(car.position.x, ny, car.position.z);
            // The car moved by transform: tell physics now, or the player's next ground check uses the old floor
            Physics.SyncTransforms();
            if (riding)
            {
                // Feet locked to the car floor (free to walk around inside): a smooth ride, no sinking, no pop at the end
                var pp = player.transform.position;
                player.enabled = false;
                player.transform.position = new Vector3(pp.x, ny + rideOffset, pp.z);
                player.enabled = true;
                Physics.SyncTransforms();
            }
            if (Mathf.Approximately(ny, ty))
            {
                moving = false;
                riding = false;
                at = target;
                ApplyDoors();
                SoundManager.PlayAt(Sfx.DialogueReply, car.position + Vector3.up * 2.2f, 0.45f);
            }
        }
    }
}
