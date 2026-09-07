# Robo Street Soccer — Asset Provenance

## Boundary

This is a new project. The Robo Open tennis repository is read-only source material. No tennis gameplay, learning, report, profile, database, log, transcript, credential or environment file is copied into this project. The user authorized public publication of this project and its Windows release; that authorization defines the publication scope but is not a blanket relicensing statement for third-party or upstream material.

## Curated candidates

| Soccer destination | Tennis source | Purpose | Status |
|---|---|---|---|
| `UnityProject/Assets/Art/Robot/RoboPlayer.fbx` | `TennisGame/Assets/Prototype/Models/RoboPlayer.fbx` | Skinned orange robot with 16-bone generic rig and six existing tennis-era clips | Approved for local prototype reuse; soccer suitability requires M0 validation |
| `UnityProject/Assets/Art/Robot/robot-basecolor.png` | `TennisGame/Assets/Prototype/Models/robot-basecolor.png` | Robot base-color texture | Approved for local prototype reuse |
| `SourceAssets/Robot/RoboPlayer.blend` | `SourceAssets/Robot/RoboPlayer.blend` | Editable robot mesh, skin and armature source | Approved for local prototype reuse; do not modify tennis original |

## Known facts and limits

- Blender inspection on 2026-09-06 found a roughly 1.784 m mesh and a 16-bone armature containing Root, Hips, Spine, Head, upper/lower arms, hands, thighs, shins and feet.
- The rig has foot bones but no separate toe joints. Soccer motion can use two-bone leg IK and foot rotation; detailed toe articulation is not promised.
- Robo Open documentation says the visual mesh originated from one Meshy image-to-3D generation followed by locally authored Blender rigging, skin repair and animation. No new Meshy call or credit spend is authorized or needed.
- No blanket redistribution license is asserted by the tennis repository. Preserve the recorded origin and applicable provider terms for every included asset. Do not claim that a public repository proves an upstream mesh or texture has a blanket license, is exclusive, or is the exact creator asset.

## Meshy attribution boundary

The visual direction and asset workflow were informed by the [original reference video](https://www.youtube.com/watch?v=DQfL_l5lRpk). The robot is an independently generated and locally modified visual reconstruction inspired by that workflow; it is not claimed to be the original creator's project asset or an exact copy. The local asset history records one Meshy image-to-3D generation followed by a custom Blender rig, skin repair and soccer motion work. Meshy's published ownership guidance says paid outputs are owned by the customer and free outputs are shared under CC BY 4.0; the public snapshot should preserve the applicable attribution and link to [Meshy's ownership guidance](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models) and [Terms of Use](https://www.meshy.ai/terms-of-use). This statement does not grant a blanket license over Unity, Blender or any upstream/reference material.

## Public package boundary

The public repository may contain readable source, project settings required to rebuild, public-safe documentation, sanitized evidence receipts and the final portable Windows package, with provenance and attribution preserved. Exclude Unity `Library`, `Temp`, `Logs`, `UserSettings`, generated caches, `.env` files, API keys, private gameplay logs, profile databases, raw agent transcripts, user names and unrelated personal assets. Keep private originals locally; exclusion from the public package is not deletion.
