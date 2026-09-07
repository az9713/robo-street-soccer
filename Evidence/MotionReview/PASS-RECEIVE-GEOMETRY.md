# Pass and receive geometry review

Historical controller analysis, 2026-09-06. The failed 1x trace below informed the gather-and-pass correction; the frozen build subsequently passed the 1x, 0.5x, and 0.25x standalone receipts. Final status and limits are in `REPORT.md`. This document does not describe the final receipt.

## Observed failure modes

The first controlled pass started at `2.530 s` while the ball was still moving forward at `1.428 m/s`. It missed the Pass contact window at `3.200 s`; the ball was `1.49 m` forward in the passer's local frame. That lies beyond the authored Pass contact envelope, so braking the passer cannot make this a valid kick.

The later moving-receiver failure has a distinct cause. At `62.970 s`, Receive began `0.270 s` after release. The ball was `3.19 m` ahead and nearly centered in the receiver's local frame, but the receiver and ball had a relative lateral speed of about `3.21 m/s`. Projecting the existing `0.3667 s` Receive contact delay gives a local ball position of about `-1.10 m` lateral and `0.75 m` forward at contact. The forward distance is suitable, but the lateral miss is not. The trace then recorded no contact and, at `63.680 s`, a local ball position of `(-1.92, 0.11, 0.41)`.

The existing fixed `receiverVelocity * 0.12` lead is therefore too short for this moving-receiver case. The early Receive trigger (`distance < 3.3 m`, `arrival < .48 s`) also measures arrival at the robot centre, not at its foot after the animation's contact delay.

## Existing authored envelope

Blender rig space uses forward `-Y`. The following numbers are source measurements of `Foot.R` and are useful for game-side targeting; Unity's final contact test must remain authoritative.

| Action | Contact | Right ankle forward | Right toe forward | Right ankle height | Stable presentation | Left support |
|---|---:|---:|---:|---:|---|---|
| Pass | `0.5333 s` | `0.3096 m` | `0.7426 m` | `0.2015 m` | ankle forward `0.1766–0.2745 m`, toe `0.6118–0.7075 m`, from `0.4333–0.6333 s` | ankle is effectively fixed from `0.4333–0.6333 s` (and already settled by `0.2667 s`) |
| Receive | `0.3667 s` | `0.3048 m` | `0.7382 m` | `0.1698 m` | held at the extended pose from `0.3667–0.6000 s` | ankle is effectively fixed from `0.2333–0.6000 s` |

With the current game probe (`0.025 m` heel offset, `0.30 m` forward length, physical ball plus foot radius `0.225 m`), the contact-centre forward ranges are approximately `0.079–0.815 m` for Pass and `0.063–0.822 m` for Receive when lateral and height alignment are good. The `<= 5 cm` gate expands those estimates to roughly `0.026–0.869 m` and `0.011–0.873 m`. These are geometric bounds, not permission to accept a wide lateral error.

The source places the right-foot centre on its own side of the robot at contact (Pass X `-0.176 m`, Receive X `-0.188 m` in rig coordinates). The game should target the live `Foot.R` transform and its existing physical-gap test, rather than robot-centre distance or a world-axis proxy.

## Recommended gather then pass sequence

1. Keep a pending pass request while the carrier runs beside the ball. Do not start Pass merely because the broad possession region (`local Z < .90 m`) is true.
2. Start Pass only after the live physical-foot gap is `<= .05 m`, the ball is aligned with the right-foot probe, and planar ball-to-carrier relative speed is no more than about `0.6–0.8 m/s`. This follows from the `0.5333 s` Pass wind-up: a ball starting around `0.30 m` forward can drift only about `0.55 m` before it leaves the safe end of the contact envelope. At `1.428 m/s`, it would travel about `0.76 m` during the wind-up and is not ready.
3. The gather is ordinary rigid-body movement: run alongside and decelerate with the ball until that condition is true. Do not snap the ball, widen the contact gate, or begin a visually planted Pass while the ball is escaping.
4. At pass release, predict the receiver's **right-foot target at Receive contact**, including the actual remaining flight time, rather than adding a fixed `0.12 s` of receiver velocity. A short forward simulation using the ball's real damping and the receiver's current velocity is adequate. Aim for the live right-foot probe with a small lateral margin, then let the receiver run to that point.
5. Start Receive only when the predicted ball position at `now + 0.3667 s` is inside its live foot-contact envelope. Keep the receiver in Run while it closes; enter Receive after the approach has put the foot target under the incoming trajectory, then decelerate into the stable `0.2333–0.6000 s` left-support interval. This preserves the existing upright cushion pose and prevents an early stationary robot from watching the ball cross its body.

No new gather clip is warranted by the source geometry. The existing Receive clip already provides a `0.2333–0.6000 s` planted left support with the right foot presented from `0.3667–0.6000 s`; gameplay timing and a flight-time lead are the demonstrated missing pieces. Reconsider a short gather clip only if a replay after this timing change still shows a visible stop that cannot be bridged by Run into Receive.
