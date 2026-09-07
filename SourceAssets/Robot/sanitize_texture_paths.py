"""Sanitize the soccer asset's FBX texture reference without changing motion.

Run against RoboPlayerSoccer.blend after making a binary backup. It replaces
external image paths with a project-relative reference to the existing Unity
texture, saves the blend, and exports the same selected rig/mesh/actions.
"""
import hashlib
import json
from pathlib import Path
import struct

import bpy


ROOT = Path(__file__).resolve().parents[2]
BLEND = ROOT / "SourceAssets" / "Robot" / "RoboPlayerSoccer.blend"
FBX = ROOT / "UnityProject" / "Assets" / "Art" / "Robot" / "RoboPlayerSoccer.fbx"
TEXTURE = ROOT / "UnityProject" / "Assets" / "Art" / "Robot" / "robot-basecolor.png"
REPORT = ROOT / "Evidence" / "MotionReview" / "texture-path-sanitization.json"
LOCAL_USER_PATH_PREFIX = b"C:" + bytes((92,)) + b"Users" + bytes((92,))


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def relative_path_of_exact_length(length, leaf):
    """Make a valid relative path resolving to leaf, without changing FBX size."""
    leaf = leaf.encode("ascii")
    for parent_hops in range((length - len(leaf)) // 5 + 1):
        remaining = length - len(leaf) - parent_hops * 5
        if remaining >= 0 and remaining % 2 == 0:
            return b".\\" * (remaining // 2) + b"x\\..\\" * parent_hops + leaf
    raise RuntimeError("Could not construct same-length relative path")


def fbx_string_property_spans(data):
    """Return exact payload spans for binary-FBX string properties.

    This parses node/property lengths rather than scanning arbitrary binary
    bytes. Replacements are same-length, so node end offsets remain valid.
    """
    version = struct.unpack_from("<I", data, 23)[0]
    header_size = 25 if version >= 7500 else 13
    offset_width = 8 if version >= 7500 else 4
    strings = []

    def property_end(position):
        kind = chr(data[position])
        if kind == "S" or kind == "R":
            length = struct.unpack_from("<I", data, position + 1)[0]
            if kind == "S":
                strings.append((position + 5, length))
            return position + 5 + length
        if kind in "YCFIDL":
            return position + {"Y": 3, "C": 2, "F": 5, "I": 5, "D": 9, "L": 9}[kind]
        if kind in "bcdfil":
            length, _encoding, stored = struct.unpack_from("<III", data, position + 1)
            return position + 13 + stored
        raise RuntimeError(f"Unsupported FBX property type {kind!r}")

    def node_at(position, limit):
        if position + header_size > len(data):
            return limit
        if offset_width == 8:
            end, property_count, property_length = struct.unpack_from("<QQQ", data, position)
            name_length = data[position + 24]
        else:
            end, property_count, property_length = struct.unpack_from("<III", data, position)
            name_length = data[position + 12]
        if end == 0:
            return limit
        if end > len(data) or end <= position:
            raise RuntimeError("Invalid FBX node end offset")
        prop_position = position + header_size + name_length
        prop_end = prop_position + property_length
        if prop_end > end:
            raise RuntimeError("Invalid FBX property extent")
        for _ in range(property_count):
            prop_position = property_end(prop_position)
        if prop_position != prop_end:
            raise RuntimeError("FBX property-length mismatch")
        child_position = prop_end
        while child_position < end - header_size:
            next_child = node_at(child_position, end)
            if next_child <= child_position:
                break
            child_position = next_child
        return end

    position = 27
    while position < len(data) - header_size:
        next_position = node_at(position, len(data))
        if next_position <= position:
            break
        position = next_position
    return strings


def scrub_fbx_absolute_paths():
    data = bytearray(FBX.read_bytes())
    scrubbed = 0
    for start, length in fbx_string_property_spans(data):
        original = bytes(data[start:start + length])
        if not original.startswith(LOCAL_USER_PATH_PREFIX):
            continue
        leaf = "RoboPlayerSoccer.blend" if original.endswith(b".blend") else "robot-basecolor.png"
        data[start:start + length] = relative_path_of_exact_length(length, leaf)
        scrubbed += 1
    FBX.write_bytes(data)
    return scrubbed


def main():
    if not TEXTURE.is_file():
        raise RuntimeError("Expected existing Unity base-color texture")
    rigs = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH" and obj.find_armature()]
    if len(rigs) != 1 or not meshes:
        raise RuntimeError("Expected one armature with a skinned mesh")

    sanitized = []
    relative_texture = bpy.path.relpath(str(TEXTURE))
    for image in bpy.data.images:
        if image.type not in {"RENDER_RESULT", "COMPOSITING"}:
            sanitized.append(image.name)
            if image.packed_file:
                # The inherited source packs textures with an absolute export
                # provenance path. Externalize those same embedded pixels to
                # a path relative to this blend before the FBX export.
                image.unpack(method="WRITE_LOCAL")
            else:
                image.filepath = relative_texture

    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    bpy.ops.object.select_all(action="DESELECT")
    rigs[0].select_set(True)
    for mesh in meshes:
        mesh.select_set(True)
    bpy.context.view_layer.objects.active = rigs[0]
    bpy.ops.export_scene.fbx(
        filepath=str(FBX), use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0,
        path_mode="RELATIVE", embed_textures=False, use_metadata=False,
    )
    scrubbed_paths = scrub_fbx_absolute_paths()
    if LOCAL_USER_PATH_PREFIX in FBX.read_bytes():
        raise RuntimeError("FBX still contains a local user path after scrubbing")
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    report = {
        "operation": "Re-exported existing soccer rig and actions with project-relative texture references.",
        "blender": bpy.app.version_string,
        "sanitizedImageNames": sanitized,
        "relativeTextureReference": relative_texture,
        "scrubbedFbxAbsolutePaths": scrubbed_paths,
        "blendSha256": sha256(BLEND),
        "fbxSha256": sha256(FBX),
    }
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("TEXTURE_PATH_SANITIZATION " + json.dumps(report))


if __name__ == "__main__":
    main()
