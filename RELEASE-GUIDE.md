# Robo Street Soccer — Player Guide

Robo Street Soccer is an offline, single-player 2v2 robot soccer game. You play the orange side; local rule-based controllers operate the teammate and mint opponents. The match ends when one side reaches three goals.

## Download and start

1. Download [RoboStreetSoccer-v0.1.0-Windows.zip](https://github.com/az9713/robo-street-soccer/releases/download/v0.1.0/RoboStreetSoccer-v0.1.0-Windows.zip).
2. Extract the entire ZIP into a folder. Do not run the executable inside the compressed archive.
3. Run `RoboStreetSoccer.exe` from the extracted folder. Leave `RoboStreetSoccer_Data`, `UnityPlayer.dll`, `MonoBleedingEdge` and the other extracted runtime files beside it.

The game does not install Unity, contact a server or require an API key.

## Controls

The HUD shows the active control preset, opponent style, speed and selected robot.

| Action | Beginner | Advanced |
|---|---|---|
| Move | Arrow keys or WASD | Arrow keys or WASD |
| Pass | Space | Space while controlling the ball; contextual switch while defending |
| Shoot / tackle | J or `SHOOT [J]`; close, facing tackles are assisted | J or left click for the contextual shoot/tackle action |
| Speed | `1×`, `½×`, `¼×` | `1×`, `½×`, `¼×` |
| Match flow | Pause/Resume, Restart | Pause/Resume, Restart |

Beginner and Advanced are control presets. Gentle and Balanced are opponent styles. The selectors are independent; Gentle and Beginner are the startup defaults. Pass and shoot remain separate actions in Beginner.

Camera controls are right-drag orbit, mouse wheel or `+`/`-` zoom, `Q`/`E` rotate and `Home` reset. During an orange kick-in, Space makes the restart pass. A selected-player ring and HUD label show who you control.

## Ball, contact and scoring

The physical ball has a 0.11 m radius, approximately 0.43 kg mass and a 0.01-second fixed simulation step. It remains a dynamic rigid body. A robot must make physical foot contact to dribble, pass, receive, shoot or tackle; proximity alone does not transfer possession.

The ball rendered on screen is intentionally larger so it remains readable. Shooting can use that visible boundary as bounded assistance. The physical collision radius remains 0.11 m for dribble, pass, receive and contact checks.

Robots block or slow one another in head-on contact and brush past in glancing contact. Contact does not automatically steal the ball or push a robot indefinitely. Goals count when the whole ball crosses the valid goal mouth. Normal board rebounds stay in play.

When the complete ball exits outside a valid goal, play pauses for a kick-in. The team that did not touch the ball last receives it, opponents clear space and play resumes only at physical restart contact. A valid goal takes precedence over an exit.

The final exhibition run exercised a rare double-board corner recovery. Physical board-clearance attempts happen first; if the ball remains pinned in the exact corner for four simulation seconds, one clearly logged neutral referee dead-ball drop recovers it. One such recovery occurred in the 60-second exhibition. The score is preserved, no player is blamed and a live possession is not teleported.

## Evidence and boundaries

The final receipts pass at 1×, ½× and ¼× and record four robots, the Beginner/Gentle defaults, pass reception, shot contacts, tackles, body contacts, goals and kick-ins. They verify mechanics and timing. They do not certify that the game is fun or that every human camera, motion or feel judgment is satisfactory.

This prototype is offline and single-player. It has no online multiplayer, trained-model selection, persistent learning, goalkeeper specialization, offside, elaborate football laws, headers, high crosses, bicycle kicks, sliding tackles or deforming net simulation.

The source package contains sanitized project files, public-safe receipts, the rendered evidence images and `Release/receipt.json`. It excludes Unity caches, generated compiler metadata, logs, profiles, credentials, raw transcripts and private pre-sanitize robot backups.
