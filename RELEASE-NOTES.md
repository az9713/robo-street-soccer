# Robo Street Soccer — Release Notes

## v0.1.0

The v0.1.0 Windows release passed the three-speed gameplay receipts and portable-package checks. Publication is handled separately from this workspace.

Download: [RoboStreetSoccer-v0.1.0-Windows.zip](https://github.com/az9713/robo-street-soccer/releases/download/v0.1.0/RoboStreetSoccer-v0.1.0-Windows.zip).

### Play and controls

- Enclosed 2v2 match: two orange robots versus two mint robots, first to three goals.
- Gentle is the default difficulty; Balanced increases opponent pressure. Beginner is the default control preset; Advanced is independently selectable.
- Beginner uses arrow keys or WASD to move, Space to pass, and J or the labeled Shoot button to shoot. Advanced keeps movement and Space while adding contextual defensive switching; J or left mouse performs the contextual shoot/tackle action.
- The HUD exposes 1×, ½× and ¼× practice speed, Pause, Restart, control preset and opponent difficulty. The fixed simulation step remains 0.01 seconds while playback speed changes.
- Camera controls are right-drag orbit, mouse wheel or `+`/`-` zoom, `Q`/`E` rotate and `Home` reset.

### Ball and contact

The approved physical ball radius is 0.11 m (0.22 m diameter) with mass approximately 0.43 kg. The ball remains a dynamic rigid body. The visible ball shell is intentionally larger for readability; shooting can use that rendered boundary as bounded assistance, while dribble, pass, receive and collision evidence uses the true physical radius. Proximity alone does not grant possession.

Board rebounds remain live. A complete exit outside a valid goal creates a short kick-in setup, awards the restart to the team that did not touch the ball last, clears opponents and resumes only after physical restart contact. Valid goals take precedence.

Observed prototype behavior: an exact double-board corner trap first receives physical board-clearance handling. If it remains stationary for four simulation seconds, one clearly logged neutral referee dead-ball drop recovers it. One such recovery occurred in each 60-second exhibition run. The score is preserved, no player is blamed and live possession is never teleported.

### Current verification boundary

The final receipts pass at 1×, ½× and ¼× with four robots, Beginner/Gentle defaults, one pass reception, two shot contacts, nine tackle contacts, four goals and nine awarded/eight started kick-ins per run. Body-contact counts were 166 at 1× and 142 at both reduced speeds; maximum stance residual stayed below 0.022 m, with the peak recorded during Tackle at reduced speed. The build receipt binds the executable SHA-256 and the managed gameplay assembly SHA-256.

In that 1× controlled chain, the queued pass began at simulation time 2.77 s, released at 3.31 s and was received at 4.74 s; the buffered shot request began at 5.24 s and made physical contact at 5.88 s. These are receipt timings, not a human fun or playability rating.

Automated receipts do not constitute human approval of fun, feel, motion quality or overall playability.

The package workflow records the ZIP hash, byte size, source executable hash, managed gameplay assembly hash, deterministic runtime manifest hash and hashes of the build and acceptance evidence. Package verification checks every shipped runtime file against that manifest and rejects private project paths, symbols, logs and profiles.

### Provenance

The visual direction follows the [original reference video](https://www.youtube.com/watch?v=DQfL_l5lRpk), but this is an independent reconstruction rather than the creator's project or exact assets. The robot provenance is one Meshy image-to-3D generation followed by local Blender rigging, skin repair and soccer motion work. See [PROVENANCE.md](PROVENANCE.md) for the Meshy ownership guidance, Terms of Use and public asset boundary.
