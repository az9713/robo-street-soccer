# Robo Street Soccer

Robo Street Soccer is a Windows 2v2 robot soccer game built in Unity. Two orange robots play two mint robots on an enclosed rebound pitch. Matches are first to three goals, with local rule-based teammate and opponent controllers. The shipped build is offline and single-player: the player controls the orange side while local controllers operate the other robots.

The visual direction and asset workflow were informed by the [original reference video](https://www.youtube.com/watch?v=DQfL_l5lRpk). This repository is an independent reconstruction; it does not claim to contain the creator's project files or exact creator assets.

## Download and run

Download [RoboStreetSoccer-v0.1.0-Windows.zip](https://github.com/az9713/robo-street-soccer/releases/download/v0.1.0/RoboStreetSoccer-v0.1.0-Windows.zip), extract the whole archive and run `RoboStreetSoccer.exe`. Keep the executable beside `RoboStreetSoccer_Data`, `UnityPlayer.dll`, `MonoBleedingEdge` and any other files from the extracted package. No Unity installation, network connection or API key is required.

The package receipt records the ZIP hash and size, source executable hash, managed gameplay assembly hash, deterministic runtime manifest hash and hashes of the build and acceptance evidence. The clean source snapshot includes that receipt at `Release/receipt.json`.

## Rebuild the source

Open `UnityProject` in Unity `6000.5.7f1`, open `Assets/Soccer/Scenes/RoboStreetSoccer.unity`, and press Play. The **Robo Street Soccer → Build final 2v2 scene** menu item regenerates the final scene. To produce a Windows executable, use Unity Build Profiles for Windows or run `-executeMethod SoccerProjectSetup.BuildWindows`. The source snapshot excludes Unity caches and generated local compiler metadata.

## Modes and controls

The HUD exposes two independent selectors:

- **Opponents:** Gentle is the default; Balanced applies more active opponent pressure. Both use the same physical ball, contact and scoring rules.
- **Control:** Beginner is the default; Advanced is independently selectable and does not change difficulty.

| Action | Beginner | Advanced |
|---|---|---|
| Move | Arrow keys or WASD | Arrow keys or WASD |
| Pass | Space | Space while controlling the ball; contextual switch while defending |
| Shoot / tackle | J or the `SHOOT [J]` button; close, facing tackles are assisted | J or left click; contextual shoot/tackle action |
| Practice speed | `1×`, `½×`, `¼×` | `1×`, `½×`, `¼×` |
| Match flow | Pause/Resume, Restart | Pause/Resume, Restart |

The selected robot is marked in the HUD and on the pitch. Camera controls are right-drag orbit, mouse wheel or `+`/`-` zoom, `Q`/`E` rotate and `Home` reset. During an orange kick-in, press Space to make the physical restart pass. The HUD is the authoritative in-game control reference.

## Physical play

The ball is a dynamic rigid body with a physical radius of 0.11 m (0.22 m diameter), mass approximately 0.43 kg and a fixed simulation step of 0.01 seconds. Robots use physical foot contact for dribbles, passes, receives, shots and tackles; proximity alone does not grant possession. The ball is not parented to a robot or teleported during live play.

The visible ball shell is intentionally larger for readability. Shooting may use that rendered boundary as bounded input assistance; dribble, pass, receive and collision checks continue to use the 0.11 m physical radius. The visual shell is not the physical ball size.

Head-on robot contact blocks or slows with bounded displacement. Glancing contact brushes past. Contact does not silently transfer possession or bulldoze a robot across the pitch. Normal board rebounds remain live. A complete exit outside a valid goal creates a short kick-in setup for the team that did not touch the ball last; opponents clear space and play resumes only after physical restart contact. A valid goal takes precedence.

The final exhibition run exercised the double-board corner recovery: physical board-clearance attempts run first, then one clearly logged neutral referee dead-ball drop occurs after four simulation seconds of a pinned corner. One such recovery occurred in each 60-second exhibition run; the score is preserved, no player is blamed and live possession is not teleported.

## Evidence and limitations

All three acceptance receipts pass at 1×, ½× and ¼×. They record four robots, Beginner/Gentle defaults, pass reception, shot contact, tackles, body contact, goals and kick-ins. Automated receipts do not constitute human approval of fun, feel, motion quality or overall playability.

The game has no online multiplayer, trained-model selection, persistent learning, goalkeeper specialization, offside, elaborate football laws, headers, high crosses, bicycle kicks, sliding tackles or deforming net simulation. The source snapshot retains historical M1 evidence as historical material; the final 2v2 receipts are the release evidence.

## Screenshots

![Four-robot match overview](Evidence/match-captures/match-default-four-robot-play-1.00.png)

![Pass reception](Evidence/match-captures/match-receive-1.00.png)

![Shot contact](Evidence/match-captures/match-shot-contact-1.00.png)

![Standing tackle contact](Evidence/match-captures/match-tackle-contact-1.00.png)

The images are representative rendered captures; the JSON receipts in `Evidence/` provide the measured acceptance evidence.

See [RELEASE-GUIDE.md](RELEASE-GUIDE.md) for player-facing details, [RELEASE-NOTES.md](RELEASE-NOTES.md) for v0.1.0 changes, and [PROVENANCE.md](PROVENANCE.md) for asset attribution and the public boundary.
