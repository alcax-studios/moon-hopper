using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace Alcax.Core
{
    /// <summary>
    /// One owner for screen shake (Cinemachine Impulse) and hit-stop (time scale),
    /// so effects never fight each other or the pause state.
    /// </summary>
    public class FeelManager : MonoBehaviour
    {
        public static FeelManager Instance { get; private set; }

        [SerializeField] float hitStopTimeScale = 0.05f;
        [SerializeField] float shakeMultiplier = 1f;

        CinemachineImpulseSource impulse;
        Coroutine hitStop;

        void Awake()
        {
            Instance = this;
            impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition = new CinemachineImpulseDefinition
            {
                ImpulseChannel = 1,
                ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump,
                ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
                ImpulseDuration = 0.18f,
            };
        }

        public static void Shake(float force)
        {
            if (Instance == null || force <= 0f) return;
            var dir = Random.insideUnitSphere;
            dir.z *= 0.3f;
            Instance.impulse.GenerateImpulseWithVelocity(dir.normalized * force * Instance.shakeMultiplier);
        }

        public static void HitStop(float seconds)
        {
            if (Instance == null || seconds <= 0f || !GameManager.IsPlaying) return;
            if (Instance.hitStop != null) Instance.StopCoroutine(Instance.hitStop);
            Instance.hitStop = Instance.StartCoroutine(Instance.HitStopRoutine(seconds));
        }

        IEnumerator HitStopRoutine(float seconds)
        {
            Time.timeScale = hitStopTimeScale;
            yield return new WaitForSecondsRealtime(seconds);
            if (GameManager.IsPlaying) Time.timeScale = 1f;
            hitStop = null;
        }
    }
}
