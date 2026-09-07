using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoboStreetSoccer
{
    [Serializable]
    public sealed class MatchAcceptanceReceipt
    {
        public bool passed;
        public string failure;
        public float practiceSpeed;
        public float fixedDeltaTime;
        public bool beginnerWasDefault;
        public bool gentleWasDefault;
        public bool advancedWasSelectable;
        public bool balancedWasSelectable;
        public int robotCount;
        public int passReleased;
        public int passReceived;
        public int normalLateralPasses;
        public int shotContacts;
        public int tackleContacts;
        public int bodyContacts;
        public int goals;
        public int kickInsAwarded;
        public int kickInsStarted;
        public int goalPlaneEdgeCasesPassed;
        public int interceptions;
        public float maximumCreditedPhysicalGap;
        public float maximumCreditedVisibleGap;
        public float freePlayBallTravel;
        public float freePlayStuckSeconds;
        public float freePlayLongestLiveStuckSeconds;
        public float freePlayDeadBallSeconds;
        public float freePlaySeconds;
        public int freePlayPassAttempts;
        public int freePlayShots;
        public int freePlayTackles;
        public int freePlayGoals;
        public int freePlayOrangePasses;
        public int freePlayMintPasses;
        public int freePlayOrangeShots;
        public int freePlayMintShots;
        public int freePlayOrangeTouches;
        public int freePlayMintTouches;
        public string freePlayDifficultyProfile;
        public float maximumUncorrectedStanceDrift;
        public float maximumResidualStanceDrift;
        public int configuredFootPlantCount;
        public float longestContinuousPlantSeconds;
        public int plantAcquisitionCount;
        public int locomotionPlantAcquisitionCount;
        public float longestContinuousLocomotionPlantSeconds;
        public int actionPlantAcquisitionCount;
        public float longestContinuousActionPlantSeconds;
        public float maximumLocomotionResidualDrift;
        public float maximumActionResidualDrift;
        public string maximumActionResidualName;
        public float maximumActionResidualNormalizedTime;
        public List<DiagnosticEvent> events;
    }

    [DefaultExecutionOrder(-50)]
    public sealed class SoccerAcceptanceRunner : MonoBehaviour
    {
        SoccerGame game;
        SoccerAgentAI[] agents;
        string outputPath;
        string captureDirectory;
        int phase;
        float phaseStarted;
        float suiteStarted;
        bool finished;
        bool negativeTacklePassed;
        int configuredGoals;
        int freePlayEventStart;
        float freePlayStarted;
        float freePlayTravel;
        float freePlayStuck;
        float currentLiveStuck;
        float longestLiveStuck;
        float freePlayDeadBall;
        Vector3 previousFreeBall;
        MatchState previousFreeState;
        bool defaultBeginner;
        bool defaultGentle;
        bool advancedSelectable;
        bool balancedSelectable;
        float nextReceiveTrace;
        float nextPassSetupTrace;
        int goalFixtureStart;
        readonly List<string> failures = new List<string>();
        bool kickInLaunched;
        bool chainPassRequested;
        bool chainShotRequested;
        int chainShotAttemptBaseline;
        int chainShotContactBaseline;
        int kickInCase;
        int kickInAwardBaseline;
        int kickInStartedBaseline;
        float kickInCaseStarted;
        bool lateralPassRequested;
        int lateralPassBaseline;
        int goalEdgeCase;
        bool goalEdgeLaunched;
        int goalEdgeGoalBaseline;
        int goalEdgeKickInBaseline;
        int goalEdgeFrameBaseline;
        int goalEdgeRecoveryBaseline;
        float goalEdgeCaseStarted;
        int goalPlaneEdgeCasesPassed;
        bool gentleOnlyExhibition;

        void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int outputIndex = Array.IndexOf(args, "--match-acceptance-output");
            if (outputIndex < 0) outputIndex = Array.IndexOf(args, "--acceptance-output");
            if (outputIndex < 0 || outputIndex + 1 >= args.Length) { enabled = false; return; }
            outputPath = args[outputIndex + 1];
            int speedIndex = Array.IndexOf(args, "--practice-speed");
            float speed = 1f;
            if (speedIndex >= 0 && speedIndex + 1 < args.Length)
                float.TryParse(args[speedIndex + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out speed);
            int captureIndex = Array.IndexOf(args, "--capture-directory");
            if (captureIndex >= 0 && captureIndex + 1 < args.Length) captureDirectory = args[captureIndex + 1];
            gentleOnlyExhibition = Array.Exists(args, value => value == "--gentle-only");
            game = FindAnyObjectByType<SoccerGame>();
            agents = FindObjectsByType<SoccerAgentAI>();
            defaultBeginner = game.Controls == ControlPreset.Beginner;
            defaultGentle = game.Difficulty == OpponentDifficulty.Gentle;
            game.ToggleControls();
            advancedSelectable = game.Controls == ControlPreset.Advanced;
            game.ToggleControls();
            game.ToggleDifficulty();
            balancedSelectable = game.Difficulty == OpponentDifficulty.Balanced;
            game.ToggleDifficulty();
            SetAgents(false);
            SoccerAgentAI receiverAgent = game.PlayerB.GetComponent<SoccerAgentAI>();
            if (receiverAgent) receiverAgent.enabled = true;
            game.SetSpeed(speed);
            game.ResetMatch();
            foreach (SoccerFootPlant plant in FindObjectsByType<SoccerFootPlant>()) plant.ResetTelemetry();
            suiteStarted = phaseStarted = Time.time;
            Debug.Log($"SOCCER_MATCH_ACCEPTANCE start speed={speed:0.00} output={outputPath}");
        }

        void FixedUpdate()
        {
            if (!game || finished) return;
            if (Time.time - suiteStarted > 155f) { Finish(false, $"Suite timed out in phase {phase}"); return; }
            switch (phase)
            {
                case 0: RunChainDribble(); break;
                case 1: RunChainPass(); break;
                case 2: WaitForReception(); break;
                case 3: RunChainShot(); break;
                case 4: RunLateralPassFixture(); break;
                case 5: RunGoalPlaneEdgeFixtures(); break;
                case 6: RunFirstToThreeFixture(); break;
                case 7: RunKickInFixture(); break;
                case 8: RunTackleFixture(); break;
                case 9: RunHeadOnFixture(); break;
                case 10: RunGlancingFixture(); break;
                case 11: RunFreePlay(); break;
            }
        }

        void RunChainDribble()
        {
            if (Time.time - phaseStarted < .05f)
            {
                game.OpponentA.ResetRobot(new Vector3(7f, 0f, 8f), Quaternion.LookRotation(Vector3.back));
                game.OpponentB.ResetRobot(new Vector3(-7f, 0f, 8f), Quaternion.LookRotation(Vector3.back));
            }
            game.PlayerA.SetMove(Vector3.forward);
            if (Time.time - phaseStarted > 1.35f) Advance();
        }

        void RunChainPass()
        {
            game.PlayerA.SetMove(Vector3.forward);
            if (Time.time >= nextPassSetupTrace)
            {
                game.Record("pass_setup_tracking", game.PlayerA.name, game.PlayerB.name, game.PlayerA.PhysicalFootGap,
                    $"passerPos={game.PlayerA.transform.position}; passerVelocity={game.PlayerA.Body.linearVelocity}; receiverPos={game.PlayerB.transform.position}; receiverVelocity={game.PlayerB.Body.linearVelocity}; ballPos={game.Ball.transform.position}; ballVelocity={game.Ball.Body.linearVelocity}; forwardOutlet={(game.PlayerB.transform.position.z - game.Ball.transform.position.z):0.000}");
                nextPassSetupTrace = Time.time + .25f;
            }
            if (!chainPassRequested && Time.time - phaseStarted > .25f)
            {
                chainPassRequested = true;
                game.PlayerA.RequestPass(game.PlayerB);
            }
            if (Count("pass_attempt") > 0) Advance();
            else if (Time.time - phaseStarted > 4.5f) Finish(false, "Single buffered request could not begin forward diagonal animated pass fixture");
        }

        void WaitForReception()
        {
            TraceReceive(game.PlayerB);
            if (Count("pass_received") > 0)
            {
                SoccerAgentAI receiverAgent = game.PlayerB.GetComponent<SoccerAgentAI>();
                if (receiverAgent) receiverAgent.enabled = false;
                Capture("match-receive");
                CaptureClose("match-receive-close", game.PlayerB.transform.position);
                chainShotRequested = false;
                chainShotAttemptBaseline = Count("shot_attempt");
                chainShotContactBaseline = Count("shot_contact");
                Advance();
            }
            else if (Time.time - phaseStarted > 3.5f)
            {
                failures.Add("Pass did not reach physical reception");
                game.CancelPendingPass("acceptance_fixture_timeout");
                SoccerAgentAI receiverAgent = game.PlayerB.GetComponent<SoccerAgentAI>();
                if (receiverAgent) receiverAgent.enabled = false;
                game.ResetMatch();
                game.PlayerA.ResetRobot(new Vector3(0f, 0f, -5f), Quaternion.LookRotation(Vector3.forward));
                game.Ball.ResetBall(new Vector3(0f, 0f, -4.42f));
                chainShotRequested = false;
                chainShotAttemptBaseline = Count("shot_attempt");
                chainShotContactBaseline = Count("shot_contact");
                Advance();
            }
        }

        void TraceReceive(SoccerRobot receiver)
        {
            if (!game.IsTargetedReceiver(receiver)) return;
            Vector3 velocity = Vector3.ProjectOnPlane(game.Ball.Body.linearVelocity, Vector3.up);
            Vector3 toBall = Vector3.ProjectOnPlane(game.Ball.transform.position - receiver.transform.position, Vector3.up);
            Vector3 relative = velocity - Vector3.ProjectOnPlane(receiver.Body.linearVelocity, Vector3.up);
            float closing = toBall.sqrMagnitude > .001f
                ? Mathf.Max(.25f, -Vector3.Dot(relative, toBall.normalized))
                : velocity.magnitude;
            float arrival = toBall.magnitude / closing;
            if (Time.time >= nextReceiveTrace)
            {
                Vector3 relativePosition = Vector3.ProjectOnPlane(game.Ball.transform.position - receiver.transform.position, Vector3.up);
                Vector3 relativeVelocity = velocity - Vector3.ProjectOnPlane(receiver.Body.linearVelocity, Vector3.up);
                float closestTime = relativeVelocity.sqrMagnitude > .0001f
                    ? Mathf.Clamp(-Vector3.Dot(relativePosition, relativeVelocity) / relativeVelocity.sqrMagnitude, 0f, 1.2f) : 0f;
                float closestDistance = (relativePosition + relativeVelocity * closestTime).magnitude;
                game.Record("receive_tracking", receiver.name, "ball", receiver.PhysicalFootGap,
                    $"robotPos={receiver.transform.position}; robotVelocity={receiver.Body.linearVelocity}; facing={receiver.transform.forward}; ballVelocity={game.Ball.Body.linearVelocity}; arrival={arrival:0.000}; closestTime={closestTime:0.000}; closestDistance={closestDistance:0.000}; visibleGap={receiver.VisibleFootGap:0.000}");
                nextReceiveTrace = Time.time + .1f;
            }
        }

        void RunChainShot()
        {
            SoccerRobot shooter = game.SelectedRobot;
            Vector3 carry = Vector3.ProjectOnPlane(game.Ball.Body.linearVelocity, Vector3.up);
            shooter.SetMove(carry.sqrMagnitude > .01f ? carry.normalized : Vector3.forward);
            if (!chainShotRequested && Time.time - phaseStarted > .05f)
            {
                chainShotRequested = true;
                shooter.RequestShoot();
                game.Record("single_shoot_request", shooter.name, "buffered_after_receive", -1f,
                    "One input event; expiry uses simulation time at every practice speed");
            }
            if (Count("shot_contact") > chainShotContactBaseline)
            {
                int newAttempts = Count("shot_attempt") - chainShotAttemptBaseline;
                int newContacts = Count("shot_contact") - chainShotContactBaseline;
                if (newAttempts != 1 || newContacts != 1)
                    failures.Add($"Single buffered shot produced attempts={newAttempts}, contacts={newContacts}");
                else game.Record("single_shoot_verified", shooter.name, "one_contact", shooter.PhysicalFootGap,
                    "One request produced one animated contact");
                Capture("match-shot-contact");
                CaptureClose("match-shot-contact-close", shooter.transform.position);
                ConfigureLateralPassFixture();
                Advance();
            }
            else if (Time.time - phaseStarted > 3f)
            {
                failures.Add("Buffered assisted shot missed its visible contact window");
                ConfigureLateralPassFixture();
                Advance();
            }
        }

        void ConfigureLateralPassFixture()
        {
            game.ResetMatch();
            game.PlayerA.ResetRobot(new Vector3(0f, 0f, -2f), Quaternion.LookRotation(Vector3.right));
            game.PlayerB.ResetRobot(new Vector3(3f, 0f, -2f), Quaternion.LookRotation(Vector3.left));
            game.Ball.ResetBall(new Vector3(.45f, 0f, -2f));
            Physics.SyncTransforms();
            lateralPassRequested = false;
            lateralPassBaseline = Count("pass_released");
        }

        void RunLateralPassFixture()
        {
            if (!lateralPassRequested && Time.time - phaseStarted > .10f)
            {
                lateralPassRequested = true;
                game.PlayerA.RequestPass(game.PlayerB);
            }
            if (Count("pass_released") > lateralPassBaseline)
            {
                game.Record("normal_lateral_pass_verified", game.PlayerA.name, game.PlayerB.name,
                    game.PlayerA.PhysicalFootGap, "Normal live-play pass across pitch after facing receiver");
                game.ResetMatch();
                ConfigureGoalPlaneEdgeFixtures();
                Advance();
            }
            else if (Time.time - phaseStarted > 3.5f)
            {
                failures.Add("Normal lateral pass did not release at physical foot contact");
                game.ResetMatch();
                ConfigureGoalPlaneEdgeFixtures();
                Advance();
            }
        }

        void ConfigureGoalPlaneEdgeFixtures()
        {
            goalEdgeCase = 0;
            goalEdgeLaunched = false;
            goalPlaneEdgeCasesPassed = 0;
        }

        void RunGoalPlaneEdgeFixtures()
        {
            if (goalEdgeCase >= 3)
            {
                game.ResetMatch();
                configuredGoals = 0;
                goalFixtureStart = Count("goal");
                Advance();
                return;
            }

            if (!goalEdgeLaunched)
            {
                game.ResetMatch();
                goalEdgeGoalBaseline = Count("goal");
                goalEdgeKickInBaseline = Count("kick_in_awarded");
                goalEdgeFrameBaseline = Count("frame_rebound");
                goalEdgeRecoveryBaseline = Count("game_error_recovery");
                goalEdgeCaseStarted = Time.time;
                goalEdgeLaunched = true;
                if (goalEdgeCase == 0)
                {
                    // A ball wholly above the crossbar must be an ordinary end exit,
                    // awarded against the last touch, never a goal or error escape.
                    game.Ball.ResetBallAtPosition(new Vector3(0f, 2.55f, 12.35f));
                    game.Ball.ApplyKick(game.PlayerA, Vector3.forward, 11f, .05f);
                    game.Record("goal_edge_fixture", "Orange One", "over_crossbar", -1f, "Whole ball above frame inner edge");
                }
                else if (goalEdgeCase == 1)
                {
                    // This diagonal line meets the north post's inner face. The
                    // physical frame must rebound it and the scorer must remain zero.
                    game.Ball.ResetBallAtPosition(new Vector3(SoccerGame.GoalHalfWidth - .20f, .55f, 12.20f));
                    game.Ball.ApplyKick(game.PlayerA, (Vector3.forward + Vector3.right * .04f).normalized, 13f, .03f);
                    game.Record("goal_edge_fixture", "Orange One", "post_graze", -1f, "Physical post/collision radius overlap");
                }
                else
                {
                    // With no physical last touch, an out ball is a logged neutral
                    // recovery. It must never invent a player/team fault.
                    game.Ball.ResetBallAtPosition(new Vector3(9.50f, .95f, 0f));
                    game.Record("goal_edge_fixture", "referee", "unknown_last_touch", -1f, "Neutral recovery required");
                }
                return;
            }

            bool passed = false;
            if (goalEdgeCase == 0)
                passed = game.State == MatchState.KickInSetup && Count("kick_in_awarded") > goalEdgeKickInBaseline
                    && Count("goal") == goalEdgeGoalBaseline;
            else if (goalEdgeCase == 1)
                passed = Count("frame_rebound") > goalEdgeFrameBaseline && Count("goal") == goalEdgeGoalBaseline;
            else
                passed = Count("game_error_recovery") > goalEdgeRecoveryBaseline
                    && Count("kick_in_awarded") == goalEdgeKickInBaseline && Count("goal") == goalEdgeGoalBaseline;

            if (passed)
            {
                goalPlaneEdgeCasesPassed++;
                AdvanceGoalEdgeCase();
            }
            else if (Time.time - goalEdgeCaseStarted > (goalEdgeCase == 2 ? .8f : 1.6f))
            {
                string label = goalEdgeCase == 0 ? "over-crossbar exit" : goalEdgeCase == 1 ? "post-grazing rebound" : "unknown-touch neutral recovery";
                failures.Add($"Goal-plane edge fixture failed: {label}");
                AdvanceGoalEdgeCase();
            }
        }

        void AdvanceGoalEdgeCase()
        {
            game.ResetMatch();
            goalEdgeCase++;
            goalEdgeLaunched = false;
        }

        void RunFirstToThreeFixture()
        {
            if (game.State == MatchState.MatchOver)
            {
                if (game.OrangeScore != 3 || Count("goal") - goalFixtureStart != 3)
                    failures.Add("First-to-three or goal latch fixture failed");
                game.ResetMatch();
                kickInLaunched = false;
                kickInCase = 0;
                Advance();
                return;
            }
            if (Time.time - phaseStarted > 12f)
            {
                failures.Add("First-to-three fixture timed out");
                game.ResetMatch();
                kickInLaunched = false;
                kickInCase = 0;
                Advance();
                return;
            }
            if (game.State != MatchState.Live) return;
            int fixtureGoals = Count("goal") - goalFixtureStart;
            if (configuredGoals >= 3 || configuredGoals != fixtureGoals) return;
            configuredGoals++;
            game.Ball.ResetBall(new Vector3(.1f * configuredGoals, 0f, 12.25f));
            game.Ball.ApplyKick(game.PlayerA, Vector3.forward, 11.2f, .05f);
        }

        void RunKickInFixture()
        {
            if (kickInCase >= 8)
            {
                SetAgents(false);
                ConfigureTackleFixture();
                Advance();
                return;
            }

            bool aiRestart = kickInCase < 4;
            SoccerRobot toucher = kickInCase < 4 ? game.PlayerA : game.OpponentA;
            SoccerTeam expectedAward = toucher.Team == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange;
            if (!kickInLaunched && game.State == MatchState.Live)
            {
                SetAgents(aiRestart);
                kickInAwardBaseline = Count("kick_in_awarded");
                kickInStartedBaseline = Count("kick_in_started");
                kickInCaseStarted = Time.time;
                kickInLaunched = true;
                int boundary = kickInCase % 4;
                Vector3 position = boundary == 0 ? new Vector3(8.72f, .95f, 3f)
                    : boundary == 1 ? new Vector3(-8.72f, .95f, -3f)
                    : boundary == 2 ? new Vector3(5f, .95f, 12.72f)
                    : new Vector3(-5f, .95f, -12.72f);
                Vector3 direction = boundary == 0 ? Vector3.right : boundary == 1 ? Vector3.left
                    : boundary == 2 ? Vector3.forward : Vector3.back;
                game.Record("kick_in_fixture", toucher.name, aiRestart ? "ai_restart" : "manual_restart", -1f,
                    $"case={kickInCase}; boundary={boundary}");
                game.Ball.ResetBallAtPosition(position);
                game.Ball.ApplyKick(toucher, direction, 8f, .05f);
                return;
            }
            if (game.State == MatchState.KickInSetup)
            {
                if (game.KickInTeam != expectedAward)
                {
                    failures.Add($"Kick-in case {kickInCase} was not awarded against {toucher.Team} last touch");
                    AdvanceKickInCase();
                }
                else if (!aiRestart && Time.time >= game.KickInReadyAt)
                    game.KickInRobot.TryStartPass(game.TeammateOf(game.KickInRobot));
                return;
            }
            if (Count("kick_in_started") > kickInStartedBaseline)
            {
                AdvanceKickInCase();
            }
            else if (Time.time - kickInCaseStarted > 2.8f)
            {
                string stage = Count("kick_in_awarded") > kickInAwardBaseline ? "restart contact" : "award";
                failures.Add($"Kick-in case {kickInCase} timed out before {stage}");
                AdvanceKickInCase();
            }
        }

        void AdvanceKickInCase()
        {
            SetAgents(false);
            game.ResetMatch();
            kickInCase++;
            kickInLaunched = false;
        }

        void ConfigureTackleFixture()
        {
            game.ResetMatch();
            game.PlayerA.ResetRobot(new Vector3(5f, 0f, -5f), Quaternion.LookRotation(Vector3.forward));
            game.Ball.ResetBall(Vector3.zero);
            negativeTacklePassed = !game.PlayerA.TryStartTackle();
            game.PlayerA.ResetRobot(new Vector3(0f, 0f, -.64f), Quaternion.LookRotation(Vector3.forward));
            game.Ball.ResetBall(Vector3.zero);
        }

        void RunTackleFixture()
        {
            if (!negativeTacklePassed)
            {
                failures.Add("Out-of-range tackle incorrectly started");
                negativeTacklePassed = true;
            }
            game.PlayerA.SetMove(Vector3.forward * .12f);
            game.PlayerA.TryStartTackle();
            if (Count("tackle_contact") > 0)
            {
                Capture("match-tackle-contact");
                CaptureClose("match-tackle-contact-close", game.PlayerA.transform.position);
                ConfigureHeadOn();
                Advance();
            }
            else if (Time.time - phaseStarted > 2.2f)
            {
                failures.Add("Reachable standing tackle did not contact ball");
                ConfigureHeadOn();
                Advance();
            }
        }

        void ConfigureHeadOn()
        {
            game.ResetMatch();
            game.PlayerA.ResetRobot(new Vector3(0f, 0f, -1.2f), Quaternion.LookRotation(Vector3.forward));
            game.OpponentA.ResetRobot(new Vector3(0f, 0f, 1.2f), Quaternion.LookRotation(Vector3.back));
            game.Ball.ResetBall(new Vector3(7f, 0f, 0f));
            Physics.SyncTransforms();
        }

        void RunHeadOnFixture()
        {
            game.PlayerA.SetMove(Vector3.forward);
            game.OpponentA.SetMove(Vector3.back);
            if (Time.time - phaseStarted > .62f && Time.time - phaseStarted < .64f) Capture("match-contact-lean");
            if (Time.time - phaseStarted > .62f && Time.time - phaseStarted < .64f)
                CaptureClose("match-contact-lean-close", (game.PlayerA.transform.position + game.OpponentA.transform.position) * .5f);
            if (Time.time - phaseStarted > 1.45f)
            {
                game.PlayerA.ResetRobot(new Vector3(-.32f, 0f, -1.2f), Quaternion.LookRotation(Vector3.forward));
                game.PlayerB.ResetRobot(new Vector3(.32f, 0f, 1.2f), Quaternion.LookRotation(Vector3.back));
                Physics.SyncTransforms();
                Advance();
            }
        }

        void RunGlancingFixture()
        {
            game.PlayerA.SetMove(Vector3.forward);
            game.PlayerB.SetMove(Vector3.back);
            if (Time.time - phaseStarted > 1.35f)
            {
                StartFreePlay();
                Advance();
            }
        }

        void StartFreePlay()
        {
            game.ResetMatch();
            SetAgents(true);
            freePlayEventStart = game.Events.Count;
            freePlayStarted = Time.time;
            previousFreeBall = game.Ball.transform.position;
            previousFreeState = game.State;
            freePlayTravel = 0f;
            freePlayStuck = 0f;
            currentLiveStuck = 0f;
            longestLiveStuck = 0f;
            freePlayDeadBall = 0f;
        }

        void RunFreePlay()
        {
            Vector3 now = game.Ball.transform.position;
            // Count only continuous live physics travel. Referee placements and
            // kick-in setup movement must never satisfy the activity gate.
            if (game.State == MatchState.Live && previousFreeState == MatchState.Live)
                freePlayTravel += Vector3.ProjectOnPlane(now - previousFreeBall, Vector3.up).magnitude;
            previousFreeBall = now;
            previousFreeState = game.State;
            if (game.State == MatchState.Live && game.Ball.Body.linearVelocity.magnitude < .08f)
            {
                freePlayStuck += Time.fixedDeltaTime;
                currentLiveStuck += Time.fixedDeltaTime;
                longestLiveStuck = Mathf.Max(longestLiveStuck, currentLiveStuck);
            }
            else currentLiveStuck = 0f;
            if (game.State != MatchState.Live) freePlayDeadBall += Time.fixedDeltaTime;
            float elapsed = Time.time - freePlayStarted;
            if (elapsed > 1f && elapsed < 1.02f) Capture("match-default-four-robot-play");
            if (!gentleOnlyExhibition && elapsed > 7f && game.Difficulty == OpponentDifficulty.Gentle) game.ToggleDifficulty();
            if (game.State == MatchState.MatchOver) game.ResetMatch();
            if (elapsed < 60f) return;

            int freeActions = CountFrom("pass_attempt", freePlayEventStart) + CountFrom("shot_attempt", freePlayEventStart)
                + CountFrom("tackle_attempt", freePlayEventStart);
            StanceMetrics(out float uncorrectedDrift, out float residualDrift, out int configuredPlants);
            if (!defaultBeginner || !defaultGentle) failures.Add("Beginner/Gentle defaults changed");
            if (!advancedSelectable || !balancedSelectable) failures.Add("Advanced/Balanced selection did not round-trip");
            if (game.Robots.Count != 4) failures.Add($"Expected 4 robots; found {game.Robots.Count}");
            if (configuredPlants != 4) failures.Add($"Expected 4 configured foot plants; found {configuredPlants}");
            if (residualDrift > .03f) failures.Add($"Foot-plant residual exceeded 3 cm: {residualDrift:0.0000} m");
            if (!negativeTacklePassed) failures.Add("Out-of-range tackle incorrectly started");
            if (Count("pass_received") == 0) failures.Add("No physical pass reception");
            if (Count("normal_lateral_pass_verified") == 0) failures.Add("No verified normal lateral pass");
            if (Count("shot_contact") == 0) failures.Add("No animated shot contact");
            if (Count("tackle_contact") == 0) failures.Add("No physical tackle contact");
            if (Count("body_contact") == 0) failures.Add("No solid body contact");
            if (Count("goal") < 3) failures.Add("First-to-three goal fixture incomplete");
            if (goalPlaneEdgeCasesPassed < 3) failures.Add($"Goal-plane edge coverage incomplete: {goalPlaneEdgeCasesPassed}/3");
            if (Count("kick_in_awarded") < 8 || Count("kick_in_started") < 8)
                failures.Add($"Kick-in boundary coverage incomplete: awarded={Count("kick_in_awarded")} started={Count("kick_in_started")}");
            if (freePlayTravel <= 35f) failures.Add($"Free-play ball travel too low: {freePlayTravel:0.00} m");
            if (longestLiveStuck >= 10f) failures.Add($"Continuous live ball deadlock: {longestLiveStuck:0.00} s");
            if (freeActions < 6) failures.Add($"Free-play action count too low: {freeActions}");
            if (CountActorPrefixFrom("ball_touch", "Orange", freePlayEventStart) < 2) failures.Add("Orange had fewer than 2 free-play touches");
            if (CountActorPrefixFrom("ball_touch", "Mint", freePlayEventStart) < 2) failures.Add("Mint had fewer than 2 free-play touches");
            bool pass = failures.Count == 0;
            Finish(pass, pass ? "" : string.Join(" | ", failures));
        }

        void SetAgents(bool value)
        {
            foreach (SoccerAgentAI agent in agents) agent.enabled = value;
        }

        int Count(string type) => CountFrom(type, 0);

        int CountFrom(string type, int start)
        {
            int count = 0;
            for (int i = Mathf.Clamp(start, 0, game.Events.Count); i < game.Events.Count; i++)
                if (game.Events[i].type == type) count++;
            return count;
        }

        int CountActorPrefixFrom(string type, string prefix, int start)
        {
            int count = 0;
            for (int i = Mathf.Clamp(start, 0, game.Events.Count); i < game.Events.Count; i++)
                if (game.Events[i].type == type && game.Events[i].actor.StartsWith(prefix, StringComparison.Ordinal)) count++;
            return count;
        }

        float MaximumGap(bool visible)
        {
            float maximum = 0f;
            foreach (DiagnosticEvent item in game.Events)
            {
                if (item.type != "pass_released" && item.type != "pass_received" && item.type != "shot_contact" && item.type != "tackle_contact") continue;
                if (!visible) { maximum = Mathf.Max(maximum, item.contactGap); continue; }
                const string token = "visibleGap=";
                int at = item.detail.IndexOf(token, StringComparison.Ordinal);
                if (at < 0) continue;
                string value = item.detail.Substring(at + token.Length).Split(';')[0];
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gap))
                    maximum = Mathf.Max(maximum, gap);
            }
            return maximum;
        }

        void Capture(string label)
        {
            if (string.IsNullOrEmpty(captureDirectory)) return;
            Directory.CreateDirectory(captureDirectory);
            ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, $"{label}-{game.CurrentSpeed:0.00}.png"));
        }

        void CaptureClose(string label, Vector3 focus)
        {
            if (string.IsNullOrEmpty(captureDirectory) || !Camera.main) return;
            Directory.CreateDirectory(captureDirectory);
            Camera camera = Camera.main;
            SoccerCameraController controller = camera.GetComponent<SoccerCameraController>();
            bool controllerWasEnabled = controller && controller.enabled;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            RenderTexture savedTarget = camera.targetTexture;
            RenderTexture active = RenderTexture.active;
            if (controller) controller.enabled = false;
            camera.transform.position = focus + new Vector3(4.8f, 3.1f, -5.4f);
            camera.transform.LookAt(focus + Vector3.up * .8f);
            RenderTexture target = new RenderTexture(960, 540, 24);
            Texture2D image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            RenderTexture.active = target;
            camera.Render();
            image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(captureDirectory, $"{label}-{game.CurrentSpeed:0.00}.png"), image.EncodeToPNG());
            camera.targetTexture = savedTarget;
            RenderTexture.active = active;
            camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            if (controller) controller.enabled = controllerWasEnabled;
            Destroy(target);
            Destroy(image);
        }

        void Advance()
        {
            phase++;
            phaseStarted = Time.time;
        }

        void Finish(bool passed, string failure)
        {
            finished = true;
            float freeSeconds = Mathf.Max(0f, Time.time - freePlayStarted);
            MatchAcceptanceReceipt receipt = new MatchAcceptanceReceipt
            {
                passed = passed,
                failure = failure,
                practiceSpeed = game.CurrentSpeed,
                fixedDeltaTime = Time.fixedDeltaTime,
                beginnerWasDefault = defaultBeginner,
                gentleWasDefault = defaultGentle,
                advancedWasSelectable = advancedSelectable,
                balancedWasSelectable = balancedSelectable,
                robotCount = game.Robots.Count,
                passReleased = Count("pass_released"),
                passReceived = Count("pass_received"),
                normalLateralPasses = Count("normal_lateral_pass_verified"),
                shotContacts = Count("shot_contact"),
                tackleContacts = Count("tackle_contact"),
                bodyContacts = Count("body_contact"),
                goals = Count("goal"),
                kickInsAwarded = Count("kick_in_awarded"),
                kickInsStarted = Count("kick_in_started"),
                goalPlaneEdgeCasesPassed = goalPlaneEdgeCasesPassed,
                interceptions = Count("interception"),
                maximumCreditedPhysicalGap = MaximumGap(false),
                maximumCreditedVisibleGap = MaximumGap(true),
                freePlayBallTravel = freePlayTravel,
                freePlayStuckSeconds = freePlayStuck,
                freePlayLongestLiveStuckSeconds = longestLiveStuck,
                freePlayDeadBallSeconds = freePlayDeadBall,
                freePlaySeconds = freeSeconds,
                freePlayPassAttempts = CountFrom("pass_attempt", freePlayEventStart),
                freePlayShots = CountFrom("shot_attempt", freePlayEventStart),
                freePlayTackles = CountFrom("tackle_attempt", freePlayEventStart),
                freePlayGoals = CountFrom("goal", freePlayEventStart),
                freePlayOrangePasses = CountActorPrefixFrom("pass_released", "Orange", freePlayEventStart),
                freePlayMintPasses = CountActorPrefixFrom("pass_released", "Mint", freePlayEventStart),
                freePlayOrangeShots = CountActorPrefixFrom("shot_contact", "Orange", freePlayEventStart),
                freePlayMintShots = CountActorPrefixFrom("shot_contact", "Mint", freePlayEventStart),
                freePlayOrangeTouches = CountActorPrefixFrom("ball_touch", "Orange", freePlayEventStart),
                freePlayMintTouches = CountActorPrefixFrom("ball_touch", "Mint", freePlayEventStart),
                freePlayDifficultyProfile = gentleOnlyExhibition ? "Gentle (60 simulation seconds)" : "Gentle 7s, then Balanced",
                maximumUncorrectedStanceDrift = GetStanceMetrics(0),
                maximumResidualStanceDrift = GetStanceMetrics(1),
                configuredFootPlantCount = Mathf.RoundToInt(GetStanceMetrics(2)),
                longestContinuousPlantSeconds = GetStanceMetrics(3),
                plantAcquisitionCount = Mathf.RoundToInt(GetStanceMetrics(4)),
                locomotionPlantAcquisitionCount = Mathf.RoundToInt(GetStanceMetrics(8)),
                longestContinuousLocomotionPlantSeconds = GetStanceMetrics(9),
                actionPlantAcquisitionCount = Mathf.RoundToInt(GetStanceMetrics(10)),
                longestContinuousActionPlantSeconds = GetStanceMetrics(11),
                maximumLocomotionResidualDrift = GetStanceMetrics(5),
                maximumActionResidualDrift = GetStanceMetrics(6),
                maximumActionResidualName = MaximumActionResidualName(),
                maximumActionResidualNormalizedTime = GetStanceMetrics(7),
                events = new List<DiagnosticEvent>(game.Events)
            };
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, JsonUtility.ToJson(receipt, true));
            Debug.Log($"SOCCER_MATCH_ACCEPTANCE {(passed ? "PASS" : "FAIL")} {failure}");
            Application.Quit(passed ? 0 : 2);
        }

        void StanceMetrics(out float uncorrected, out float residual, out int configured)
        {
            uncorrected = 0f; residual = 0f; configured = 0;
            foreach (SoccerFootPlant plant in FindObjectsByType<SoccerFootPlant>())
            {
                if (plant.IsConfigured) configured++;
                uncorrected = Mathf.Max(uncorrected, plant.MaximumUncorrectedStanceDrift);
                residual = Mathf.Max(residual, plant.MaximumResidualStanceDrift);
            }
        }

        float GetStanceMetrics(int kind)
        {
            StanceMetrics(out float uncorrected, out float residual, out int configured);
            if (kind == 0) return uncorrected;
            if (kind == 1) return residual;
            if (kind == 2) return configured;
            float longest = 0f;
            int acquisitions = 0;
            float locomotionResidual = 0f;
            float actionResidual = 0f;
            float actionNormalizedTime = 0f;
            int locomotionAcquisitions = 0;
            float locomotionLongest = 0f;
            int actionAcquisitions = 0;
            float actionLongest = 0f;
            foreach (SoccerFootPlant plant in FindObjectsByType<SoccerFootPlant>())
            {
                longest = Mathf.Max(longest, plant.LongestContinuousPlantSeconds);
                acquisitions += plant.PlantAcquisitionCount;
                locomotionResidual = Mathf.Max(locomotionResidual, plant.MaximumLocomotionResidualDrift);
                if (plant.MaximumActionResidualDrift > actionResidual)
                {
                    actionResidual = plant.MaximumActionResidualDrift;
                    actionNormalizedTime = plant.MaximumActionResidualNormalizedTime;
                }
                locomotionAcquisitions += plant.LocomotionPlantAcquisitionCount;
                locomotionLongest = Mathf.Max(locomotionLongest, plant.LongestContinuousLocomotionPlantSeconds);
                actionAcquisitions += plant.ActionPlantAcquisitionCount;
                actionLongest = Mathf.Max(actionLongest, plant.LongestContinuousActionPlantSeconds);
            }
            if (kind == 3) return longest;
            if (kind == 4) return acquisitions;
            if (kind == 5) return locomotionResidual;
            if (kind == 6) return actionResidual;
            if (kind == 7) return actionNormalizedTime;
            if (kind == 8) return locomotionAcquisitions;
            if (kind == 9) return locomotionLongest;
            if (kind == 10) return actionAcquisitions;
            return actionLongest;
        }

        string MaximumActionResidualName()
        {
            SoccerFootPlant maximum = null;
            foreach (SoccerFootPlant plant in FindObjectsByType<SoccerFootPlant>())
                if (!maximum || plant.MaximumActionResidualDrift > maximum.MaximumActionResidualDrift) maximum = plant;
            return maximum ? maximum.MaximumActionResidualName : "None";
        }
    }
}
