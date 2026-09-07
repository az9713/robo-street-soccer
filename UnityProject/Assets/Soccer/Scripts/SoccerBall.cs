using UnityEngine;

namespace RoboStreetSoccer
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class SoccerBall : MonoBehaviour
    {
        public float Radius => .11f;
        [SerializeField] Transform visualShell;
        public float RenderedRadius
        {
            get
            {
                Transform rendered = visualShell ? visualShell : transform;
                return .5f * Mathf.Max(Mathf.Abs(rendered.lossyScale.x),
                    Mathf.Abs(rendered.lossyScale.y), Mathf.Abs(rendered.lossyScale.z));
            }
        }
        public Vector3 RenderedCenter => visualShell ? visualShell.position : transform.position;
        public Rigidbody Body { get; private set; }
        public string LastTouchName { get; private set; } = "none";
        public SoccerRobot LastTouchRobot { get; private set; }
        public Vector3 LastRequestedVelocity { get; private set; }
        SoccerGame game;

        public void Initialize(SoccerGame owner)
        {
            game = owner;
            Body = GetComponent<Rigidbody>();
        }

        void Awake()
        {
            if (!visualShell) visualShell = transform.Find("Readable Visual Shell");
            Body = GetComponent<Rigidbody>();
            Body.mass = .43f;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.maxAngularVelocity = 80f;
            Body.linearDamping = .035f;
            Body.angularDamping = .045f;
            UpdateVisualShell();
        }

        void LateUpdate() => UpdateVisualShell();

        void UpdateVisualShell()
        {
            if (!visualShell) return;
            // Keep the readability offset in world-up space. The child still
            // inherits ball rotation for visible roll, but its centre cannot
            // orbit the collider or dip below the pitch as the body spins.
            visualShell.position = transform.position + Vector3.up * Mathf.Max(0f, RenderedRadius - Radius);
        }

        void FixedUpdate()
        {
            if (!Body || Body.IsSleeping() || transform.position.y > Radius + .055f) return;
            Vector3 velocity = Body.linearVelocity;
            Vector3 planar = Vector3.ProjectOnPlane(velocity, Vector3.up);
            float speed = planar.magnitude;
            if (speed < .001f) return;
            // Constant rolling resistance gives a street ball a finite coast
            // without magnetising it to a player or changing airborne shots.
            float nextSpeed = Mathf.Max(0f, speed - .55f * Time.fixedDeltaTime);
            Vector3 slowed = planar * (nextSpeed / speed);
            Body.linearVelocity = new Vector3(slowed.x, velocity.y, slowed.z);
        }

        public void ResetBall(Vector3 groundPosition, bool clearLastTouch = true)
        {
            ResetBallAtPosition(new Vector3(groundPosition.x, Radius + .015f, groundPosition.z), clearLastTouch);
        }

        public void ResetBallAtPosition(Vector3 centrePosition, bool clearLastTouch = true)
        {
            transform.position = centrePosition;
            transform.rotation = Quaternion.identity;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.Sleep();
            UpdateVisualShell();
            if (clearLastTouch)
            {
                LastTouchName = "none";
                LastTouchRobot = null;
            }
        }

        public void ApplyKick(SoccerRobot robot, Vector3 planarDirection, float speed, float lift)
        {
            Vector3 direction = Vector3.ProjectOnPlane(planarDirection, Vector3.up).normalized;
            Vector3 desiredVelocity = direction * speed + Vector3.up * lift;
            LastRequestedVelocity = desiredVelocity;
            Body.AddForce(desiredVelocity - Body.linearVelocity, ForceMode.VelocityChange);
            Body.AddTorque(Vector3.Cross(Vector3.up, direction) * (speed / Radius) * .08f, ForceMode.VelocityChange);
            LastTouchName = robot.name;
            LastTouchRobot = robot;
            if (game) game.RegisterPhysicalTouch(robot, "kick");
        }

        public void Cushion(SoccerRobot robot, Vector3 retainDirection)
        {
            Vector3 current = Body.linearVelocity;
            Vector3 desired = Vector3.ProjectOnPlane(retainDirection, Vector3.up).normalized * Mathf.Min(.55f, current.magnitude * .12f);
            Body.AddForce(desired - current, ForceMode.VelocityChange);
            LastTouchName = robot.name;
            LastTouchRobot = robot;
            if (game) game.RegisterPhysicalTouch(robot, "cushion");
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!game) return;
            if (collision.collider.CompareTag("GoalFrame"))
                game.Record("frame_rebound", LastTouchName, collision.collider.name, -1f, $"relativeSpeed={collision.relativeVelocity.magnitude:0.00}");
            else if (collision.collider.CompareTag("Board"))
                game.Record("wall_rebound", LastTouchName, collision.collider.name, -1f, $"relativeSpeed={collision.relativeVelocity.magnitude:0.00}");
            SoccerRobot robot = collision.collider.GetComponentInParent<SoccerRobot>();
            if (robot)
            {
                LastTouchName = robot.name;
                LastTouchRobot = robot;
                game.RegisterPhysicalTouch(robot, "body_collision");
                game.Record("ball_touch", robot.name, "collision", -1f, "Physical rigid-body contact");
            }
        }
    }
}
