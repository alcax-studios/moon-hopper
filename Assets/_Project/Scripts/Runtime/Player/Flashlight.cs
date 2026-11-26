using Alcax.Audio;
using Alcax.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.Player
{
    /// <summary>Toggles the head-mounted spotlight with F.</summary>
    public class Flashlight : MonoBehaviour
    {
        [SerializeField] Light spot;
        [SerializeField] bool startOn;

        public bool IsOn => spot != null && spot.enabled;

        void Awake()
        {
            if (spot == null) spot = GetComponentInChildren<Light>(true);
            if (spot != null) spot.enabled = startOn;
        }

        void Update()
        {
            if (!GameManager.IsPlaying || spot == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame)
            {
                spot.enabled = !spot.enabled;
                SoundManager.Play2D(Sfx.Click, 0.5f);
            }
        }
    }
}
