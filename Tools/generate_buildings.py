"""Generates a set of low-poly buildings and exports one FBX per type for Unity.

Run headless:
    blender --background --python Tools/generate_buildings.py -- <output_dir>

Every building is modelled inside a 1x1 footprint with its base sitting on z=0, so Unity can
drop it at y=0 and it stands on the floor, and a uniform scale of S gives it a footprint
half-width of exactly S/2 - which is the number BlackHoleController measures to decide whether
the hole is wide enough to swallow it.

Written against the Blender 2.9x API. No add-ons required.
"""

import math
import os
import sys

import bpy

# Material slots. Every building has exactly these two, in this order.
WALL = 0
ROOF = 1

HALF = 0.5  # the footprint half-width every building must reach, in both X and Y


# ---------------------------------------------------------------------------
# Tiny mesh builder: accumulates verts/faces/material indices, no bmesh needed.
# ---------------------------------------------------------------------------

def new_shape():
    return {"verts": [], "faces": [], "mats": []}


def add_box(shape, x0, y0, z0, x1, y1, z1, mat):
    i = len(shape["verts"])
    shape["verts"].extend([
        (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
        (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
    ])
    # Wound so every normal faces out.
    for quad in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
        shape["faces"].append(tuple(i + k for k in quad))
        shape["mats"].append(mat)


def add_gable_roof(shape, x0, y0, z0, x1, y1, z1, mat):
    """A pitched roof: rectangular base, ridge running along Y at the midpoint of X."""
    i = len(shape["verts"])
    mid = (x0 + x1) * 0.5
    shape["verts"].extend([
        (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
        (mid, y0, z1), (mid, y1, z1),
    ])
    shape["faces"].append((i + 0, i + 3, i + 2, i + 1))   # underside
    shape["faces"].append((i + 0, i + 1, i + 4))          # gable end
    shape["faces"].append((i + 2, i + 3, i + 5))          # gable end
    shape["faces"].append((i + 0, i + 4, i + 5, i + 3))   # slope
    shape["faces"].append((i + 1, i + 2, i + 5, i + 4))   # slope
    shape["mats"].extend([mat] * 5)


def add_cylinder(shape, cx, cy, radius, z0, z1, segments, mat):
    i = len(shape["verts"])
    for z in (z0, z1):
        for s in range(segments):
            angle = 2.0 * math.pi * s / segments
            shape["verts"].append((cx + radius * math.cos(angle), cy + radius * math.sin(angle), z))

    for s in range(segments):
        n = (s + 1) % segments
        shape["faces"].append((i + s, i + n, i + segments + n, i + segments + s))
        shape["mats"].append(mat)

    shape["faces"].append(tuple(i + s for s in range(segments - 1, -1, -1)))
    shape["mats"].append(mat)
    shape["faces"].append(tuple(i + segments + s for s in range(segments)))
    shape["mats"].append(mat)


def add_cone(shape, cx, cy, radius, z0, z1, segments, mat):
    i = len(shape["verts"])
    for s in range(segments):
        angle = 2.0 * math.pi * s / segments
        shape["verts"].append((cx + radius * math.cos(angle), cy + radius * math.sin(angle), z0))
    apex = len(shape["verts"])
    shape["verts"].append((cx, cy, z1))

    for s in range(segments):
        n = (s + 1) % segments
        shape["faces"].append((i + s, i + n, apex))
        shape["mats"].append(mat)

    shape["faces"].append(tuple(i + s for s in range(segments - 1, -1, -1)))
    shape["mats"].append(mat)


# ---------------------------------------------------------------------------
# The buildings. Each returns a shape plus its two colours.
# ---------------------------------------------------------------------------

def build_house():
    """Cottage: squat walls under a pitched roof whose eaves define the footprint."""
    shape = new_shape()
    add_box(shape, -0.40, -0.42, 0.0, 0.40, 0.42, 0.58, WALL)
    add_gable_roof(shape, -HALF, -HALF, 0.55, HALF, HALF, 0.95, ROOF)
    add_box(shape, 0.14, -0.16, 0.70, 0.26, -0.04, 1.10, ROOF)  # chimney
    return shape, (0.78, 0.71, 0.58), (0.55, 0.23, 0.18)


def build_shop():
    """Low storefront with an awning that reaches out to the full footprint."""
    shape = new_shape()
    add_box(shape, -0.42, -0.42, 0.0, 0.42, 0.42, 0.52, WALL)
    add_box(shape, -HALF, -HALF, 0.46, HALF, HALF, 0.58, ROOF)   # flat overhanging roof
    add_box(shape, -0.30, -HALF, 0.60, 0.30, -0.34, 0.78, ROOF)  # sign board
    return shape, (0.85, 0.80, 0.66), (0.20, 0.42, 0.45)


def build_block():
    """Two-storey office with a setback upper floor."""
    shape = new_shape()
    add_box(shape, -HALF, -HALF, 0.0, HALF, HALF, 0.78, WALL)
    add_box(shape, -0.38, -0.38, 0.78, 0.38, 0.38, 1.22, WALL)
    add_box(shape, -0.42, -0.42, 1.22, 0.42, 0.42, 1.30, ROOF)
    return shape, (0.62, 0.64, 0.68), (0.30, 0.31, 0.34)


def build_tower():
    """The landmark: a tall stepped tower, the one the hole has to work up to."""
    shape = new_shape()
    add_box(shape, -HALF, -HALF, 0.0, HALF, HALF, 0.30, WALL)     # plinth
    add_box(shape, -0.36, -0.36, 0.30, 0.36, 0.36, 1.65, WALL)    # shaft
    add_box(shape, -0.42, -0.42, 1.65, 0.42, 0.42, 1.78, ROOF)    # cornice
    add_box(shape, -0.24, -0.24, 1.78, 0.24, 0.24, 2.05, WALL)    # crown
    add_cone(shape, 0.0, 0.0, 0.26, 2.05, 2.35, 6, ROOF)          # spire
    return shape, (0.72, 0.66, 0.60), (0.34, 0.28, 0.42)


def build_silo():
    """Round grain silo with a conical cap - breaks up the boxiness of the rest."""
    shape = new_shape()
    add_cylinder(shape, 0.0, 0.0, HALF, 0.0, 1.15, 10, WALL)
    add_cone(shape, 0.0, 0.0, HALF, 1.15, 1.58, 10, ROOF)
    return shape, (0.80, 0.78, 0.72), (0.42, 0.45, 0.30)


def build_warehouse():
    """Wide barn for the biggest slot. The roof needs a real pitch: scaled up 3x and seen from
    the game's top-down camera, a shallow one just reads as an open crate."""
    shape = new_shape()
    add_box(shape, -HALF, -HALF, 0.0, HALF, HALF, 0.50, WALL)
    add_gable_roof(shape, -HALF, -HALF, 0.48, HALF, HALF, 1.02, ROOF)
    add_box(shape, -0.34, -0.20, 0.0, -0.30, 0.20, 0.38, ROOF)  # loading door
    return shape, (0.70, 0.72, 0.74), (0.48, 0.36, 0.24)


BUILDINGS = [
    ("Building_House", build_house),
    ("Building_Shop", build_shop),
    ("Building_Block", build_block),
    ("Building_Tower", build_tower),
    ("Building_Silo", build_silo),
    ("Building_Warehouse", build_warehouse),
]


# ---------------------------------------------------------------------------

def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    if not argv:
        raise SystemExit("usage: blender --background --python generate_buildings.py -- <output_dir>")
    return os.path.abspath(argv[0])


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.objects):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def make_material(name, colour):
    material = bpy.data.materials.new(name=name)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled is not None:
        principled.inputs["Base Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
        principled.inputs["Roughness"].default_value = 0.85
        principled.inputs["Metallic"].default_value = 0.0
    return material


def create_object(name, shape, wall_colour, roof_colour):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(shape["verts"], [], shape["faces"])
    mesh.validate()
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    mesh.materials.append(make_material(name + "_Wall", wall_colour))
    mesh.materials.append(make_material(name + "_Roof", roof_colour))
    for polygon, index in zip(mesh.polygons, shape["mats"]):
        polygon.material_index = index
        polygon.use_smooth = False

    return obj


def export_fbx(obj, output):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    bpy.ops.export_scene.fbx(
        filepath=output,
        use_selection=True,
        object_types={"MESH"},
        axis_forward="-Z",
        axis_up="Y",
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        path_mode="COPY",
        embed_textures=False,
    )


def main():
    output_dir = parse_args()
    os.makedirs(output_dir, exist_ok=True)

    for name, factory in BUILDINGS:
        clear_scene()
        shape, wall_colour, roof_colour = factory()
        obj = create_object(name, shape, wall_colour, roof_colour)

        bounds = [v.co for v in obj.data.vertices]
        base = min(v.z for v in bounds)
        top = max(v.z for v in bounds)
        reach = max(max(abs(v.x) for v in bounds), max(abs(v.y) for v in bounds))

        # These two invariants are what let Unity place and size these without guesswork.
        assert abs(base) < 1e-5, "{}: base sits at z={}, not on the floor".format(name, base)
        assert abs(reach - HALF) < 1e-5, "{}: footprint reaches {}, expected {}".format(name, reach, HALF)

        export_fbx(obj, os.path.join(output_dir, name + ".fbx"))
        print("BUILDING_OK {} tris={} height={:.2f}".format(
            name,
            sum(len(p.vertices) - 2 for p in obj.data.polygons),
            top,
        ))


if __name__ == "__main__":
    main()
