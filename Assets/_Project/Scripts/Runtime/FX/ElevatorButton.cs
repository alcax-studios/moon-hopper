using Alcax.Audio;
using Alcax.Core;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>An elevator button: calls the car to a floor, or steps it up or down (inside the car).</summary>
    public class ElevatorButton : Interactable
    {
        [SerializeField] Elevator elevator;
        [Tooltip("Floor index to call the car to (call buttons). Ignored when step is not zero.")]
        [SerializeField] int floor = -1;
        [Tooltip("+1 up, -1 down (car buttons).")]
        [SerializeField] int step;

        Vector3 baseScale;
        float pressedAt = -10f;

        void Awake() => baseScale = transform.localScale;

        public override bool Available => elevator != null;

        public override void Interact(GameObject who)
        {
            if (elevator == null) return;
            pressedAt = Time.time;
            SoundManager.PlayAt(Sfx.Click, transform.position, 0.6f);
            if (step != 0) elevator.Step(step);
            else elevator.Request(floor);
        }

        void Update()
        {
            // Press feedback: the button pushes in briefly
            float k = Mathf.Clamp01((Time.time - pressedAt) / 0.2f);
            transform.localScale = Vector3.Scale(baseScale, new Vector3(1f, 1f, Mathf.Lerp(0.4f, 1f, k)));
        }
    }
}
