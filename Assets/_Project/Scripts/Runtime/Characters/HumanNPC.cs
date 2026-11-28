using UnityEngine;
using UnityEngine.AI;

namespace Alcax.Characters
{
    /// <summary>
    /// An ambient human on a HumanMotor (feet drive): walks a patrol on the NavMesh (the agent only plans the path),
    /// walks corners as curves and turns sharp ones on the spot first, eases into each stop, faces the stop's way,
    /// and holds a random pose (talking, folded arms, phone, rail). Looks at you when you're close (the motor).
    /// Hit() and Die() drive the reactions.
    /// </summary>
    [RequireComponent(typeof(HumanMotor))]
    public class HumanNPC : MonoBehaviour
    {
        [SerializeField] Transform[] patrol = new Transform[0];
        [SerializeField] float walkParam = 1.4f;            // blend-tree speed for a plain walk
        [SerializeField] Vector2 pauseRange = new Vector2(6f, 14f);
        [SerializeField] float turnRadius = 0.9f;           // tightest walking curve (m)

        HumanMotor motor;
        Animator anim;
        NavMeshAgent agent;
        int next;
        float pauseUntil;
        int pendingPose = -1;
        bool dead, travelling;
        static readonly int PoseId = Animator.StringToHash("Pose"), HitId = Animator.StringToHash("Hit"),
            HitHeadId = Animator.StringToHash("HitHead"), DeadId = Animator.StringToHash("Dead");

        void Awake()
        {
            motor = GetComponent<HumanMotor>();
            motor.drive = MotorDrive.Feet;
            anim = GetComponent<Animator>();
            agent = GetComponent<NavMeshAgent>();
            if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 1.8f;
            agent.updatePosition = false;      // the feet move the body
            agent.updateRotation = false;
            agent.speed = 1.5f;
        }

        void Start() => pauseUntil = Time.time + Random.Range(1f, 4f);

        public void SetPatrol(Transform[] points) { patrol = points; next = 0; }

        public void Hit(bool head)
        {
            if (dead) return;
            anim.SetBool(HitHeadId, head);
            anim.SetTrigger(HitId);
        }

        public void Die()
        {
            if (dead) return;
            dead = true;
            anim.SetBool(DeadId, true);
            motor.Kill();
        }

        void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;
            float gait = 0f;
            if (agent.isOnNavMesh && patrol.Length > 0)
            {
                if (pauseUntil > 0f)
                {
                    // Paused at a stop: turn to the stop's facing, then strike the pose
                    if (pendingPose >= 0 && (motor.FaceYaw == null || Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, motor.FaceYaw.Value)) < 8f))
                    { anim.SetInteger(PoseId, pendingPose); pendingPose = -1; }
                    if (Time.time >= pauseUntil)
                    {
                        pauseUntil = 0f;
                        anim.SetInteger(PoseId, 0);
                        next = (next + 1) % patrol.Length;
                        agent.SetDestination(patrol[next].position);
                        travelling = true;
                    }
                }
                else if (travelling && !agent.pathPending)
                {
                    float remaining = agent.remainingDistance;
                    if (remaining <= 0.3f)
                    {
                        travelling = false;
                        pauseUntil = Time.time + Random.Range(pauseRange.x, pauseRange.y);
                        var f = patrol[next].forward; f.y = 0f;
                        motor.FaceYaw = f.sqrMagnitude > 0.01f ? Quaternion.LookRotation(f).eulerAngles.y : (float?)null;
                        pendingPose = Random.Range(0, 5);
                    }
                    else
                    {
                        var steer = agent.steeringTarget - transform.position; steer.y = 0f;
                        float angle = steer.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(transform.forward, steer, Vector3.up) : 0f;
                        if (Mathf.Abs(angle) > 60f && motor.Gait < 0.35f)
                            motor.FaceYaw = transform.eulerAngles.y + angle;      // sharp: turn on the spot first
                        else
                        {
                            motor.FaceYaw = null;
                            gait = walkParam * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(remaining / 1.4f));   // ease in to the stop
                            // walk the corner as a curve, as tight as the stride allows
                            float rate = Mathf.Max(35f, Mathf.Rad2Deg * Mathf.Max(motor.StrideSpeed, 0.3f) / turnRadius);
                            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(steer), rate * dt);
                        }
                    }
                }
                else if (!travelling && pauseUntil <= 0f)
                {
                    agent.SetDestination(patrol[next].position);
                    travelling = true;
                }
            }
            motor.GaitTarget = gait;
        }
    }
}
