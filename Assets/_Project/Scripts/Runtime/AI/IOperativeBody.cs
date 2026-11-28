using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// What an Operative's mind drives each frame, whatever the body is: the procedural mannequin (OperativeBody)
    /// or an animated human (Alcax.Characters.HumanOperativeBody). The mind moves the root (NavMeshAgent) and turns
    /// it; the body makes the limbs, the weapon, and the gaze match.
    /// </summary>
    public interface IOperativeBody
    {
        Vector3 Velocity { get; set; }
        float Crouch { get; set; }
        bool WeaponRaised { get; set; }
        Vector3 AimDirection { get; set; }
        Vector3? LookPoint { get; set; }
        bool Scanning { get; set; }
        float ReloadProgress { get; set; }
        float MeleeT { get; set; }
        bool Pistol { get; set; }
        bool HandsUp { get; set; }
        bool Holstered { get; set; }
        bool Drawing { get; }
        float SideLean { get; set; }
        float BlindFire { get; set; }
        float BlindSide { get; set; }

        Transform Head { get; }
        Transform Muzzle { get; }
        /// <summary>The torso (loot and hit anchors).</summary>
        Transform Torso { get; }

        void Flinch(Vector3 worldDir, float strength);
        /// <summary>Let go of the weapon: it falls as a physics object.</summary>
        Transform DropWeapon();
        /// <summary>Stop animating (death).</summary>
        void Freeze();
        /// <summary>Become a ragdoll, pushed by the killing hit.</summary>
        void Ragdoll(Vector3 hitDir, Vector3 hitPoint, float impulse);
    }
}
