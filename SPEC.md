# Robo Street Soccer — Implementation Specification

Status: implementation and public-release work authorized on 2026-09-06. This specification describes the final 2v2 Windows release target. Feature status belongs in the release receipt and must distinguish automated evidence, human-played evidence, and unverified behavior.
Project root: this repository

## Product priorities
1. Convincing physical interaction and robot locomotion.
2. Responsive, understandable, enjoyable play.
3. Simple two-on-two match flow.
Rules, tactical sophistication and learning must not undermine the first two priorities. Preserve the tennis project unchanged. Public release is authorized for the finished soccer game and its safe, reproducible documentation; no paid generation or new spending is required.

## Match defaults
An approximately 18 x 26 metre enclosed pitch with low rebound boards, two recessed goals and clearly marked scoring planes. Two orange robots versus two mint robots; no dedicated keeper, offside or fouls. Ordinary board rebounds remain live; balls escaping over the boards use the approved kick-in restart below. First team to three wins. Restart at midfield after goals, with opponents kept clear until restart. Provide a two-player practice mode first and retain it for motion/contact validation.

## Ground-focused play — approved
The first prototype emphasizes ground passes and dribbling, with modest lift for shots. Headers, high crosses, bicycle kicks and elaborate aerial play are deferred. Natural airborne rebounds still obey physics and may go out of play; ground-focused does not mean artificially clamping the ball to the floor. Test rolling slowdown, energy loss on bounces, receiving cushion, simultaneous contacts without stacked kick impulses, playable wall/corner rebounds and consistent outcomes in simulation time at all three practice speeds.

## Control presets
Presets change input and assistance only; they are separate from game difficulty and use the same physical rules. BEGINNER is the initial default. Passing and shooting stay separate so AI never guesses which action the human intends.

### Difficulty styles
- GENTLE is the initial default selectable difficulty. It should make the local rule-based opponents more forgiving and give the player more readable time and space while preserving the same ball, body and goal physics.
- BALANCED is selectable for a more active challenge. It may change opponent decision timing, positioning and pressure within bounded, readable rules; it must not secretly change collision radii, contact validity, scoring geometry or kick-in legality.
- Difficulty is a gameplay behavior setting. It is independent of Beginner/Advanced controls, which remain selectable without changing difficulty.

### Beginner (initial default)
- Arrow keys: camera-relative movement; WASD may provide an equivalent option.
- Space: assisted pass only; it never switches the selected robot.
- J or a clearly labeled Shoot UI button: shoot only; it never requests a tackle.
- Dribbling is automatic through physical foot taps when the ball is controllable.
- On defense, assistance may initiate a standing tackle only when close AND facing a reachable ball. Actual foot-ball contact and action cooldown remain mandatory. Proximity alone never steals possession.
- Human control follows successful teammate pass reception; otherwise it stays with the selected robot throughout live play. No manual switch button is required.
- Labeled UI buttons provide Normal / Half / Quarter speed, Pause / Resume and Restart. Keyboard shortcuts are optional.

### Advanced
- WASD or arrow keys: camera-relative movement.
- Space: assisted pass in possession; otherwise manually switch to the best-positioned teammate. Automatic pass-related selection transfer still waits for actual reception; manual defensive switching is explicit.
- J or left mouse button: shoot when the ball is reachable in possession; standing tackle otherwise.
- 1 / 2 / 3: normal / half / quarter simulation speed; R: restart; Escape: pause/resume.
- F1: diagnostics overlay; F2: practice/match selection when available.
- Labeled speed, pause and restart UI buttons remain available.

Movement acceleration, stopping and turn rates are bounded. Input is sampled every display frame and actions execute in fixed simulation steps. Slow motion changes simulation time, not contact distances or tactical rules.
## Possession and control transitions
Ball remains a dynamic rigid body in all live-play states. Possession is a logical affordance, not parenting or attachment. A nearby ball within a robot's forward control area can enter dribble state if speed and height are controllable. Foot taps maintain possession through discrete physical impulses, with no position teleport or continual magnetic attachment.
Pass selects the teammate and predicts a modest reception lead. A short wind-up aligns hips and support foot. Release occurs only when the striking foot reaches the ball; if the ball has moved outside reach the action misses. Do not freeze the ball during wind-up. Record passer, receiver and pass id on release. The passer remains selected during flight. A receiver slows/cushions the arriving ball through a bounded contact impulse and is credited with reception only on actual contact plus control-speed confirmation. Then switch human selection. A missed pass, rebound or opponent interception does not cause premature switching.
Shooting uses the same contact discipline with greater impulse and limited lift. Direction uses attack-goal assistance constrained by robot facing and realistic turn time. Tackles are standing foot extensions; no slides, body teleport or automatic stealing. A tackle succeeds only through ball contact. Action cooldowns prevent repeated impulse stacking. Loose-ball control and interceptions use the same physical reception rules.

## Ball and collisions
One authoritative fixed-step physics simulation, nominal 100 Hz, with continuous collision detection on the ball. Gravity remains physical. Approved initial physical ball radius: approximately 0.11 m (0.22 m diameter), mass approximately 0.43 kg. The earlier 0.22 m radius was a radius/diameter error, not the intended default. This is approximately a standard football's size; [IFAB Law 2](https://www.theifab.com/laws/latest/the-ball/) specifies a circumference of 68–70 cm and mass of 410–450 g. Current readability tuning intentionally uses an oversized rendered shell while preserving the 0.11 m physical collision radius. Shooting may use the visible rendered boundary as bounded input assistance; dribble, pass, receive and collision receipts must continue to use true physical contact geometry. Do not present the visual shell as the physical ball size. Tune finite rolling resistance, angular response, wall restitution and floor friction together. Ball spin follows rolling speed where grounded; no arbitrary air braking or speed-dependent wall rules. Board collisions, robot bodies, goal posts and ball share the same simulation. Avoid artificial ball ownership collision exemptions.

## Out-of-bounds and kick-in restart — approved
Perimeter boards are solid: a ball hitting and rebounding from them stays in live play. If the complete ball escapes over the boards outside a valid scored goal, briefly stop play and place it safely just inside the boundary near the exit point. Award the kick-in to the team that did not touch the ball last. This is one simple arcade restart for boundary exits, not the outdoor-soccer distinction between throw-ins, corners and goal kicks.

Move opponents back enough to provide a clear restart and place the restarting robot near the stationary ball. Show a short KICK-IN prompt and a controlled-player ring. For a human-team restart, select the restarting robot during this explicit dead-ball setup; Space uses the existing pass action to resume play. An AI-team restart uses the same setup and contact-gated pass after a short readable preparation. No new throw animation or control is required. The restart becomes live at the actual kick contact, not merely on the button press; prevent repeated reset/restart events. Grounded-ball placement and robot repositioning are allowed only in the logged dead-ball setup, never as live receiving or possession assistance.

Track the last robot to physically touch the ball, the exit location and the awarded team. A valid goal takes precedence over an out-of-bounds restart. A ball escaping through a collider defect or becoming trapped requires a separately logged game-error recovery, not an ordinary last-touch penalty or a player-skill failure. Cover uncertain last-touch/recovery cases explicitly during implementation rather than assigning an arbitrary player fault.

## Goal frame and scoring — approved
Both posts and the crossbar have solid physical colliders. Frame rebounds depend on incoming speed and impact angle, with energy loss; glancing impacts deflect naturally. Rebounds remain live and available to either team. Continuous collision handling must prevent fast shots from tunneling through posts, crossbar or frame junctions.
Score only when the entire ball crosses the goal plane within the valid mouth, inside the posts and below the crossbar, accounting for the ball radius. Touching the frame alone is not a goal. A frame rebound that subsequently fully crosses the valid plane counts once; a ball that bounces back before fully crossing does not count. Use a goal latch until the explicit dead-ball reset to prevent duplicate scoring. Resetting after a goal is distinct from live possession; diagnostics must expose resets so they cannot masquerade as receiving.
A simple behind-goal ball-catching/net boundary is sufficient initially. Keep its collision boundary behind the scoring plane so it cannot prevent valid full crossing. Elaborate deforming nets are deferred.

## Body contact — approved
Robots have solid bodies with blocking and slight pushing. Head-on contact slows or blocks motion with only small displacement; glancing contact permits brushing past. Bound pushing so sustained movement cannot bulldoze another robot across the pitch. Robots remain upright and show a plausible lean/recovery reaction. Apply identical contact rules to both teams and to teammates. Body contact may disrupt dribbling through physical interaction, but never automatically transfers possession.
## Locomotion and contact acceptance
Inspect reused rig's hips, knees, ankles, foot axes, bind pose and deformation before choosing its motion strategy. A rigid decorative shell without usable articulated legs is insufficient. Use authored/procedural locomotion with world-space stance targets and two-bone leg IK where needed. Swing feet lift, advance and land; stance feet stay planted while hips move over them. Replan stride length for speed and heading; decelerate for sharp turns. Torso leans with acceleration and turns, constrained to plausible angles. Idle and action transitions blend smoothly.
Author idle, walk/run, turn/brake, dribble tap, receive/cushion, inside-foot pass, instep shot, standing tackle and upright body-contact lean/recovery. Each action has wind-up, contact and recovery phases; the support foot stays planted and foot-to-ball contact determines release. Rendering and physics must use the same action timeline.
Acceptance at quarter speed: no visible stance-foot sliding, foot penetration, impossible knee inversion, abrupt heading snaps, early ball release, foot missing ball at credited contact, or receiver/ball teleports. Measure stance drift and contact gap as well as watching clips. Targets: stance drift <= 3 cm over a plant; contact surface gap <= 5 cm at impulse. These are targets, not claims of current performance.

## Game AI architecture — approved clarification
Here AI means rule-based game decision-making implemented locally in Unity/C#, not a trained neural model or language model. Both teammate and opponents use finite-state controllers with scored choices between available actions, bounded steering and shared physical action interfaces. No GPT, Claude, cloud inference, model API key, paid inference or online learning is required. Different roles choose different actions from this common foundation. Beginner/Advanced selects controls and assistance, not an AI model. Selectable behavior styles or difficulty can be evaluated later; a model-selection menu and multiple trained policies are not part of the first prototype.

## Teammate controller — rule-based game AI
States: idle/restart, support, offer passing lane, approach loose ball, receive targeted pass, cover, attack with possession. Avoid crowding the human; offer a diagonal forward or lateral outlet with separation and line-of-pass checks. A targeted receiver prioritizes the predicted landing/intercept point and brakes before contact. The nearest sensible robot pursues a loose ball while its partner covers. In defense, one pressures while the other covers the central route to goal. Use bounded steering and the same acceleration, turning, action and physics interfaces as the human.

## Opponent controllers — rule-based game AI (only after first milestone passes)
Two mint robots share press/cover and attack/support roles. Ball carrier advances, shoots from reasonable range and passes if pressured and a lane is open. No goalkeeper designation or omniscient teleport interceptions. Opponents may intercept through reachable motion and contact only. Ensure possession can change naturally without sticky ownership.

## Camera and presentation
Stable elevated three-quarter camera showing the entire small pitch initially, orange/mint robot style, crisp shadows, readable ball and controlled-player ground ring. Minimal HUD: score/first-to-three, speed, practice/match, selected control preset and short preset-specific controls. Provide labeled speed, pause/resume and restart buttons plus a clearly labeled Shoot button for Beginner. Control-preset selection is separate from any game difficulty setting. Show incoming receiver subtly; selected ring follows actual selection. Avoid moving camera that changes control axes unexpectedly. Pause and end-match panels must accept restart.

## Diagnostics
Separate event types and counters: pass_attempt, pass_released, pass_received, bad_pass_out_of_reach, bad_pass_missed_contact, interception, receiver_positioning_failure, loose_ball_recovery, shot_contact, tackle_contact, body_contact, push_limit_reached, contact_recovery, goal, frame_rebound, goal_crossing_rejected, duplicate_goal_suppressed, wall_rebound, animation_contact_gap, stance_drift, game_reset. Include simulation time, actor, target, ball position/velocity, action phase and contact distance. No tennis return metric reuse. Keep logs local to this project; no personal profiles or persistent learning.

## Phased milestones
M0: verify tools and source; inspect rig; record asset provenance and license limits.
M1: two robots only. Build pitch, locomotion, ball, dribble, assisted pass, physical reception, selection transfer and shot. Include solid body contact, head-on blocking, glancing contact, bounded pushing and upright lean/recovery in this first physics milestone, using the teammate for controlled contact scenarios. Run repeatable sequences and inspect quarter-speed evidence. Include solid posts/crossbar, live frame rebounds and complete-ball goal detection in the shot validation.
M2: add standing tackles and the two opponents; implement support/cover/press states, first-to-three scoring, goal restarts and kick-ins for either team. Add selectable Gentle/Balanced difficulty with Gentle as the default, while keeping Beginner/Advanced controls independent.
M3: Windows build, controls/readme, regression scenarios, all-speed validation, release packaging and evidence receipt. Clearly distinguish automated checks, visually observed results and unverified behavior. Publish only the safe source/docs and the final portable package authorized by the user.

## Validation matrix
At 1x, 0.5x and 0.25x test move/stop/sharp turn; dribble straight/turn; stationary and moving pass/receive; pass miss; wall rebound; shot and goal; body contact; preset-appropriate defense/tackle; pause/restart. Test Beginner separate pass/shoot inputs, automatic dribbling, labeled UI controls, selection retention and receipt-only transfer. Test assisted tackling with close/facing/reachable/contact/cooldown prerequisites, including negative cases for each condition; proximity alone must fail to steal. Test Advanced manual defensive switching and contextual actions separately. Confirm actual-receipt selection transfer and no transfer on failed pass. Compare trajectories in simulation time across speeds. Check every action at quarter speed from a useful contact camera; retain local screenshots/clips where tooling allows. Test teammate separation and loose-ball chase. In M1, at every speed, test head-on blocking with small displacement, glancing brushing, sustained push limits, upright lean/recovery and disrupted dribbling without automatic possession transfer. In M2 repeat those contact checks across opposing robots and teammates to confirm identical rules, then add interception, press/cover, simultaneous contacts, score latch and first-to-three end state. At normal, half and quarter speed validate each post, the crossbar and post/crossbar corner junctions; high-speed shots without tunneling; inside/outside near misses accounting for ball radius; frame-to-goal and frame-to-pitch outcomes; touching the frame without scoring; bounce-back before full crossing; and duplicate goal prevention. Confirm live rebounds are available to either team once opponents are present. Build success alone does not establish visual or fun acceptance.

Additional restart and ball-size checks: verify the initial visible/collision radius is 0.11 m and readable from the match camera. At all three speeds test normal board rebounds without stopping play, whole-ball over-board exits, last-touch attribution including deflections, goal-versus-exit precedence, safe boundary/corner placement, opponent clearance, human Space restart and AI restart, no release without contact, and exactly one restart per exit. Log out_of_bounds, kick_in_awarded, kick_in_started and game_error_recovery separately. A recovery caused by a collision defect must not be counted as human underperformance.

## Deferred scope
Persistent AI learning, trained-model selection, online multiplayer, elaborate football laws, headers, high crosses, bicycle kicks, sliding tackles, offside, goalkeeper specialization, league progression, elaborate deforming nets, paid new assets and any changes to tennis code/repository. Public release of raw Unity caches, runtime logs, private profiles, credentials, transcripts or unrelated personal assets is excluded.


