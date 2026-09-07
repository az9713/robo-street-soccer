# M1 Verification Receipt

Date: 2026-09-06  
Build: Windows x64 development player, Unity 6000.5.7f1  
Physics: constant 0.01-second fixed step (100 Hz)  
Ball: 0.11 m radius, 0.43 kg

## Dribble, pass, receive, control transfer, shoot

The final runner executes on the fixed physics clock. It rejects any goal, reset or out-of-bounds interruption and rejects a wall rebound before reception, preventing a later recovery from masquerading as the intended direct chain.

| Speed | Result | First dribble | Pass release | Reception / transfer | Shot contact | Largest credited contact gap |
|---|---:|---:|---:|---:|---:|---:|
| 1x | Pass | 0.37 s | 2.35 s | 2.76 s | 4.00 s | 2.41 cm |
| 1/2x | Pass | 0.37 s | 2.35 s | 2.76 s | 4.00 s | 2.41 cm |
| 1/4x | Pass | 0.37 s | 2.35 s | 2.76 s | 4.00 s | 2.41 cm |

Pass and receive gaps were 0 cm in every run. Shot contact was approximately 2.41 cm, below the 5 cm M1 target. Identical simulation timestamps across all speeds show that slow motion changes wall-clock playback while preserving the fixed-step sequence.

Receipts: `Evidence/m1-acceptance-1.00.json`, `Evidence/m1-acceptance-0.50.json`, and `Evidence/m1-acceptance-0.25.json`.

## Solid robot body contact

| Speed | Result | Head-on events | Glancing events | Sustained center drift | Maximum root tilt |
|---|---:|---:|---:|---:|---:|
| 1x | Pass | 6 | 2 | 0.00 m | 0.00 degrees |
| 1/2x | Pass | 6 | 2 | 0.00 m | 0.00 degrees |
| 1/4x | Pass | 6 | 2 | 0.00 m | 0.00 degrees |

Receipts: `Evidence/m1-contact-1.00.json`, `Evidence/m1-contact-0.50.json`, and `Evidence/m1-contact-0.25.json`.

## Rendered evidence and limits

The final quarter-speed windowed run passed and wrote 1920x1080 frames at reception and shot contact:

- `Evidence/captures-final-0.25/receive-0.25.png`
- `Evidence/captures-final-0.25/shot-0.25.png`

Automated evidence establishes build success, fixed-step repeatability, contact gaps, receipt-only selection transfer, and bounded root-body collision behavior. It does not establish fun, responsiveness, foot sliding throughout every clip, or visual quality. Those remain the human G1 review. Opponents are intentionally absent until that review passes.
