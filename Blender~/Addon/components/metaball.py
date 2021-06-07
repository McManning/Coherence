# Eating my own dog food with the new API
# metaballs.py

import bpy
from bpy.props import (
    BoolProperty
)

from .. import api
from ..core import (utils, interop, utils)

class Metaball(api.Component):
    """The metaball component attaches itself to every metaball in the scene
        but it only renders the geometry as a single blob for one 'root' metaball.
    """

    #: bool: Is this the root metaball in the scene
    is_root: BoolProperty(options={'HIDDEN'})

    #: bool: Class bool to indicate whether we should push
    # an update with the shared metaballs geometry
    dirty = False

    @classmethod
    def poll(cls, obj):
        return obj.type == 'META'

    def get_material_name(self) -> str:
        """Unique identifier for the material attached to the object

        If there is no bpy_obj or no active material, this returns None.

        Returns:
            Union[str, None]
        """
        obj = self.bpy_obj
        if not obj or not obj.active_material:
            return None

        return obj.active_material.name

    def on_update(self, depsgraph, update):
        # If *any* metaballs update, we dirty the root
        self.dirty = True

    def on_after_depsgraph_updates(self, depsgraph):
        """Executed after all depsgraph updates have been processed

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
        """
        if not self.dirty:
            return

        # Flag this instance as root or not. "Root" can change
        # based on metaballs being added/removed from the scene.
        is_root = bpy.data.metaballs[0] == self.bpy_obj.data
        if is_root != self.property_group.is_root:
            self.property_group.is_root = is_root

        # Only the root may send geometry data to Unity
        if is_root:
            self.send_evaluated_mesh(depsgraph)

    def send_evaluated_mesh(self, depsgraph):
        """
        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
        """
        mesh_id = '__METABALLS'

        # try:
        eval_obj = self.bpy_obj.evaluated_get(depsgraph)
        mesh = eval_obj.to_mesh()

        mesh.calc_loop_triangles()

        interop.lib.CopyMeshDataNative(
            interop.get_string_buffer(mesh_id),
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.loop_triangles[0].as_pointer(),
            len(mesh.loop_triangles),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices),
            None, # colors
            None, # uv
            None, # uv2
            None, # uv3
            None  # uv4
        )

        eval_obj.to_mesh_clear()
        # except Exception as e:
            # utils.error('Could not update mesh', e)

def register():
    api.register_component(Metaball)

def unregister():
    api.unregister_component(Metaball)
