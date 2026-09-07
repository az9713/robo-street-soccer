# Robo Street Soccer motion review

Final review for the frozen build, 2026-09-06. This report combines Blender source inspection with the final standalone 1x, 0.5x, and 0.25x Unity receipts. All three runs passed.

## Final assets and privacy

| Asset | SHA-256 |
|---|---|
| `SourceAssets/Robot/RoboPlayerSoccer.blend` | `663B3ED7C52C7E1655B5D7427EA5C6DF9F66F50413A48403348A1DF9BC9AF4AF` |
| `UnityProject/Assets/Art/Robot/RoboPlayerSoccer.fbx` | `58C5414B36E76B9EEE3C9BE0631A5225C4D1E624F6C5625CE1C69951315C0817` |
| `UnityProject/Assets/Soccer/Scripts/SoccerFootPlant.cs` | `D65A5CB037DBA9CB1245669970EF702623333258C74168B9499DE1224A045CA5` |

The evaluated `RoboRig` has scale `(1,1,1)`, 16 bones, and the required hips, thigh, shin, and foot bones. It has no toe bones. Unity imports the FBX as Generic at global scale `1`, with root position and rotation locked.

The final blend and FBX contain no embedded local user path, username, or tennis-project token. Blender re-import verified one armature and all nine takes. `SourceAssets/Robot/Backups/` and recovery `.blend1` files are private and must remain excluded from releases.

## Authored contract

| Clip | Duration | Right-foot contact | Source result |
|---|---:|---:|---|
| Idle | 2.000 s | — | looping rest |
| Run | 1.000 s | — | alternating low support; repaired knees `97.72°–106.29°` |
| Turn | 1.000 s | — | alternating turn steps |
| Dribble | 0.800 s | 0.367 s | right-foot tap, left support |
| Receive | 0.867 s | 0.367 s | presented right foot, left support |
| Pass | 1.133 s | 0.533 s | planted left support and inside pass |
| Shoot | 1.400 s | 0.633 s | left support, right instep swing |
| Tackle | 1.067 s | 0.467 s | upright standing extension and recovery |
| ContactLean | 0.800 s | — | visual-only lean, upright by 0.800 s |

All source knee angles stayed positive; no inversion was found. Blender source sampling confirms the named contact times used by gameplay. The repaired Run provides actual alternating support rather than a treadmill-like leg arc.

## Final standalone verification

Every run used four robots and a fixed simulation step of approximately `0.01 s`.

| Practice speed | Result | Pass / receive | Shot / tackle | Goals | Plant residual max | Locomotion residual max | Plant acquisitions (locomotion / action) |
|---:|---|---:|---:|---:|---:|---:|---:|
| 1.00x | pass | 10 / 1 | 2 / 9 | 4 | `0.020891 m` | `0.0000183 m` | 48 / 86 |
| 0.50x | pass | 10 / 1 | 2 / 9 | 4 | `0.021284 m` | `0.0000186 m` | 50 / 90 |
| 0.25x | pass | 10 / 1 | 2 / 9 | 4 | `0.021286 m` | `0.0000186 m` | 50 / 90 |

The 60-second free-play segment recorded one actual goal at every speed. Ball travel was `55.560 m` at 0.25x and 0.50x, and `54.337 m` at 1x; the longest live stuck interval was `4.590 s`.

The largest residual is the 0.25x Tackle support interval: `2.129 cm`, under the 3 cm target. It is measured after the bounded two-bone correction; uncorrected motion is logged separately and is not presented as a plant result. The nonzero locomotion acquisitions and longest support intervals (`0.350–0.354 s`) show that the locomotion result was exercised rather than left at a default value. Action support intervals reached `0.280–0.285 s`.

The final fixtures record physical pass release and confirmed reception at `0 m` gap, physical dribble contact up to `4.535 cm`, and physical tackle contact up to `2.125 cm`. Tackle contact is tied to its authored standing action and physical foot-gap check; body collision is not credited as a tackle.

The 0.25x close captures are [receive](../match-captures/match-receive-close-0.25.png), [shot](../match-captures/match-shot-contact-close-0.25.png), [tackle](../match-captures/match-tackle-contact-close-0.25.png), and [contact lean](../match-captures/match-contact-lean-close-0.25.png). The receive view makes the upright body and visible ball readable at quarter speed.

## Limits retained deliberately

`CurrentFootGap` is a gameplay proxy: a `0.325 m` segment from `Foot.R`, oriented by robot forward, with a `0.115 m` foot radius. It does not measure the rendered mesh surface or animated foot orientation. Therefore the receipt proves the configured physical proxy sequence, not a sub-5-cm mesh-surface measurement.

The buffered assisted Shoot path intentionally uses the visible ball boundary. Its largest credited physical proxy gap is `6.849 cm`, while its visible gap is `0 m`; this is the existing visual-boundary shot assistance, not a claim that every shot meets the 5 cm physical-proxy threshold. Pass, receive, dribble, and tackle contacts above are reported separately.

`SoccerFootPlant` is visual-only: it never moves the rigidbody, colliders, ball, or striking right foot during an action. It restores the authored baseline before each LateUpdate sample, bounds leg reach, retains the knee bend direction, and only resets anchors through `ResetAnchorsAfterTeleport()` after an explicit rigidbody/Animator reset. It does not re-anchor during live locomotion or action playback.

The close captures are checkpoints, not a frame-by-frame visual audit of every free-play action. Their value is corroboration of the runtime receipts, not a replacement for them.

## Provenance

`inspect_motion.py` is a read-only Blender 5.2 sampling helper. Its source samples are `source-motion-sampling.json`. `PASS-RECEIVE-GEOMETRY.md` records the controller timing analysis that informed the final gather-and-pass behavior. Neither helper mutates the final blend or FBX.
