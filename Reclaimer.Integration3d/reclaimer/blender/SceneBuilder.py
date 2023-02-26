import bpy
import bmesh
import itertools
from typing import cast
from typing import Dict, Tuple
from mathutils import Vector
from bpy.types import Context, Collection, Armature, EditBone, Object

from ..src.Scene import *
from ..src.Model import *
from ..src.Material import *

def create_model(context: Context, scene: Scene, model: Model):
    builder = ModelBuilder(context, scene, model)
    model_col = bpy.data.collections.new(model.name)
    context.collection.children.link(model_col)
    for r in model.regions:
        region_col = bpy.data.collections.new(r.name)
        model_col.children.link(region_col)
        for p in r.permutations:
            builder.build_mesh(region_col, r, p)

class ModelBuilder:
    _context: Context
    _scene: Scene
    _model: Model
    _armature: Armature
    _instances: Dict[Tuple[int, int], Object]

    def __init__(self, context: Context, scene: Scene, model: Model):
        self._context = context
        self._scene = scene
        self._model = model
        self._armature = None
        self._instances = dict()

    def create_bones(self) -> Armature:
        context, model = self._context, self._model
        print('creating armature')

        PREFIX = '' # TODO
        TAIL_VECTOR = (0.0, 5.0, 0.0)

        bpy.ops.object.add(type = 'ARMATURE', enter_editmode = True)
        armature = self._armature = cast(Armature, context.object.data)
        armature.name = f'{model.name} armature'

        for b in model.bones:
            bone = armature.edit_bones.new(PREFIX + b.name)
            bone.tail = TAIL_VECTOR

        for i, b in enumerate(model.bones):
            if b.parent_index >= 0:
                armature.edit_bones[i].parent = armature.edit_bones[b.parent_index]

        bpy.ops.object.mode_set(mode = 'OBJECT')

        return armature

    def create_markers(self):
        context, model = self._context, self._model
        print('creating markers')
        
        PREFIX = '' # TODO
        MODE = 'MESH' # TODO

        for m in model.markers:
            NAME = PREFIX + m.name
            for inst in m.instances:
                pass # TODO

    def build_mesh(self, collection: Collection, region: ModelRegion, permutation: ModelPermutation):
        context, scene, model = self._context, self._scene, self._model
        
        SPLIT_MODE = False # TODO

        for mesh_index in range(permutation.mesh_index, permutation.mesh_index + permutation.mesh_count):
            MESH_NAME = f'{region.name}:{permutation.name}'
            INSTANCE_KEY = (mesh_index, -1) # TODO: second element reserved for submesh index if mesh splitting enabled

            if INSTANCE_KEY in self._instances.keys():
                source = self._instances.get(INSTANCE_KEY)
                copy = cast(Object, source.copy()) # note: use source.data.copy() for a deep copy
                copy.name = MESH_NAME
                copy.matrix_world = permutation.transform
                collection.objects.link(copy)
                continue

            mesh_data = model.meshes[mesh_index]
            index_buffer = scene.index_buffer_pool[mesh_data.index_buffer_index]
            vertex_buffer = scene.vertex_buffer_pool[mesh_data.vertex_buffer_index]

            positions = list(Vector(v) for v in vertex_buffer.position_channels[0])
            normals = list(Vector(v) for v in vertex_buffer.normal_channels[0])
            faces = list(index_buffer.get_triangles(mesh_data))

            # blender doesnt like if we provide too many dimensions
            for v in itertools.chain(positions, normals):
                v.resize_3d()

            mesh = bpy.data.meshes.new(MESH_NAME)
            mesh.from_pydata(positions, [], faces)
            mesh.transform(mesh_data.vertex_transform)

            for p in mesh.polygons:
                p.use_smooth = True

            mesh.normals_split_custom_set_from_vertices(normals)
            mesh.use_auto_smooth = True # this is required in order for custom normals to take effect

            obj = bpy.data.objects.new(mesh.name, mesh)
            obj.matrix_world = permutation.transform
            collection.objects.link(obj)

            self._instances[INSTANCE_KEY] = obj
