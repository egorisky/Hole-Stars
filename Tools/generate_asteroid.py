"""Generates a low-poly asteroid and exports it as an FBX for Unity.

Run headless:
    blender --background --python Tools/generate_asteroid.py -- <output.fbx> [seed]

Kept deliberately dependency-free (no add-ons) so it runs on a bare Blender install.
Written against the Blender 2.9x API.
"""

import os
import sys

import bpy
from mathutils import Vector, noise

NAME = "Asteroid_Test"

# Subdivision 2 gives an 80-tri icosphere - enough facets to read as a rock at gameplay
# distance, few enough that the flat shading still shows.
SUBDIVISIONS = 2

# How far vertices are allowed to travel along their own normal, as a fraction of the radius.
# Past ~0.35 the silhouette starts self-intersecting on the sharper icosphere corners.
LUMPINESS = 0.28


def parse_args():
    """Everything after the standalone '--' belongs to this script, not to Blender."""
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []

    if not argv:
        raise SystemExit("usage: blender --background --python generate_asteroid.py -- <output.fbx> [seed]")

    output = os.path.abspath(argv[0])
    seed = int(argv[1]) if len(argv) > 1 else 20260815
    return output, seed


def clear_scene():
    """The startup file ships with a cube, a camera and a light - none of them wanted in the FBX."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def build_asteroid(seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=SUBDIVISIONS, radius=1.0, location=(0.0, 0.0, 0.0))
    obj = bpy.context.active_object
    obj.name = NAME
    obj.data.name = NAME

    # Offsetting the noise field per seed gives a different rock each run without touching
    # the topology, so every asteroid stays the same tri count and stays budget-predictable.
    noise.seed_set(seed)
    offset = Vector((seed % 97, seed % 89, seed % 83))

    for vertex in obj.data.vertices:
        direction = vertex.co.normalized()

        # Two octaves: the low one carves the overall potato shape, the high one chips the facets.
        base = noise.noise(direction * 1.6 + offset)
        detail = noise.noise(direction * 4.7 + offset) * 0.35

        vertex.co = direction * (1.0 + (base + detail) * LUMPINESS)

    # Squash slightly so it never reads as a ball, and bake it into the mesh so Unity gets a
    # clean 1,1,1 scale rather than a transform the physics would have to fight.
    obj.scale = (1.0, 0.86, 0.93)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    for polygon in obj.data.polygons:
        polygon.use_smooth = False

    return obj


def assign_material(obj):
    material = bpy.data.materials.new(name="Asteroid_Mat")
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled is not None:
        principled.inputs["Base Color"].default_value = (0.31, 0.29, 0.27, 1.0)
        principled.inputs["Roughness"].default_value = 0.9
        principled.inputs["Metallic"].default_value = 0.0
    obj.data.materials.append(material)


def export_fbx(obj, output):
    os.makedirs(os.path.dirname(output), exist_ok=True)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    bpy.ops.export_scene.fbx(
        filepath=output,
        use_selection=True,
        object_types={"MESH"},
        # Blender is Z-up, Unity is Y-up; these are the axes Unity's FBX importer expects.
        axis_forward="-Z",
        axis_up="Y",
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        # FACE keeps the flat shading baked in, instead of Unity re-smoothing the facets away.
        mesh_smooth_type="FACE",
        path_mode="COPY",
        embed_textures=False,
    )


def main():
    output, seed = parse_args()

    clear_scene()
    asteroid = build_asteroid(seed)
    assign_material(asteroid)
    export_fbx(asteroid, output)

    print("ASTEROID_OK verts={} tris={} -> {}".format(
        len(asteroid.data.vertices),
        sum(len(p.vertices) - 2 for p in asteroid.data.polygons),
        output,
    ))


if __name__ == "__main__":
    main()
