"""Author soccer-specific motion on the copied RoboPlayer rig and export Unity FBX.

Run with Blender 5.2.1 from the project root. This script never reads or writes
the tennis project. Coordinates use Blender rig space: -Y forward, +Z up.
"""
import bpy
import json
import math
import pathlib
from mathutils import Matrix, Quaternion, Vector

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT_BLEND = ROOT / "SourceAssets/Robot/RoboPlayerSoccer.blend"
OUT_FBX = ROOT / "UnityProject/Assets/Art/Robot/RoboPlayerSoccer.fbx"
OUT_REPORT = ROOT / "Evidence/m0-rig-and-motion.json"

DURATIONS = {
    "Idle": 60,
    "Run": 30,
    "Turn": 30,
    "Dribble": 24,
    "Receive": 26,
    "Pass": 34,
    "Shoot": 42,
    "Tackle": 32,
    "ContactLean": 24,
}
CONTACTS = {"Dribble": 11, "Receive": 11, "Pass": 16, "Shoot": 19, "Tackle": 14}

rigs = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
meshes = [obj for obj in bpy.data.objects if obj.type == "MESH" and obj.find_armature()]
if len(rigs) != 1 or not meshes:
    raise RuntimeError(f"Expected one armature and skinned meshes, found rigs={len(rigs)} meshes={len(meshes)}")
rig = rigs[0]
required = {
    "Root", "Hips", "Spine", "Head",
    "Thigh.L", "Shin.L", "Foot.L", "Thigh.R", "Shin.R", "Foot.R",
    "UpperArm.L", "Forearm.L", "Hand.L", "UpperArm.R", "Forearm.R", "Hand.R",
}
missing = sorted(required - {bone.name for bone in rig.data.bones})
if missing:
    raise RuntimeError(f"Missing required bones: {missing}")

for track in list(rig.animation_data.nla_tracks if rig.animation_data else []):
    rig.animation_data.nla_tracks.remove(track)
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)
rig.animation_data_create()
bpy.context.scene.render.fps = 30
for bone in rig.pose.bones:
    bone.rotation_mode = "QUATERNION"

def refresh():
    bpy.context.view_layer.update()

def reset_pose():
    for bone in rig.pose.bones:
        bone.location = (0, 0, 0)
        bone.rotation_quaternion = Quaternion()

def rotate(name, axis, degrees):
    bone = rig.pose.bones[name]
    local_axis = bone.bone.matrix_local.to_quaternion().inverted() @ Vector(axis)
    bone.rotation_quaternion = Quaternion(local_axis, math.radians(degrees))

def aim(bone, target):
    refresh()
    matrix = bone.matrix.copy()
    direction = Vector(target) - bone.head
    if direction.length < 1e-6:
        return
    rotation = (matrix.to_3x3() @ Vector((0, 1, 0))).rotation_difference(direction.normalized())
    bone.matrix = Matrix.Translation(bone.head) @ rotation.to_matrix().to_4x4() @ matrix.to_3x3().to_4x4()
    refresh()

def solve_two_bone(upper, lower, end, target, pole):
    refresh()
    a = rig.pose.bones[upper]
    b = rig.pose.bones[lower]
    origin = a.head.copy()
    l1, l2 = a.length, b.length
    delta = Vector(target) - origin
    distance = max(abs(l1 - l2) + .002, min(delta.length, l1 + l2 - .008))
    axis = delta.normalized()
    bend = Vector(pole) - origin
    bend -= axis * bend.dot(axis)
    if bend.length < .0001:
        bend = Vector((0, -1, 0)).cross(axis)
    bend.normalize()
    along = (l1*l1 - l2*l2 + distance*distance) / (2*distance)
    joint = origin + axis*along + bend*math.sqrt(max(0, l1*l1 - along*along))
    aim(a, joint)
    aim(b, origin + axis*distance)

def smoothstep(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)

def sample(keys, frame):
    for index in range(len(keys) - 1):
        fa, va = keys[index]
        fb, vb = keys[index + 1]
        if frame <= fb:
            t = smoothstep((frame - fa) / max(1, fb - fa))
            result = {}
            for key, value in va.items():
                other = vb[key]
                result[key] = Vector(value).lerp(Vector(other), t) if isinstance(value, tuple) else value + (other - value) * t
            return result
    return dict(keys[-1][1])

READY = {
    "right_foot": (-.245, -.015, .16), "left_foot": (.245, -.015, .16),
    "right_hand": (-.46, -.02, .76), "left_hand": (.46, -.02, .76),
    "hips": 0.0, "turn": 0.0, "lean": 0.0, "crouch": .025, "lift": 0.0,
}

def pose(**changes):
    result = dict(READY)
    result.update(changes)
    return result

TIMELINES = {
    "Dribble": [(0, READY), (7, pose(right_foot=(-.245, .06, .18), left_foot=(.245, -.02, .16), lean=4, turn=-4)),
                (11, pose(right_foot=(-.18, -.28, .145), left_foot=(.245, -.02, .16), lean=6, turn=7)),
                (17, pose(right_foot=(-.245, -.08, .17), left_foot=(.245, -.02, .16), lean=3)), (24, READY)],
    "Receive": [(0, READY), (7, pose(right_foot=(-.20, -.29, .15), left_foot=(.25, .02, .16), crouch=.055, lean=2)),
                (11, pose(right_foot=(-.19, -.32, .145), left_foot=(.25, .02, .16), crouch=.075, lean=-3)),
                (18, pose(right_foot=(-.19, -.32, .145), left_foot=(.25, .02, .16), crouch=.075, lean=-3)),
                (22, pose(right_foot=(-.22, -.16, .15), left_foot=(.25, .02, .16), crouch=.05, lean=-5)), (26, READY)],
    "Pass": [(0, READY), (8, pose(right_foot=(-.25, .13, .18), left_foot=(.25, -.03, .16), crouch=.06, hips=-12, turn=-8)),
             (16, pose(right_foot=(-.18, -.34, .145), left_foot=(.25, -.03, .16), crouch=.025, hips=14, turn=12, lean=5)),
             (24, pose(right_foot=(-.21, -.22, .18), left_foot=(.25, -.03, .16), hips=18, turn=15, lean=7)), (34, READY)],
    "Shoot": [(0, READY), (10, pose(right_foot=(-.27, .22, .23), left_foot=(.26, -.07, .16), crouch=.09, hips=-20, turn=-15, lean=-5)),
              (19, pose(right_foot=(-.16, -.39, .16), left_foot=(.26, -.07, .16), crouch=.02, hips=22, turn=18, lean=8)),
              (28, pose(right_foot=(-.18, -.30, .30), left_foot=(.26, -.07, .16), hips=28, turn=22, lean=11)), (42, READY)],
    "Tackle": [(0, READY), (8, pose(right_foot=(-.22, -.16, .17), left_foot=(.27, .02, .16), crouch=.10, lean=7)),
               (14, pose(right_foot=(-.14, -.42, .14), left_foot=(.27, .02, .16), crouch=.13, lean=14, turn=7)),
               (21, pose(right_foot=(-.19, -.27, .16), left_foot=(.27, .02, .16), crouch=.10, lean=9)), (32, READY)],
    "ContactLean": [(0, READY), (7, pose(lean=-7, crouch=.04)), (12, pose(lean=-11, crouch=.06)), (18, pose(lean=4, crouch=.04)), (24, READY)],
}

for kind, end_frame in DURATIONS.items():
    action = bpy.data.actions.new(kind)
    action.use_fake_user = True
    rig.animation_data.action = action
    if kind in CONTACTS:
        action.pose_markers.new("CONTACT").frame = CONTACTS[kind] + 1
    for frame in range(end_frame + 1):
        reset_pose()
        t = frame / end_frame
        cfg = sample(TIMELINES[kind], frame) if kind in TIMELINES else dict(READY)
        if kind == "Idle":
            cfg["crouch"] = .025 + .008 * math.sin(t * math.tau)
            cfg["turn"] = 1.5 * math.sin(t * math.tau)
        elif kind == "Run":
            cycle = t * math.tau
            stride = math.sin(cycle)
            # A low, flexed support leg gives runtime world-space planting room.
            # The raised leg alone advances; the support foot holds its local
            # pose until the next step instead of tracing a treadmill arc.
            right_swing = max(0, stride)
            left_swing = max(0, -stride)
            cfg["right_foot"] = (-.245, -.06 - .22*right_swing, .145 + right_swing*.115)
            cfg["left_foot"] = (.245, -.06 - .22*left_swing, .145 + left_swing*.115)
            cfg["right_hand"] = (-.46, -.02 + .10*stride, .78)
            cfg["left_hand"] = (.46, -.02 - .10*stride, .78)
            cfg["hips"] = -4*stride
            cfg["turn"] = 5*stride
            cfg["lean"] = 6
            cfg["crouch"] = .14
            cfg["lift"] = abs(stride)*.01
        elif kind == "Turn":
            cycle = t * math.tau
            s = math.sin(cycle)
            cfg["right_foot"] = (-.245 - .035*s, -.015 - .08*s, .16 + max(0, s)*.055)
            cfg["left_foot"] = (.245 - .035*s, -.015 + .08*s, .16 + max(0, -s)*.055)
            cfg["hips"] = 12*s
            cfg["turn"] = 18*s
            cfg["lean"] = 5
        root = rig.pose.bones["Root"]
        root.location = root.bone.matrix_local.to_quaternion().inverted() @ Vector((0, 0, cfg["lift"] - cfg["crouch"]))
        rotate("Hips", (0, 0, 1), cfg["hips"])
        rotate("Spine", (0, 0, 1), cfg["turn"] - cfg["hips"])
        rotate("Head", (0, 0, 1), -cfg["turn"] * .55)
        rig.pose.bones["Spine"].rotation_quaternion @= Quaternion((1, 0, 0), math.radians(cfg["lean"]))
        refresh()
        for side, sign in (("R", -1), ("L", 1)):
            foot = Vector(cfg["right_foot" if side == "R" else "left_foot"])
            pole = Vector((sign*.27, -.8, .42))
            solve_two_bone(f"Thigh.{side}", f"Shin.{side}", f"Foot.{side}", foot, pole)
            aim(rig.pose.bones[f"Foot.{side}"], rig.pose.bones[f"Foot.{side}"].head + Vector((0, -1, 0)))
            hand = Vector(cfg["right_hand" if side == "R" else "left_hand"])
            hand.x += sign * min(.08, abs(cfg["turn"]) / 300)
            elbow = Vector((sign*.72, .02, .96))
            solve_two_bone(f"UpperArm.{side}", f"Forearm.{side}", f"Hand.{side}", hand, elbow)
            aim(rig.pose.bones[f"Hand.{side}"], rig.pose.bones[f"Hand.{side}"].head + Vector((0, -.4, .55)))
        for bone in rig.pose.bones:
            bone.keyframe_insert("rotation_quaternion", frame=frame+1, group=bone.name)
            bone.keyframe_insert("location", frame=frame+1, group=bone.name)
    track = rig.animation_data.nla_tracks.new()
    track.name = kind
    track.strips.new(kind, 1, action)
    track.mute = True

rig.animation_data.action = bpy.data.actions["Idle"]
bpy.context.scene.frame_set(1)
refresh()

OUT_BLEND.parent.mkdir(parents=True, exist_ok=True)
OUT_FBX.parent.mkdir(parents=True, exist_ok=True)
OUT_REPORT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT_BLEND))
bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
for mesh in meshes:
    mesh.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=str(OUT_FBX), use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0, path_mode="COPY", embed_textures=False,
)

vertices = sum(len(mesh.data.vertices) for mesh in meshes)
dimensions = [round(value, 4) for value in meshes[0].dimensions]
report = {
    "blender": bpy.app.version_string,
    "armature": rig.name,
    "bones": [bone.name for bone in rig.data.bones],
    "boneCount": len(rig.data.bones),
    "meshCount": len(meshes),
    "vertices": vertices,
    "firstMeshDimensions": dimensions,
    "actions": DURATIONS,
    "contactsAt30Fps": CONTACTS,
    "toeBones": False,
    "method": "Analytical two-bone IK with explicit knee poles, stance targets and contact-marked soccer actions",
}
OUT_REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
print("ROBO_SOCCER_MOTION " + json.dumps(report))
