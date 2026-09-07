using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RoboStreetSoccer
{
    public enum MatchState { Setup, Live, GoalFreeze, KickInSetup, MatchOver, Paused }
    public enum SoccerTeam { Orange, Mint }
    public enum ControlPreset { Beginner, Advanced }
    public enum OpponentDifficulty { Gentle, Balanced }

    [Serializable]
    public sealed class DiagnosticEvent
    {
        public string type;
        public float simulationTime;
        public string actor;
        public string target;
        public Vector3 ballPosition;
        public Vector3 ballVelocity;
        public float contactGap;
        public string detail;
    }

    [DefaultExecutionOrder(-100)]
    public sealed class SoccerGame : MonoBehaviour
    {
        public const float PitchHalfWidth = 9f;
        public const float PitchHalfLength = 13f;
        public const float GoalHalfWidth = 2.1f;
        public const float CrossbarHeight = 2.25f;
        public const float GoalFrameRadius = .10f;

        [SerializeField] SoccerBall ball;
        [SerializeField] SoccerRobot playerA;
        [SerializeField] SoccerRobot playerB;
        [SerializeField] SoccerRobot opponentA;
        [SerializeField] SoccerRobot opponentB;
        [SerializeField] SoccerRobot selectedRobot;
        [SerializeField] SoccerRobot pendingReceiver;
        [SerializeField] SoccerRobot pendingPasser;

        public SoccerBall Ball => ball;
        public SoccerRobot PlayerA => playerA;
        public SoccerRobot PlayerB => playerB;
        public SoccerRobot OpponentA => opponentA;
        public SoccerRobot OpponentB => opponentB;
        public SoccerRobot SelectedRobot => selectedRobot;
        public SoccerRobot PendingReceiver => pendingReceiver;
        public SoccerRobot PendingPasser => pendingPasser;
        public MatchState State { get; private set; } = MatchState.Setup;
        public MatchState StateBeforePause { get; private set; } = MatchState.Live;
        public ControlPreset Controls { get; private set; } = ControlPreset.Beginner;
        public OpponentDifficulty Difficulty { get; private set; } = OpponentDifficulty.Gentle;
        public bool DiagnosticsVisible { get; private set; }
        public bool AcceptanceMode { get; private set; }
        public int OrangeScore { get; private set; }
        public int MintScore { get; private set; }
        public int PassReceivedCount { get; private set; }
        public int ShotContactCount { get; private set; }
        public SoccerTeam KickInTeam { get; private set; }
        public SoccerRobot KickInRobot { get; private set; }
        public int PendingPassId { get; private set; }
        public float KickInReadyAt { get; private set; }
        public string StatusText { get; private set; } = "FIRST TO 3";
        public IReadOnlyList<DiagnosticEvent> Events => events;
        public IReadOnlyList<SoccerRobot> Robots => robots;

        readonly List<DiagnosticEvent> events = new List<DiagnosticEvent>(512);
        readonly List<SoccerRobot> robots = new List<SoccerRobot>(4);
        Vector3 previousBallPosition;
        bool goalLatched;
        bool boundaryLatched;
        float selectedSpeed = 1f;
        int nextPassId;
        float pendingPassExpires;
        float statusHintExpires;
        SoccerRobot looseBallCollector;
        float looseCollectorAssignedAt;
        Vector3 looseCollectorBallPosition;
        float cornerTrapSince = -1f;
        float contestedTrapSince = -1f;

        void Awake()
        {
            Time.fixedDeltaTime = .01f;
            Time.maximumDeltaTime = .1f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            AcceptanceMode = Array.Exists(Environment.GetCommandLineArgs(), value =>
                value.StartsWith("--acceptance", StringComparison.Ordinal) || value == "--contact-acceptance-output"
                || value == "--match-acceptance-output");
        }

        void Start()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera && !mainCamera.GetComponent<SoccerCameraController>())
                mainCamera.gameObject.AddComponent<SoccerCameraController>();
            if (ball && playerA && playerB && opponentA && opponentB)
                Initialize(ball, playerA, playerB, opponentA, opponentB);
            else Debug.LogError("SOCCER_BOOTSTRAP_FAIL Missing scene references");
        }

        void Update()
        {
            if (AcceptanceMode) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(1f);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(.5f);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(.25f);
            if (Input.GetKeyDown(KeyCode.R)) ResetMatch();
            if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            if (Input.GetKeyDown(KeyCode.F1)) DiagnosticsVisible = !DiagnosticsVisible;
        }

        public void Initialize(SoccerBall targetBall, SoccerRobot orangeOne, SoccerRobot orangeTwo, SoccerRobot mintOne, SoccerRobot mintTwo)
        {
            ball = targetBall;
            playerA = orangeOne;
            playerB = orangeTwo;
            opponentA = mintOne;
            opponentB = mintTwo;
            robots.Clear();
            robots.Add(playerA); robots.Add(playerB); robots.Add(opponentA); robots.Add(opponentB);
            ball.Initialize(this);
            foreach (SoccerRobot robot in robots) robot.Initialize(this, ball);
            SelectRobot(playerA);
            ResetMatch();
        }

        public void Initialize(SoccerBall targetBall, SoccerRobot orangeOne, SoccerRobot orangeTwo)
        {
            ball = targetBall; playerA = orangeOne; playerB = orangeTwo;
        }

        public void ResetMatch()
        {
            StopAllCoroutines();
            OrangeScore = 0;
            MintScore = 0;
            PassReceivedCount = 0;
            ShotContactCount = 0;
            ResetKickoff("match_restart");
        }

        public void ResetPractice() => ResetMatch();

        void ResetKickoff(string reason)
        {
            State = MatchState.Setup;
            Time.timeScale = selectedSpeed;
            goalLatched = false;
            boundaryLatched = false;
            pendingReceiver = null;
            pendingPasser = null;
            PendingPassId = 0;
            pendingPassExpires = 0f;
            statusHintExpires = 0f;
            looseBallCollector = null;
            cornerTrapSince = -1f;
            contestedTrapSince = -1f;
            KickInRobot = null;
            playerA.ResetRobot(new Vector3(-1.35f, 0f, -5.8f), Quaternion.LookRotation(Vector3.forward));
            playerB.ResetRobot(new Vector3(2.7f, 0f, -1.4f), Quaternion.LookRotation(Vector3.forward));
            opponentA.ResetRobot(new Vector3(1.35f, 0f, 5.8f), Quaternion.LookRotation(Vector3.back));
            opponentB.ResetRobot(new Vector3(-2.7f, 0f, 2.2f), Quaternion.LookRotation(Vector3.back));
            ball.ResetBall(new Vector3(-1.35f, 0f, -5.25f));
            SelectRobot(playerA);
            previousBallPosition = ball.transform.position;
            StatusText = OrangeScore == 0 && MintScore == 0 ? "FIRST TO 3" : "KICKOFF";
            State = MatchState.Live;
            Record("game_reset", "game", reason, -1f, "Explicit dead-ball reset");
        }

        public void SelectRobot(SoccerRobot robot)
        {
            if (!robot || robot.Team != SoccerTeam.Orange) return;
            selectedRobot = robot;
            if (playerA) playerA.SetSelected(playerA == robot);
            if (playerB) playerB.SetSelected(playerB == robot);
        }

        public void ToggleControls()
        {
            Controls = Controls == ControlPreset.Beginner ? ControlPreset.Advanced : ControlPreset.Beginner;
            Record("control_preset", "game", Controls.ToString(), -1f, "Physics unchanged");
        }

        public void ToggleDifficulty()
        {
            Difficulty = Difficulty == OpponentDifficulty.Gentle ? OpponentDifficulty.Balanced : OpponentDifficulty.Gentle;
            Record("opponent_difficulty", "game", Difficulty.ToString(), -1f, "Decision timing and aim only; physics unchanged");
        }

        public SoccerRobot TeammateOf(SoccerRobot robot)
        {
            if (!robot) return null;
            if (robot == playerA) return playerB;
            if (robot == playerB) return playerA;
            if (robot == opponentA) return opponentB;
            if (robot == opponentB) return opponentA;
            return null;
        }

        public SoccerRobot ClosestRobot(SoccerTeam team, Vector3 position, SoccerRobot exclude = null)
        {
            SoccerRobot best = null;
            float bestDistance = float.MaxValue;
            foreach (SoccerRobot robot in robots)
            {
                if (!robot || robot.Team != team || robot == exclude) continue;
                float distance = (robot.transform.position - position).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = robot; }
            }
            return best;
        }

        public SoccerRobot ClosestRobot(Vector3 position)
        {
            SoccerRobot best = null;
            float bestDistance = float.MaxValue;
            foreach (SoccerRobot robot in robots)
            {
                if (!robot) continue;
                float distance = (robot.transform.position - position).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = robot; }
            }
            return best;
        }

        public SoccerRobot LooseBallCollector()
        {
            bool controlled = TeamLikelyControlsBall(SoccerTeam.Orange) || TeamLikelyControlsBall(SoccerTeam.Mint);
            if (controlled || Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up).magnitude >= .35f)
            {
                looseBallCollector = null;
                return null;
            }
            bool assignmentFresh = looseBallCollector && Time.time - looseCollectorAssignedAt < 1.25f;
            bool assignmentNearby = looseBallCollector
                && Vector3.ProjectOnPlane(looseBallCollector.transform.position - ball.transform.position, Vector3.up).magnitude < 3f
                && Vector3.ProjectOnPlane(ball.transform.position - looseCollectorBallPosition, Vector3.up).magnitude < 1f;
            if (assignmentFresh || assignmentNearby) return looseBallCollector;

            float bestDistance = float.MaxValue;
            looseBallCollector = null;
            foreach (SoccerRobot candidate in robots)
            {
                float distance = Vector3.ProjectOnPlane(candidate.transform.position - ball.transform.position, Vector3.up).sqrMagnitude;
                if (!looseBallCollector || distance < bestDistance - .0025f
                    || (Mathf.Abs(distance - bestDistance) <= .0025f && candidate.RosterIndex < looseBallCollector.RosterIndex))
                {
                    bestDistance = distance;
                    looseBallCollector = candidate;
                }
            }
            looseCollectorAssignedAt = Time.time;
            looseCollectorBallPosition = ball.transform.position;
            Record("loose_ball_collector", looseBallCollector ? looseBallCollector.name : "none", "ball", -1f,
                "Stable nearest collector; roster index breaks near ties");
            return looseBallCollector;
        }

        public void RegisterPass(SoccerRobot passer, SoccerRobot receiver)
        {
            if (pendingReceiver) CancelPendingPass("superseded_by_new_pass", passer);
            pendingPasser = passer;
            pendingReceiver = receiver;
            PendingPassId = ++nextPassId;
            pendingPassExpires = Time.time + 3.2f;
            Record("pass_registered", passer.name, receiver ? receiver.name : "none", -1f, $"passId={PendingPassId}");
            if (State == MatchState.KickInSetup && passer == KickInRobot)
            {
                State = MatchState.Live;
                StatusText = "PLAY";
                boundaryLatched = false;
                previousBallPosition = ball.transform.position;
                Record("kick_in_started", passer.name, receiver ? receiver.name : "space", -1f, "Restart became live at physical foot contact");
            }
        }

        public bool IsTargetedReceiver(SoccerRobot robot) => pendingReceiver == robot;

        public void RegisterPhysicalTouch(SoccerRobot robot, string kind)
        {
            if (!robot || !pendingReceiver) return;
            if (robot == pendingReceiver || robot == pendingPasser) return;
            bool interception = pendingPasser && robot.Team != pendingPasser.Team;
            Record(interception ? "interception" : "loose_ball_recovery", robot.name, pendingReceiver.name, -1f,
                $"passId={PendingPassId}; touch={kind}");
            CancelPendingPass(interception ? "opponent_touch" : "non_target_touch", robot);
        }

        public void CancelPendingPass(string reason, SoccerRobot actor = null)
        {
            if (!pendingReceiver) return;
            Record("pass_cancelled", actor ? actor.name : "referee", pendingReceiver.name, -1f, $"passId={PendingPassId}; reason={reason}");
            pendingReceiver = null;
            pendingPasser = null;
            PendingPassId = 0;
            pendingPassExpires = 0f;
        }

        public void ConfirmReception(SoccerRobot receiver, float physicalGap, float visibleGap)
        {
            if (State != MatchState.Live || receiver != pendingReceiver) return;
            int receivedPassId = PendingPassId;
            PassReceivedCount++;
            Record("pass_received", receiver.name, pendingPasser ? pendingPasser.name : "unknown", physicalGap,
                $"passId={receivedPassId}; physical cushion confirmed control; visibleGap={visibleGap:0.000}");
            if (receiver.Team == SoccerTeam.Orange && pendingPasser && pendingPasser.Team == SoccerTeam.Orange)
                SelectRobot(receiver);
            pendingReceiver = null;
            pendingPasser = null;
            PendingPassId = 0;
            pendingPassExpires = 0f;
        }

        public void RegisterShot(SoccerRobot robot, float physicalGap, float visibleGap, string poseDetail = "")
        {
            ShotContactCount++;
            Record("shot_contact", robot.name, robot.Team == SoccerTeam.Orange ? "north_goal" : "south_goal", physicalGap,
                $"One impulse at animated foot phase; visibleGap={visibleGap:0.000}; {poseDetail}");
        }

        public bool RobotControlsBall(SoccerRobot robot)
        {
            if (!robot || !ball) return false;
            Vector3 local = robot.transform.InverseTransformPoint(ball.transform.position);
            return local.z > -.08f && local.z < .90f && Mathf.Abs(local.x) < .58f && ball.Body.linearVelocity.magnitude < 8.5f;
        }

        public bool TeamLikelyControlsBall(SoccerTeam team)
        {
            foreach (SoccerRobot robot in robots)
                if (robot.Team == team && RobotControlsBall(robot)) return true;
            return false;
        }

        public bool CanHumanAct(SoccerRobot robot) => robot && robot.IsSelected &&
            (State == MatchState.Live || (State == MatchState.KickInSetup && robot == KickInRobot));

        void FixedUpdate()
        {
            if (statusHintExpires > 0f && Time.time >= statusHintExpires && State == MatchState.Live)
            {
                statusHintExpires = 0f;
                StatusText = "PLAY";
            }
            if (!ball || State != MatchState.Live) return;
            if (pendingReceiver && Time.time > pendingPassExpires)
                CancelPendingPass("flight_timeout");
            Vector3 position = ball.transform.position;
            float radius = ball.Radius;
            bool cornerTrap = Mathf.Abs(position.x) >= PitchHalfWidth - radius - .025f
                && Mathf.Abs(position.z) >= PitchHalfLength - radius - .025f
                && Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up).magnitude < .05f;
            if (!cornerTrap) cornerTrapSince = -1f;
            else if (cornerTrapSince < 0f) cornerTrapSince = Time.time;
            else if (!boundaryLatched && Time.time - cornerTrapSince >= 4f)
            {
                boundaryLatched = true;
                Record("game_error_recovery", "referee", "corner_trap", -1f,
                    "Physical board-clearance attempts did not release the ball within 4 simulation seconds");
                StartCoroutine(PinnedBallRecovery(position, "corner_trap"));
                return;
            }
            bool contestedTrap = Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up).magnitude < .05f
                && TeamLikelyControlsBall(SoccerTeam.Orange) && TeamLikelyControlsBall(SoccerTeam.Mint);
            if (!contestedTrap) contestedTrapSince = -1f;
            else if (contestedTrapSince < 0f) contestedTrapSince = Time.time;
            else if (!boundaryLatched && Time.time - contestedTrapSince >= 4f)
            {
                boundaryLatched = true;
                Record("game_error_recovery", "referee", "collision_cluster_trap", -1f,
                    "Opposing solid bodies pinned a stationary ball for 4 simulation seconds");
                StartCoroutine(PinnedBallRecovery(position, "collision_cluster_trap"));
                return;
            }
            if (!boundaryLatched && IsInvalidBallState(position))
            {
                boundaryLatched = true;
                Record("game_error_recovery", "referee", "ball", -1f, "Non-finite, trapped-below-floor or extreme escape state");
                StartCoroutine(RecoveryReset());
                return;
            }
            float northPlane = PitchHalfLength + radius;
            float southPlane = -PitchHalfLength - radius;
            bool northCross = previousBallPosition.z <= northPlane && position.z > northPlane;
            bool southCross = previousBallPosition.z >= southPlane && position.z < southPlane;
            float plane = northCross ? northPlane : southPlane;
            float crossingT = (northCross || southCross) && Mathf.Abs(position.z - previousBallPosition.z) > .00001f
                ? Mathf.Clamp01((plane - previousBallPosition.z) / (position.z - previousBallPosition.z)) : 1f;
            Vector3 crossing = Vector3.Lerp(previousBallPosition, position, crossingT);
            // The whole collision ball must cross inside the frame's inner edges.
            // GoalHalfWidth and CrossbarHeight locate the post/bar centrelines;
            // subtract their physical radius before applying the ball radius.
            bool validMouth = Mathf.Abs(crossing.x) + radius < GoalHalfWidth - GoalFrameRadius
                && crossing.y + radius < CrossbarHeight - GoalFrameRadius;
            if (!goalLatched && (northCross || southCross) && validMouth)
            {
                goalLatched = true;
                boundaryLatched = true;
                if (northCross) OrangeScore++; else MintScore++;
                Record("goal", northCross ? "orange" : "mint", "goal_plane", -1f, "Whole collision ball crossed valid plane");
                StartCoroutine(GoalReset(northCross ? SoccerTeam.Orange : SoccerTeam.Mint));
            }
            else if (!boundaryLatched && IsWholeBallOut(position, radius))
            {
                boundaryLatched = true;
                if (!ball.LastTouchRobot)
                {
                    Record("game_error_recovery", "referee", "unknown_last_touch", -1f, "Neutral reset; no arbitrary team fault");
                    StartCoroutine(RecoveryReset());
                }
                else BeginKickIn(position);
            }
            previousBallPosition = position;
        }

        bool IsWholeBallOut(Vector3 position, float radius)
        {
            bool side = Mathf.Abs(position.x) - radius > PitchHalfWidth + .16f;
            bool end = Mathf.Abs(position.z) - radius > PitchHalfLength + .16f;
            return side || end;
        }

        bool IsInvalidBallState(Vector3 position)
        {
            return !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)
                || position.y < -.7f || Mathf.Abs(position.x) > 18f || Mathf.Abs(position.z) > 24f;
        }

        void BeginKickIn(Vector3 exit)
        {
            SoccerTeam lastTeam = ball.LastTouchRobot.Team;
            KickInTeam = lastTeam == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange;
            string lastTouch = ball.LastTouchName;
            State = MatchState.KickInSetup;
            StatusText = $"KICK-IN: {KickInTeam.ToString().ToUpperInvariant()}";
            pendingPasser = null;
            pendingReceiver = null;
            foreach (SoccerRobot robot in robots) robot.StopImmediately();
            // Keep the ball and the robot behind it inside the physical boards as
            // one geometric pair. The old .48 m inset left a .62 m back-step
            // outside the pitch and made some AI restarts impossible.
            const float restartBallInset = 1.12f;
            bool crossedSide = Mathf.Abs(exit.x) > PitchHalfWidth;
            bool crossedEnd = Mathf.Abs(exit.z) > PitchHalfLength;
            Vector3 spot = new Vector3(
                Mathf.Clamp(exit.x, -PitchHalfWidth + restartBallInset, PitchHalfWidth - restartBallInset), 0f,
                Mathf.Clamp(exit.z, -PitchHalfLength + restartBallInset, PitchHalfLength - restartBallInset));
            if (crossedSide) spot.x = Mathf.Sign(exit.x) * (PitchHalfWidth - restartBallInset);
            if (crossedEnd) spot.z = Mathf.Sign(exit.z) * (PitchHalfLength - restartBallInset);
            Vector3 inward = new Vector3(crossedSide ? -Mathf.Sign(exit.x) : 0f, 0f,
                crossedEnd ? -Mathf.Sign(exit.z) : 0f);
            if (inward.sqrMagnitude < .1f) inward = KickInTeam == SoccerTeam.Orange ? Vector3.forward : Vector3.back;
            inward.Normalize();
            KickInRobot = ClosestRobot(KickInTeam, spot);
            SoccerRobot mate = TeammateOf(KickInRobot);
            Vector3 kickerSpot = spot - inward * .62f;
            KickInRobot.ResetRobot(kickerSpot, Quaternion.LookRotation(inward));
            ball.ResetBall(spot, false);
            if (mate)
            {
                Vector3 mateSpot = spot + inward * 3.2f;
                mateSpot.x = Mathf.Clamp(mateSpot.x, -7.8f, 7.8f); mateSpot.z = Mathf.Clamp(mateSpot.z, -11.5f, 11.5f);
                mate.ResetRobot(mateSpot, Quaternion.LookRotation(inward));
            }
            SoccerTeam defending = KickInTeam == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange;
            foreach (SoccerRobot defender in robots)
            {
                if (defender.Team != defending) continue;
                Vector3 away = Vector3.ProjectOnPlane(defender.transform.position - spot, Vector3.up);
                if (away.magnitude < 2.6f)
                {
                    Vector3 safe = spot + (away.sqrMagnitude > .01f ? away.normalized : -inward) * 2.8f;
                    safe.x = Mathf.Clamp(safe.x, -8.15f, 8.15f);
                    safe.z = Mathf.Clamp(safe.z, -12.15f, 12.15f);
                    Vector3 clearance = Vector3.ProjectOnPlane(safe - spot, Vector3.up);
                    if (clearance.magnitude < 2.25f)
                    {
                        safe = spot - inward * 2.8f;
                        safe.x = Mathf.Clamp(safe.x, -8.15f, 8.15f);
                        safe.z = Mathf.Clamp(safe.z, -12.15f, 12.15f);
                    }
                    defender.ResetRobot(safe, Quaternion.LookRotation(-inward));
                }
            }
            if (KickInTeam == SoccerTeam.Orange) SelectRobot(KickInRobot);
            KickInReadyAt = Time.time + (Difficulty == OpponentDifficulty.Gentle ? .95f : .62f);
            previousBallPosition = ball.transform.position;
            Record("out_of_bounds", lastTouch, $"exit=({exit.x:0.00},{exit.z:0.00})", -1f, "Whole collision ball crossed boundary");
            Record("kick_in_awarded", KickInTeam.ToString(), KickInRobot.name, -1f,
                $"Awarded against last physical touch; ballSpot=({spot.x:0.00},{spot.z:0.00}); kickerSpot=({kickerSpot.x:0.00},{kickerSpot.z:0.00})");
        }

        IEnumerator GoalReset(SoccerTeam scorer)
        {
            State = MatchState.GoalFreeze;
            StatusText = $"GOAL — {scorer.ToString().ToUpperInvariant()}";
            foreach (SoccerRobot robot in robots) robot.StopImmediately();
            yield return new WaitForSeconds(.8f);
            if (OrangeScore >= 3 || MintScore >= 3)
            {
                State = MatchState.MatchOver;
                StatusText = $"{scorer.ToString().ToUpperInvariant()} WINS — FIRST TO 3";
                yield break;
            }
            int orange = OrangeScore, mint = MintScore;
            ResetKickoff("goal_restart");
            OrangeScore = orange; MintScore = mint;
        }

        IEnumerator RecoveryReset()
        {
            State = MatchState.GoalFreeze;
            yield return new WaitForSeconds(.35f);
            int orange = OrangeScore, mint = MintScore;
            ResetKickoff("collision_bug_recovery");
            OrangeScore = orange; MintScore = mint;
        }

        IEnumerator PinnedBallRecovery(Vector3 trappedPosition, string reason)
        {
            State = MatchState.GoalFreeze;
            StatusText = "REFEREE DROP — BALL UNSTUCK";
            CancelPendingPass(reason);
            foreach (SoccerRobot robot in robots) robot.ClearForRefereeStop();
            yield return new WaitForSeconds(.3f);
            Vector3 safe = new Vector3(Mathf.Clamp(trappedPosition.x, -PitchHalfWidth + 2.4f, PitchHalfWidth - 2.4f), 0f,
                Mathf.Clamp(trappedPosition.z, -PitchHalfLength + 2.4f, PitchHalfLength - 2.4f));
            int relocated = 0;
            foreach (SoccerRobot robot in robots)
            {
                Vector3 separation = Vector3.ProjectOnPlane(robot.transform.position - safe, Vector3.up);
                if (separation.magnitude >= 1.25f) continue;
                float angle = 45f + robot.RosterIndex * 90f;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                Vector3 playerSpot = safe + direction * 1.8f;
                playerSpot.x = Mathf.Clamp(playerSpot.x, -8.1f, 8.1f);
                playerSpot.z = Mathf.Clamp(playerSpot.z, -12.1f, 12.1f);
                robot.ResetRobot(playerSpot, Quaternion.LookRotation(Vector3.ProjectOnPlane(safe - playerSpot, Vector3.up)));
                relocated++;
            }
            ball.ResetBall(safe);
            looseBallCollector = null;
            cornerTrapSince = -1f;
            contestedTrapSince = -1f;
            previousBallPosition = ball.transform.position;
            boundaryLatched = false;
            Physics.SyncTransforms();
            State = MatchState.Live;
            StatusText = "PLAY";
            Record("corner_trap_recovered", "referee", "neutral_drop", -1f,
                $"reason={reason}; safeSpot=({safe.x:0.00},{safe.z:0.00}); score preserved; relocatedPlayers={relocated}; actions/pass/last-touch cleared");
        }

        public void SetSpeed(float speed)
        {
            selectedSpeed = Mathf.Clamp(speed, .25f, 1f);
            if (State != MatchState.Paused) Time.timeScale = selectedSpeed;
            Record("practice_speed", "game", selectedSpeed.ToString("0.00"), -1f, "Fixed simulation step remains 0.01 seconds");
        }

        public float CurrentSpeed => selectedSpeed;

        public void ShowHint(string message, float simulationSeconds)
        {
            if (State != MatchState.Live) return;
            StatusText = message;
            statusHintExpires = Time.time + Mathf.Max(.2f, simulationSeconds);
        }

        public void TogglePause()
        {
            if (State == MatchState.Paused)
            {
                State = StateBeforePause;
                Time.timeScale = selectedSpeed;
                if (State == MatchState.Live) StatusText = "PLAY";
            }
            else if (State == MatchState.Live || State == MatchState.KickInSetup)
            {
                StateBeforePause = State;
                State = MatchState.Paused;
                Time.timeScale = 0f;
                StatusText = "PAUSED";
            }
        }

        public void Record(string type, string actor, string target, float contactGap = -1f, string detail = "")
        {
            DiagnosticEvent item = new DiagnosticEvent
            {
                type = type,
                simulationTime = Time.time,
                actor = actor ?? "",
                target = target ?? "",
                ballPosition = ball ? ball.transform.position : Vector3.zero,
                ballVelocity = ball && ball.Body ? ball.Body.linearVelocity : Vector3.zero,
                contactGap = contactGap,
                detail = detail ?? ""
            };
            events.Add(item);
            // Acceptance checks keep stable event indices for the duration of a run.
            // Ordinary play remains bounded so a long-running match cannot grow memory.
            if (!AcceptanceMode && events.Count > 800) events.RemoveAt(0);
            Debug.Log($"SOCCER_EVENT {type} actor={actor} target={target} gap={contactGap:0.000} {detail}");
        }
    }
}
