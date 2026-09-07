using UnityEngine;

namespace RoboStreetSoccer
{
    public enum AgentIntent { Idle, Support, Press, Cover, Receive, Carry, Restart }

    [DefaultExecutionOrder(-25)]
    [RequireComponent(typeof(SoccerRobot))]
    public sealed class SoccerAgentAI : MonoBehaviour
    {
        SoccerRobot robot;
        SoccerGame game;
        SoccerBall ball;
        float nextThink;
        float possessionSince;
        float nextBoardRecoveryLog;
        AgentIntent intent;

        public AgentIntent Intent => intent;

        void Start()
        {
            robot = GetComponent<SoccerRobot>();
            game = FindAnyObjectByType<SoccerGame>();
            ball = FindAnyObjectByType<SoccerBall>();
        }

        void FixedUpdate()
        {
            if (!game || (robot.IsSelected && !game.AcceptanceMode)) return;
            if (game.State == MatchState.KickInSetup)
            {
                if (game.KickInRobot == robot) RunRestart();
                else robot.SetMove(Vector3.zero);
                return;
            }
            if (game.State != MatchState.Live) { robot.SetMove(Vector3.zero); return; }

            if (game.IsTargetedReceiver(robot)) { RunReceive(); return; }
            if (Time.time >= nextThink)
            {
                float reaction = robot.Team == SoccerTeam.Mint && game.Difficulty == OpponentDifficulty.Gentle ? .32f : .17f;
                nextThink = Time.time + reaction;
                ChooseIntent();
            }
            ExecuteIntent();
        }

        void ChooseIntent()
        {
            float touchDistance = Vector3.ProjectOnPlane(ball.transform.position - robot.transform.position, Vector3.up).magnitude;
            float ballSpeed = Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up).magnitude;
            bool retainedPhysicalCarry = ball.LastTouchRobot == robot && touchDistance < 1.55f && ballSpeed < 3.4f;
            SoccerTeam opposition = robot.Team == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange;
            bool directControl = game.RobotControlsBall(robot);
            bool contestedByOpponent = game.TeamLikelyControlsBall(opposition);
            if ((directControl && (!contestedByOpponent || ball.LastTouchRobot == robot)) || retainedPhysicalCarry)
            {
                // A normal dribble tap briefly puts the ball outside the narrow
                // control envelope. Preserve the same physical carrier across
                // that short separation so tactical pass/shot clocks can mature.
                if (possessionSince <= 0f) possessionSince = Time.time;
                intent = AgentIntent.Carry;
                return;
            }
            if (game.SelectedRobot && game.SelectedRobot != robot && game.SelectedRobot.Team == robot.Team
                && (game.RobotControlsBall(game.SelectedRobot)
                    || Vector3.ProjectOnPlane(ball.transform.position - game.SelectedRobot.transform.position, Vector3.up).magnitude < 1.55f))
            {
                possessionSince = 0f;
                intent = AgentIntent.Support;
                return;
            }
            possessionSince = 0f;
            SoccerRobot nearest = game.ClosestRobot(robot.Team, ball.transform.position);
            bool ourControl = game.TeamLikelyControlsBall(robot.Team);
            bool anybodyControls = ourControl || game.TeamLikelyControlsBall(
                robot.Team == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange);
            if (!anybodyControls && Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up).magnitude < .35f)
            {
                // One nearest robot gathers a stopped loose ball. Sending one
                // collector from each team created symmetric, endless body jams.
                intent = game.LooseBallCollector() == robot ? AgentIntent.Press : AgentIntent.Cover;
                return;
            }
            intent = ourControl ? AgentIntent.Support : nearest == robot ? AgentIntent.Press : AgentIntent.Cover;
        }

        void ExecuteIntent()
        {
            switch (intent)
            {
                case AgentIntent.Carry: RunCarry(); break;
                case AgentIntent.Press: RunPress(); break;
                case AgentIntent.Cover: RunCover(); break;
                default: RunSupport(); break;
            }
        }

        void RunCarry()
        {
            if (TryRunBoardClearance()) return;
            float attack = robot.Team == SoccerTeam.Orange ? 1f : -1f;
            Vector3 goal = new Vector3(0f, 0f, attack * SoccerGame.PitchHalfLength);
            float goalDistance = Vector3.Distance(robot.transform.position, goal);
            SoccerTeam opposition = robot.Team == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange;
            SoccerRobot nearestOpponent = game.ClosestRobot(opposition, robot.transform.position);
            float pressure = nearestOpponent ? Vector3.Distance(nearestOpponent.transform.position, robot.transform.position) : 99f;
            SoccerRobot mate = game.TeammateOf(robot);
            bool balanced = robot.Team != SoccerTeam.Mint || game.Difficulty == OpponentDifficulty.Balanced;
            float shootRange = balanced ? 7.2f : 5.4f;
            float passPressure = balanced ? 2.15f : 1.45f;
            if (goalDistance < shootRange && robot.CurrentAction == RobotAction.None)
            {
                float error = robot.Team == SoccerTeam.Mint && !balanced ? DeterministicAimError(7f) : DeterministicAimError(2.5f);
                if (robot.TryStartShoot(error)) return;
            }
            if (mate && (pressure < passPressure || Time.time - possessionSince > 2.2f) && HasOpenLane(mate)
                && robot.CurrentAction == RobotAction.None)
            {
                float error = robot.Team == SoccerTeam.Mint && !balanced ? DeterministicAimError(8f) : DeterministicAimError(2.5f);
                if (robot.TryStartPass(mate, error)) return;
            }
            Vector3 lane = goal - robot.transform.position;
            if (nearestOpponent && pressure < 2.3f)
                lane += Vector3.Cross(Vector3.up, (nearestOpponent.transform.position - robot.transform.position).normalized) * 1.6f;
            robot.SetMove(AvoidCrowding(lane.normalized));
        }

        void RunPress()
        {
            Vector3 planarBallVelocity = Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up);
            SoccerTeam opposition = robot.Team == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange;
            if (planarBallVelocity.magnitude < .35f && !game.TeamLikelyControlsBall(robot.Team)
                && !game.TeamLikelyControlsBall(opposition))
            {
                // Intent is reconsidered on a bounded cadence, so reject stale
                // Press execution when collector ownership has since changed.
                if (game.LooseBallCollector() != robot) { intent = AgentIntent.Cover; RunCover(); return; }
                if (TryRunBoardClearance()) return;
                float attack = robot.Team == SoccerTeam.Orange ? 1f : -1f;
                Vector3 recoveryDirection = Vector3.forward * attack;
                Vector3 setup = ball.transform.position - recoveryDirection * .55f;
                Vector3 setupDelta = Vector3.ProjectOnPlane(setup - robot.transform.position, Vector3.up);
                if (setupDelta.magnitude > .20f) MoveTo(setup, .20f);
                else robot.SetMove(recoveryDirection * .55f);
                return;
            }
            Vector3 intercept = ball.transform.position + Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up) * .16f;
            Vector3 delta = Vector3.ProjectOnPlane(intercept - robot.transform.position, Vector3.up);
            robot.SetMove(AvoidCrowding(delta.normalized));
            if (delta.magnitude < 1.08f && game.TeamLikelyControlsBall(opposition)) robot.TryStartTackle();
        }

        bool TryRunBoardClearance()
        {
            if (Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up).magnitude >= .65f) return false;
            Vector3 inward = Vector3.zero;
            if (Mathf.Abs(ball.transform.position.x) > SoccerGame.PitchHalfWidth - .9f)
                inward.x = -Mathf.Sign(ball.transform.position.x);
            if (Mathf.Abs(ball.transform.position.z) > SoccerGame.PitchHalfLength - .9f)
                inward.z = -Mathf.Sign(ball.transform.position.z);
            if (inward.sqrMagnitude < .1f) return false;

            // Approach from inside and tap into the elastic board. Its physical
            // rebound supplies the inward component without pulling the ball or
            // placing the robot outside the enclosure.
            Vector3 outwardTap = -inward.normalized;
            Vector3 setup = ball.transform.position - outwardTap * .43f;
            Vector3 setupDelta = Vector3.ProjectOnPlane(setup - robot.transform.position, Vector3.up);
            if (setupDelta.magnitude > .13f) MoveTo(setup, .13f);
            else robot.SetMove(outwardTap * .58f);
            if (Time.time >= nextBoardRecoveryLog)
            {
                game.Record("board_clearance_attempt", robot.name, "physical_rebound", robot.PhysicalFootGap,
                    $"outwardTap={outwardTap}; setup={setup}; ball={ball.transform.position}");
                nextBoardRecoveryLog = Time.time + 1.5f;
            }
            return true;
        }

        void RunCover()
        {
            float defend = robot.Team == SoccerTeam.Orange ? -1f : 1f;
            Vector3 ownGoal = new Vector3(0f, 0f, defend * SoccerGame.PitchHalfLength);
            Vector3 cover = Vector3.Lerp(ball.transform.position, ownGoal, .38f);
            cover.x = Mathf.Clamp(cover.x, -5.8f, 5.8f);
            cover.z = Mathf.Clamp(cover.z, -10.2f, 10.2f);
            MoveTo(cover, .65f);
        }

        void RunSupport()
        {
            SoccerRobot carrier = game.ClosestRobot(robot.Team, ball.transform.position, robot);
            if (!carrier) return;
            float attack = robot.Team == SoccerTeam.Orange ? 1f : -1f;
            Vector3 support = carrier.transform.position + Vector3.forward * attack * 3.6f;
            support.x += carrier.transform.position.x <= 0f ? 2.8f : -2.8f;
            support.x = Mathf.Clamp(support.x, -7.2f, 7.2f);
            support.z = Mathf.Clamp(support.z, -10.5f, 10.5f);
            MoveTo(support, .55f);
        }

        void RunReceive()
        {
            intent = AgentIntent.Receive;
            Vector3 velocity = Vector3.ProjectOnPlane(ball.Body.linearVelocity, Vector3.up);
            Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position - robot.transform.position, Vector3.up);
            const float receiveContactDelay = 11f / 30f;
            Vector3 incomingFacing = velocity.sqrMagnitude > .09f ? -velocity.normalized : toBall.normalized;
            Vector3 receiveRight = Vector3.Cross(Vector3.up, incomingFacing).normalized;
            Vector3 predictedBall = ball.transform.position + velocity * receiveContactDelay;
            Vector3 desiredRootAtContact = predictedBall - incomingFacing * .30f - receiveRight * .19f;
            Vector3 currentVelocity = Vector3.ProjectOnPlane(robot.Body.linearVelocity, Vector3.up);
            Vector3 predictedRoot = robot.transform.position + currentVelocity * .14f;
            Vector3 predictedError = Vector3.ProjectOnPlane(desiredRootAtContact - predictedRoot, Vector3.up);
            Vector3 runDelta = Vector3.ProjectOnPlane(desiredRootAtContact - robot.transform.position, Vector3.up);
            robot.SetMove(runDelta.normalized * Mathf.Clamp01(runDelta.magnitude / .8f));
            if (toBall.magnitude < 3.8f && predictedError.magnitude <= .26f)
            {
                robot.SetMove(runDelta.normalized * .18f);
                robot.TryStartReceive();
            }
        }

        void RunRestart()
        {
            intent = AgentIntent.Restart;
            robot.SetMove(Vector3.zero);
            if (Time.time >= game.KickInReadyAt) robot.TryStartPass(game.TeammateOf(robot));
        }

        void MoveTo(Vector3 target, float stopRadius)
        {
            Vector3 delta = Vector3.ProjectOnPlane(target - robot.transform.position, Vector3.up);
            robot.SetMove(delta.magnitude > stopRadius ? AvoidCrowding(delta.normalized) * Mathf.Clamp01(delta.magnitude / 2f) : Vector3.zero);
        }

        Vector3 AvoidCrowding(Vector3 desired)
        {
            Vector3 avoid = Vector3.zero;
            foreach (SoccerRobot other in game.Robots)
            {
                if (other == robot) continue;
                Vector3 away = Vector3.ProjectOnPlane(robot.transform.position - other.transform.position, Vector3.up);
                if (away.magnitude > .01f && away.magnitude < 1.15f) avoid += away.normalized * (1.15f - away.magnitude);
            }
            return Vector3.ClampMagnitude(desired + avoid * .8f, 1f);
        }

        bool HasOpenLane(SoccerRobot target)
        {
            Vector3 start = ball.transform.position + Vector3.up * .12f;
            Vector3 delta = target.transform.position - start;
            SoccerTeam opposition = robot.Team == SoccerTeam.Orange ? SoccerTeam.Mint : SoccerTeam.Orange;
            foreach (SoccerRobot defender in game.Robots)
            {
                if (defender.Team != opposition) continue;
                Vector3 along = Vector3.Project(defender.transform.position - start, delta.normalized);
                Vector3 nearest = start + Vector3.ClampMagnitude(along, delta.magnitude);
                if (Vector3.Distance(nearest, defender.transform.position) < .85f) return false;
            }
            return true;
        }

        float DeterministicAimError(float amplitude)
        {
            return Mathf.Sin((Time.time + robot.RosterIndex * .71f) * 2.17f) * amplitude;
        }

        public void ResetController()
        {
            nextThink = 0f;
            possessionSince = 0f;
            nextBoardRecoveryLog = 0f;
            intent = AgentIntent.Idle;
        }
    }
}
