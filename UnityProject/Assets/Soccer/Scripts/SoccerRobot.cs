using UnityEngine;

namespace RoboStreetSoccer
{
    public enum RobotAction { None, Dribble, Receive, Pass, Shoot, Tackle, ContactLean }

    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class SoccerRobot : MonoBehaviour
    {
        [SerializeField] SoccerTeam team;
        [SerializeField] bool aiDriven;
        [SerializeField] int rosterIndex;
        [SerializeField] Transform rightFoot;
        [SerializeField] Transform selectionRing;
        [SerializeField] Animator animator;

        public SoccerTeam Team => team;
        public bool IsAiDriven => aiDriven;
        public int RosterIndex => rosterIndex;
        public bool IsSelected { get; private set; }
        public Rigidbody Body { get; private set; }
        public Vector3 DesiredMove { get; private set; }
        public RobotAction CurrentAction { get; private set; }
        public Transform RightFoot => rightFoot;
        public Animator Animator => animator;
        public float PhysicalFootGap => CurrentFootGap(false);
        public float VisibleFootGap => CurrentFootGap(true);

        SoccerGame game;
        SoccerBall ball;
        Vector3 inputMove;
        Vector3 actionDirection;
        Vector3 kickDirection;
        SoccerRobot actionTarget;
        float actionContact;
        float actionEnds;
        float contactWindowEnd;
        bool contactApplied;
        bool awaitingReceptionConfirmation;
        float receptionPhysicalGap;
        float receptionVisibleGap;
        float receptionImpulseTime;
        float nextDribbleTime;
        float shootRequestExpires = -1f;
        float passRequestExpires = -1f;
        SoccerRobot passRequestTarget;
        float passRequestError;
        float nextBodyLog;
        float nextTackleTime;
        float reactionUntil;
        Vector3 blockingNormal;
        float blockingUntil;
        float shotAimBias;
        float passLateralError;

        const float MaximumSpeed = 3.7f;
        const float Acceleration = 16f;
        const float TurnDegrees = 540f;
        const float FootContactRadius = .115f;
        const float FootContactLength = .30f;

        public void ConfigureVisual(Animator targetAnimator, Transform targetRightFoot, Transform ring)
        {
            animator = targetAnimator;
            rightFoot = targetRightFoot;
            selectionRing = ring;
        }

        public void ConfigureTeam(SoccerTeam value, bool controlledByAi, int stableRosterIndex)
        {
            team = value;
            aiDriven = controlledByAi;
            rosterIndex = stableRosterIndex;
        }

        public void Initialize(SoccerGame owner, SoccerBall targetBall)
        {
            game = owner;
            ball = targetBall;
            Body = GetComponent<Rigidbody>();
        }

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = 68f;
            Body.linearDamping = 1.2f;
            Body.angularDamping = 8f;
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        public void ResetRobot(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            DesiredMove = Vector3.zero;
            inputMove = Vector3.zero;
            CurrentAction = RobotAction.None;
            contactApplied = false;
            awaitingReceptionConfirmation = false;
            shootRequestExpires = -1f;
            passRequestExpires = -1f;
            passRequestTarget = null;
            nextDribbleTime = 0f;
            nextTackleTime = 0f;
            reactionUntil = 0f;
            actionTarget = null;
            actionDirection = Vector3.zero;
            kickDirection = Vector3.zero;
            if (animator) animator.CrossFade("Idle", 0f);
            SoccerFootPlant footPlant = animator ? animator.GetComponent<SoccerFootPlant>() : GetComponentInChildren<SoccerFootPlant>();
            if (footPlant) footPlant.ResetAnchorsAfterTeleport();
            SoccerAgentAI controller = GetComponent<SoccerAgentAI>();
            if (controller) controller.ResetController();
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectionRing) selectionRing.gameObject.SetActive(selected);
        }

        void Update()
        {
            if (!game || !game.CanHumanAct(this) || game.AcceptanceMode) return;
            Vector3 raw = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            Camera view = Camera.main;
            if (view)
            {
                Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
                inputMove = Vector3.ClampMagnitude(right * raw.x + forward * raw.z, 1f);
            }
            else inputMove = Vector3.ClampMagnitude(raw, 1f);

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (game.Controls == ControlPreset.Advanced && game.State == MatchState.Live && !game.RobotControlsBall(this))
                    game.SelectRobot(game.TeammateOf(this));
                else RequestPass(game.TeammateOf(this));
            }
            bool advancedMouseAction = game.Controls == ControlPreset.Advanced && Input.GetMouseButtonDown(0) && !PointerOverHud();
            if (Input.GetKeyDown(KeyCode.J) || advancedMouseAction)
            {
                if (game.Controls == ControlPreset.Advanced && !game.RobotControlsBall(this)) TryStartTackle();
                else RequestShoot();
            }
        }

        bool PointerOverHud()
        {
            Vector3 pointer = Input.mousePosition;
            bool leftPanel = pointer.x <= 380f && pointer.y >= Screen.height - 250f;
            bool scorePanel = pointer.x >= Screen.width * .5f - 190f && pointer.x <= Screen.width * .5f + 190f
                && pointer.y >= Screen.height - 150f;
            return leftPanel || scorePanel;
        }

        void FixedUpdate()
        {
            if (!game || (game.State != MatchState.Live && !(game.State == MatchState.KickInSetup && game.KickInRobot == this))) return;
            if (IsSelected && !game.AcceptanceMode) DesiredMove = inputMove;
            if (IsSelected && shootRequestExpires >= Time.time)
            {
                if (TryStartShoot()) shootRequestExpires = -1f;
            }
            else if (shootRequestExpires >= 0f)
            {
                shootRequestExpires = -1f;
                if (IsSelected) game.ShowHint("Bring ball closer", 1f);
            }

            if (CurrentAction == RobotAction.None && passRequestTarget && passRequestExpires >= Time.time)
            {
                Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up);
                if (toBall.sqrMagnitude > .001f)
                {
                    if (!PassTargetReady(passRequestTarget)) DesiredMove = toBall.normalized * .35f;
                    else
                    {
                        Vector3 matchVelocity = Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up) / MaximumSpeed;
                        float correction = Mathf.Clamp((toBall.magnitude - .44f) * 1.8f, -.18f, .65f);
                        DesiredMove = Vector3.ClampMagnitude(matchVelocity + toBall.normalized * correction, 1f);
                    }
                }
                TryStartPassNow(passRequestTarget, passRequestError);
            }
            else if (passRequestExpires >= 0f && passRequestExpires < Time.time)
            {
                string reason = PassSetupReason(passRequestTarget);
                game.ShowHint(reason, 1.2f);
                game.Record("pass_request_expired", name, passRequestTarget ? passRequestTarget.name : "none",
                    CurrentFootGap(false), $"reason={reason}; ballSpeed={Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up).magnitude:0.000}");
                passRequestExpires = -1f;
                passRequestTarget = null;
            }

            if (IsSelected && !game.AcceptanceMode && game.Controls == ControlPreset.Beginner && !game.RobotControlsBall(this))
                TryAssistedTackle();
            UpdateAction();
            DriveBody();
            TryAutomaticDribble();
            UpdateAnimator();
        }

        void DriveBody()
        {
            // Bring the rigid body under the authored support foot before its
            // planted contact beat. Full-speed root travel through the beat
            // produces visible skating even if a visual IK correction is used.
            bool plantBrake = (CurrentAction == RobotAction.Dribble || CurrentAction == RobotAction.Pass)
                && Time.time >= actionContact - .30f && Time.time <= actionContact + .10f;
            float actionScale = CurrentAction == RobotAction.None ? 1f
                : plantBrake ? .22f
                : ((CurrentAction == RobotAction.Shoot || CurrentAction == RobotAction.Tackle) && Time.time < actionContact) ? .55f
                : .12f;
            Vector3 wanted = Vector3.ClampMagnitude(DesiredMove, 1f) * MaximumSpeed * actionScale;
            if ((CurrentAction == RobotAction.Shoot || CurrentAction == RobotAction.Tackle) && Time.time < actionContact)
            {
                Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up);
                float approachSpeed = Time.time < actionContact - .24f
                    ? Mathf.Clamp((toBall.magnitude - .58f) * 5f, 0f, 2.4f) : 0f;
                wanted = toBall.sqrMagnitude > .001f ? toBall.normalized * approachSpeed : Vector3.zero;
            }
            if (Time.time < blockingUntil && Vector3.Dot(wanted, -blockingNormal) > .2f)
                wanted = Vector3.ProjectOnPlane(wanted, blockingNormal) * .55f;
            Vector3 planar = Vector3.ProjectOnPlane(Body.linearVelocity, Vector3.up);
            Vector3 change = Vector3.ClampMagnitude(wanted - planar, Acceleration * Time.fixedDeltaTime);
            Body.AddForce(change, ForceMode.VelocityChange);
            Vector3 facing = CurrentAction != RobotAction.None && actionDirection.sqrMagnitude > .01f ? actionDirection : wanted;
            if (facing.sqrMagnitude > .02f)
            {
                Quaternion target = Quaternion.LookRotation(Vector3.ProjectOnPlane(facing, Vector3.up));
                Body.MoveRotation(Quaternion.RotateTowards(Body.rotation, target, TurnDegrees * Time.fixedDeltaTime));
            }
            Vector3 velocity = Body.linearVelocity;
            Vector3 clamped = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(velocity, Vector3.up), MaximumSpeed * 1.08f);
            Body.linearVelocity = new Vector3(clamped.x, velocity.y, clamped.z);
        }

        void UpdateAnimator()
        {
            if (!animator || CurrentAction != RobotAction.None || Time.time < reactionUntil) return;
            float speed = Vector3.ProjectOnPlane(Body.linearVelocity, Vector3.up).magnitude;
            string state = speed > .18f ? "Run" : "Idle";
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName(state)) animator.CrossFade(state, .10f);
            animator.speed = state == "Run" ? Mathf.Lerp(1.1f, 4.4f, speed / MaximumSpeed) : 1f;
        }

        public void SetMove(Vector3 worldDirection)
        {
            DesiredMove = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(worldDirection, Vector3.up), 1f);
        }

        public bool TryStartPass(SoccerRobot target, float lateralError = 0f)
        {
            if (!target || CurrentAction != RobotAction.None) return false;
            if (!PassSetupReady() || !PassTargetReady(target))
            {
                passRequestTarget = target;
                passRequestError = lateralError;
                passRequestExpires = Time.time + 3.2f;
                return false;
            }
            return TryStartPassNow(target, lateralError);
        }

        public void RequestPass(SoccerRobot target, float lateralError = 0f)
        {
            passRequestTarget = target;
            passRequestError = lateralError;
            passRequestExpires = Time.time + 3.2f;
            game.ShowHint("PASS QUEUED — GATHERING BALL", .8f);
            TryStartPassNow(target, lateralError);
        }

        bool PassSetupReady()
        {
            if (!game.RobotControlsBall(this) || CurrentFootGap(false) > .05f) return false;
            Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up);
            if (toBall.sqrMagnitude < .001f) return true;
            Vector3 relativeVelocity = Vector3.ProjectOnPlane(ball.Body.linearVelocity - Body.linearVelocity, Vector3.up);
            return Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up).magnitude <= .75f
                && relativeVelocity.magnitude <= .8f;
        }

        string PassSetupReason(SoccerRobot target)
        {
            if (!PassTargetReady(target)) return "FACE TEAMMATE TO PASS";
            if (CurrentFootGap(false) > .05f || !game.RobotControlsBall(this)) return "BRING BALL CLOSER";
            return "SETTLE THE BALL TO PASS";
        }

        bool PassTargetReady(SoccerRobot target)
        {
            if (!target) return false;
            Vector3 toTarget = Vector3.ProjectOnPlane(target.transform.position - ball.transform.position, Vector3.up);
            if (game.State == MatchState.KickInSetup && game.KickInRobot == this)
                return toTarget.sqrMagnitude > .64f && Vector3.Dot(transform.forward, toTarget.normalized) >= .55f;
            // A pass may go forward, lateral, or toward the defending goal once
            // the player faces that outlet. Reject only a target hidden behind
            // the planted body, which would make the right foot kick through it.
            return toTarget.sqrMagnitude > .64f && Vector3.Dot(transform.forward, toTarget.normalized) >= .25f;
        }

        bool TryStartPassNow(SoccerRobot target, float lateralError)
        {
            if (!target || CurrentAction != RobotAction.None || !PassSetupReady() || !PassTargetReady(target)) return false;
            passRequestExpires = -1f;
            passRequestTarget = null;
            game.ShowHint("PASS", .5f);
            Vector3 lead = target.transform.position + Vector3.ProjectOnPlane(target.Body.linearVelocity, Vector3.up) * .32f;
            actionTarget = target;
            passLateralError = lateralError;
            kickDirection = Quaternion.AngleAxis(passLateralError, Vector3.up) * (lead - ball.transform.position).normalized;
            actionDirection = transform.forward;
            game.Record("pass_attempt", name, target.name, -1f, $"Assisted lead; aimErrorDegrees={lateralError:0.0}");
            BeginAction(RobotAction.Pass, "Pass", 16f / 30f, 34f / 30f);
            return true;
        }

        public void RequestShoot() => shootRequestExpires = Time.time + 1.25f;

        public bool TryStartShoot(float aimBiasDegrees = 0f)
        {
            if ((CurrentAction != RobotAction.None && CurrentAction != RobotAction.Dribble) || !BallInKickAssistRange()) return false;
            float attack = team == SoccerTeam.Orange ? 1f : -1f;
            Vector3 aimPoint = new Vector3(Mathf.Clamp(-transform.position.x * .18f, -1.2f, 1.2f), .3f, attack * (SoccerGame.PitchHalfLength + .4f));
            shotAimBias = aimBiasDegrees;
            kickDirection = Quaternion.AngleAxis(shotAimBias, Vector3.up) * (aimPoint - ball.transform.position).normalized;
            Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up);
            actionDirection = toBall.sqrMagnitude > .001f ? toBall.normalized : transform.forward;
            game.Record("shot_attempt", name, team == SoccerTeam.Orange ? "north_goal" : "south_goal", CurrentFootGap(false),
                $"visibleGap={CurrentFootGap(true):0.000}; aimErrorDegrees={aimBiasDegrees:0.0}; buffered=true");
            BeginAction(RobotAction.Shoot, "Shoot", 19f / 30f, 42f / 30f);
            return true;
        }

        bool BallInKickAssistRange()
        {
            if (!ball || ball.Body.linearVelocity.magnitude >= 8.5f) return false;
            Vector3 offset = ball.transform.position - transform.position;
            return Mathf.Abs(offset.y) < .8f && Vector3.ProjectOnPlane(offset, Vector3.up).magnitude <= 1.25f;
        }

        public bool TryStartTackle()
        {
            if (CurrentAction != RobotAction.None || Time.time < nextTackleTime || !BallReachableForTackle()) return false;
            actionDirection = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up).normalized;
            kickDirection = actionDirection;
            nextTackleTime = Time.time + 1.25f;
            BeginAction(RobotAction.Tackle, "Tackle", 14f / 30f, 32f / 30f);
            game.Record("tackle_attempt", name, "ball", CurrentFootGap(false), "Standing tackle; contact required");
            return true;
        }

        void TryAssistedTackle()
        {
            SoccerTeam opposition = team == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange;
            if (!game.TeamLikelyControlsBall(opposition)) return;
            Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up);
            if (toBall.magnitude > 1.02f || toBall.sqrMagnitude < .001f) return;
            if (Vector3.Dot(transform.forward, toBall.normalized) < .72f) return;
            if (ball.transform.position.y > .62f) return;
            TryStartTackle();
        }

        bool BallReachableForTackle()
        {
            Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up);
            return toBall.magnitude <= 1.15f && toBall.sqrMagnitude > .001f && Vector3.Dot(transform.forward, toBall.normalized) >= .55f
                && ball.transform.position.y < .7f;
        }

        public bool TryStartReceive()
        {
            if (CurrentAction != RobotAction.None || !game.IsTargetedReceiver(this)) return false;
            actionDirection = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up).normalized;
            game.Record("receive_attempt", name, "ball", CurrentFootGap(false),
                $"robotPos={transform.position}; robotVelocity={Body.linearVelocity}; facing={transform.forward}; ballVelocity={ball.Body.linearVelocity}; visibleGap={CurrentFootGap(true):0.000}");
            BeginAction(RobotAction.Receive, "Receive", 11f / 30f, 26f / 30f);
            return true;
        }

        void TryAutomaticDribble()
        {
            if (CurrentAction != RobotAction.None || (passRequestTarget && PassTargetReady(passRequestTarget)) || game.IsTargetedReceiver(this) || Time.time < nextDribbleTime || DesiredMove.sqrMagnitude < .08f || !game.RobotControlsBall(this)) return;
            actionDirection = DesiredMove.normalized;
            kickDirection = actionDirection;
            BeginAction(RobotAction.Dribble, "Dribble", 11f / 30f, 24f / 30f);
            nextDribbleTime = Time.time + .98f;
        }

        void BeginAction(RobotAction action, string state, float contactTime, float duration)
        {
            CurrentAction = action;
            actionContact = Time.time + contactTime;
            contactWindowEnd = actionContact + (action == RobotAction.Receive ? .34f : action == RobotAction.Tackle ? .17f : .13f);
            actionEnds = Time.time + duration;
            contactApplied = false;
            if (animator)
            {
                animator.speed = 1f;
                animator.CrossFade(state, .06f);
            }
        }

        void UpdateAction()
        {
            if (CurrentAction == RobotAction.None) return;
            if ((CurrentAction == RobotAction.Shoot || CurrentAction == RobotAction.Tackle) && !contactApplied)
            {
                Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up);
                if (toBall.sqrMagnitude > .001f) actionDirection = toBall.normalized;
            }
            if (awaitingReceptionConfirmation && Time.time > receptionImpulseTime)
            {
                float controlledDistance = Vector3.ProjectOnPlane(ball.transform.position - transform.position, Vector3.up).magnitude;
                if (ball.Body.linearVelocity.magnitude <= 1.5f && controlledDistance <= .85f)
                    game.ConfirmReception(this, receptionPhysicalGap, receptionVisibleGap);
                else game.Record("receiver_positioning_failure", name, "ball", receptionPhysicalGap,
                    $"Cushion did not produce bounded control; visibleGap={receptionVisibleGap:0.000}");
                awaitingReceptionConfirmation = false;
            }
            if (!contactApplied && Time.time >= actionContact && Time.time <= contactWindowEnd) TryApplyContact();
            if (!contactApplied && Time.time > contactWindowEnd)
            {
                Vector3 localBall = transform.InverseTransformPoint(ball.transform.position);
                Vector3 localFoot = rightFoot ? transform.InverseTransformPoint(rightFoot.position) : Vector3.zero;
                game.Record(CurrentAction == RobotAction.Pass ? "bad_pass_missed_contact" : "animation_contact_gap", name,
                    CurrentAction.ToString(), CurrentFootGap(false),
                    $"visibleGap={CurrentFootGap(true):0.000}; localBall={localBall}; localFoot={localFoot}");
                contactApplied = true;
            }
            if (Time.time >= actionEnds)
            {
                CurrentAction = RobotAction.None;
                actionTarget = null;
                actionDirection = Vector3.zero;
                kickDirection = Vector3.zero;
            }
        }

        float CurrentFootGap(bool useRenderedBall)
        {
            if (!rightFoot || !ball) return 99f;
            Vector3 ballCenter = useRenderedBall ? ball.RenderedCenter : ball.transform.position;
            Vector3 heel = rightFoot.position - transform.forward * .025f;
            Vector3 toe = rightFoot.position + transform.forward * FootContactLength;
            Vector3 axis = toe - heel;
            float along = Mathf.Clamp01(Vector3.Dot(ballCenter - heel, axis) / axis.sqrMagnitude);
            Vector3 closest = heel + axis * along;
            float ballContactRadius = useRenderedBall ? ball.RenderedRadius : ball.Radius;
            return Mathf.Max(0f, Vector3.Distance(closest, ballCenter) - FootContactRadius - ballContactRadius);
        }

        void TryApplyContact()
        {
            float physicalGap = CurrentFootGap(false);
            float visibleGap = CurrentFootGap(true);
            AnimatorStateInfo pose = animator ? animator.GetCurrentAnimatorStateInfo(0) : default;
            string poseDetail = $"animatorState={CurrentAction}; normalizedTime={Mathf.Repeat(pose.normalizedTime, 1f):0.000}";
            bool forgivingVisibleAction = CurrentAction == RobotAction.Shoot;
            if ((forgivingVisibleAction ? visibleGap : physicalGap) > (forgivingVisibleAction ? .10f : .05f)) return;
            contactApplied = true;
            switch (CurrentAction)
            {
                case RobotAction.Dribble:
                    ball.ApplyKick(this, actionDirection, Mathf.Max(2.1f, Vector3.ProjectOnPlane(Body.linearVelocity, Vector3.up).magnitude + .18f), .04f);
                    game.Record("dribble_contact", name, "ball", physicalGap, $"Discrete tap; visibleGap={visibleGap:0.000}; {poseDetail}");
                    break;
                case RobotAction.Pass:
                    if (actionTarget)
                    {
                        Vector3 receiveForward = Vector3.ProjectOnPlane(ball.transform.position - actionTarget.transform.position, Vector3.up).normalized;
                        if (receiveForward.sqrMagnitude < .001f) receiveForward = -kickDirection;
                        Vector3 receiveRight = Vector3.Cross(Vector3.up, receiveForward).normalized;
                        Vector3 animatedFootTarget = actionTarget.transform.position + receiveRight * .19f + receiveForward * .30f;
                        Vector3 lead = animatedFootTarget + Vector3.ProjectOnPlane(actionTarget.Body.linearVelocity, Vector3.up) * .12f;
                        kickDirection = Quaternion.AngleAxis(passLateralError, Vector3.up) * (lead - ball.transform.position).normalized;
                    }
                    ball.ApplyKick(this, kickDirection, 7.2f, .10f);
                    game.RegisterPass(this, actionTarget);
                    game.Record("pass_released", name, actionTarget ? actionTarget.name : "none", physicalGap,
                        $"Release at foot contact; requestedVelocity={ball.LastRequestedVelocity}; visibleGap={visibleGap:0.000}; passerPos={transform.position}; ballPos={ball.transform.position}; receiverPos={(actionTarget ? actionTarget.transform.position : Vector3.zero)}; {poseDetail}");
                    break;
                case RobotAction.Receive:
                    ball.Cushion(this, transform.forward);
                    nextDribbleTime = Mathf.Max(nextDribbleTime, actionEnds + .65f);
                    awaitingReceptionConfirmation = true;
                    receptionPhysicalGap = physicalGap;
                    receptionVisibleGap = visibleGap;
                    receptionImpulseTime = Time.time;
                    break;
                case RobotAction.Shoot:
                    ball.ApplyKick(this, kickDirection, 11.8f, 1.15f);
                    game.RegisterShot(this, physicalGap, visibleGap, poseDetail);
                    break;
                case RobotAction.Tackle:
                    ball.ApplyKick(this, actionDirection, Mathf.Max(3.4f, ball.Body.linearVelocity.magnitude * .45f), .08f);
                    game.Record("tackle_contact", name, "ball", physicalGap, $"Standing contact; visibleGap={visibleGap:0.000}; cooldown=1.25; {poseDetail}");
                    break;
            }
        }

        public void StopImmediately()
        {
            DesiredMove = Vector3.zero;
            inputMove = Vector3.zero;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }

        public void ClearForRefereeStop()
        {
            StopImmediately();
            CurrentAction = RobotAction.None;
            contactApplied = false;
            awaitingReceptionConfirmation = false;
            shootRequestExpires = -1f;
            passRequestExpires = -1f;
            passRequestTarget = null;
            actionTarget = null;
            actionDirection = Vector3.zero;
            kickDirection = Vector3.zero;
            reactionUntil = 0f;
            if (animator) animator.CrossFade("Idle", 0f);
            SoccerAgentAI controller = GetComponent<SoccerAgentAI>();
            if (controller) controller.ResetController();
        }

        void OnCollisionStay(Collision collision)
        {
            SoccerRobot other = collision.collider.GetComponentInParent<SoccerRobot>();
            if (!other || collision.contactCount == 0) return;
            ContactPoint contact = collision.GetContact(0);
            blockingNormal = Vector3.ProjectOnPlane(contact.normal, Vector3.up).normalized;
            blockingUntil = Time.time + .08f;
            if (Time.time >= nextBodyLog)
            {
                float headOn = Mathf.Abs(Vector3.Dot(transform.forward, blockingNormal));
                game.Record("body_contact", name, other.name, -1f, headOn > .7f ? "head_on_block" : "glancing_brush");
                nextBodyLog = Time.time + .35f;
                if (animator && CurrentAction == RobotAction.None)
                {
                    animator.CrossFade("ContactLean", .05f);
                    reactionUntil = Time.time + .8f;
                }
            }
        }
    }
}
