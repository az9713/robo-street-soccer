# Robo Street Soccer — Development Log

## 2026-09-06 — Final release authorization and requirements audit

The user expanded the target from the M1 practice prototype to a finished, selectable 2v2 Windows game and authorized a public repository at `az9713/robo-street-soccer`. Gentle and Balanced are difficulty styles, with Gentle as the default. Beginner and Advanced remain separate control presets and must not be conflated with difficulty. The release must preserve tennis unchanged and exclude credentials, private logs, profile databases, raw transcripts and Unity caches.

Release review items sent to the gameplay implementation owner:

- Demonstrate two orange robots against two mint robots, first-to-three scoring, goal restart and match-over behavior.
- Demonstrate difficulty selection and default state, with difficulty affecting opponent behavior/forgiveness while preserving physical contact and scoring rules.
- Demonstrate Beginner and Advanced controls independently, including pass/shoot separation, defensive tackle/switch behavior, pause/restart and labeled UI controls.
- Demonstrate live board rebounds separately from complete-ball exits, then the visible kick-in setup, correct last-touch award, opponent clearance, contact-gated human and AI restarts, and exactly one restart per exit.
- Make the final HUD labels and public README match the shipped controls. Any behavior without a final-build receipt remains marked unverified.

## 2026-09-06 — Implementation authorized

Human decision: begin implementation from the reviewed specification and HTML plan. Preserve tennis unchanged. Keep all soccer work inside this repository.

### M0 observations

- Unity 6000.5.7f1 with Windows build support and Blender 5.2.1 LTS are installed.
- The recovered tennis source contains `RoboPlayer.blend`, the Unity FBX and base-color texture.
- Blender inspection found a roughly 1.784 m visual mesh and a 16-bone armature with hips, thighs, shins and feet; there are no separate toe bones.
- The existing tennis project uses Unity fixed timestep 0.02 seconds. Soccer requires an explicit constant 0.01-second simulation step.

### Failure: Unity create-project bootstrap

Command-line `-createProject` created partial cache folders but did not create `Packages/manifest.json`; Unity logged `Failed to update project manifest: The "path" argument must be of type string. Received undefined` and exited with code 1. No soccer source or tennis files were changed. Recovery: define the minimal clean Unity manifest and version explicitly, then open the project normally in batch mode so Unity imports packages and generates defaults.

### Scope correction carried into execution

- Start from a clean Unity project rather than copying the tennis project.
- Keep standing tackles and their assisted-control validation in M2. M1 uses teammate body-contact tests without tackling.
- Use a constant 0.01-second simulation step at every practice speed; only wall-clock playback changes.
- G1 requires automated evidence, quarter-speed contact inspection and human feel approval before opponents.

### M1 implementation and verification

- Created a clean Unity 6000.5.7f1 URP project and a Windows x64 development build. The project uses a constant 0.01-second physics step, continuous dynamic collision detection on the 0.11 m / 0.43 kg ball, solid robot roots, boards, posts and crossbars.
- Authored nine Blender actions from the inspected 16-bone rig: Idle, Run, Turn, Dribble, Receive, Pass, Shoot, Tackle and ContactLean. Tackle animation is authored for later use; tackle gameplay remains in M2.
- Implemented Beginner movement, automatic physical dribble taps, assisted pass lead, relative-velocity receiver timing, physical cushion, receipt-only selection transfer, facing- and reach-gated shooting, teammate support movement, full-pitch camera, selected-player ring and labeled UI controls.
- The standalone runtime initially lost non-serialized game, animator and foot references. These were converted to serialized scene references and initialized again at player startup.
- Early dribble logic immediately restarted its animation, leaving no pass-input window. A short cooldown now exposes a deterministic pass opportunity.
- Early moving passes failed because the passer stopped during wind-up and because the receiver lead was computed before the wind-up. The passer now preserves approach speed until contact and computes the foot-side lead at the strike frame.
- Early receptions used ball speed instead of relative closing speed and could credit only after rebounds. Receiver timing now uses ball-versus-robot velocity; the receive pose holds through its bounded contact window. Reception confirmation occurs on the fixed step after the cushion impulse is resolved.
- An early acceptance result could pass after an unrelated goal reset. The final runner rejects any chain interruption and any wall rebound before reception.
- The scripted driver originally ran on display updates and exposed speed-dependent timing under concurrent load. It now runs before gameplay scripts on the authoritative fixed step.
- Final chain receipts pass at 1x, 1/2x and 1/4x with identical simulation times: pass release 2.35 s, reception/control transfer 2.76 s and shot 4.00 s. Pass/receive gaps are 0 cm; shot gap is about 2.41 cm.
- Final body-contact receipts pass at all three speeds with six head-on and two glancing events, 0.00 m sustained midpoint drift and 0.00 degrees root tilt.
- A final windowed quarter-speed run produced 1920x1080 reception and shot frames. Automated checks do not replace the required human review of feel, readability, stance sliding and overall motion quality.

### Historical M1 gate (superseded by final-release authorization)

At the time of the M1 receipt, G1 was ready for human review and opponents were held until that practice build was approved. The later 2026-09-06 user authorization expanded scope to the finished 2v2 release; final acceptance evidence now supersedes this interim gate.

### Human-playability tuning

- User testing found shooting difficult despite the automated chain passing. Source inspection showed three simultaneous rejection gates: the oversized rendered ball still used the smaller physical radius for shot reach, the robot had to be precisely aligned with its right foot and the north goal, and a J press during automatic dribbling was discarded.
- Shooting now uses the rendered ball boundary for visible-foot reach while pass, receive and dribble contacts retain their original physical radius. The ball remains a dynamic rigid body and receives one impulse at the shot contact frame.
- A J or Shoot-button request is buffered for 0.75 seconds. It may interrupt automatic dribbling, waits through a receive animation, turns the robot toward the ball and applies a bounded 2.4 m/s approach during the wind-up.
- The acceptance driver now requests a buffered shot while the receive animation is still finishing. The full dribble, pass, receive and assisted-shot chain passes at 1x, 1/2x and 1/4x.

### Latest 2v2 fixture status

The final 1× 2v2 fixture passes with four robots, Beginner/Gentle defaults, one pass reception, two shot contacts, nine tackle contacts, 166 body contacts, four goals and nine awarded/eight started kick-ins. Maximum stance residual is 0.02089 m at 1×; the reduced-speed peak is 0.02129 m during Tackle. All three receipts pass and the build hashes bind the executable and managed gameplay assembly.

The same 1× receipt records the queued pass at 2.77 s, pass release at 3.31 s, physical reception at 4.74 s, buffered shot request at 5.24 s and shot contact at 5.88 s of simulation time.
