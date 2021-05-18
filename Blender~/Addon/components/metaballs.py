# Eating my own dog food with the new API
# metaballs.py

import bpy
from bpy.app.handlers import (
    depsgraph_update_post
)

from .. import api
from .. import core

# Global Metaball instance
__meta = None

class Metaballs(api.Component):
    @property
    def mesh_uid(self) -> str:
        # There is no base mesh, only an evaluated copy.
        # So return a custom UID to ensure this metaball
        # will send an updated mesh for on_update_mesh
        return '__METABALLS'

    def on_create(self):
        # Note: Depsgraph isn't available because
        # we don't execute this within a depsgraph update.
        self.send_evaluated_mesh(
            bpy.context.evaluated_depsgraph_get()
        )

    def on_update_mesh(self, depsgraph):
        self.send_evaluated_mesh(self, depsgraph)

    def on_update_transform(self):
        core.interop.update_transform(self.bpy_obj)

    def send_evaluated_mesh(self, depsgraph, preserve_all_data_layers: bool = True):
        """
        Attempt to evaluate a mesh from the associated `bpy.types.Object`
        using the provided depsgraph and send to Unity

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
            preserve_all_data_layers (bool): Preserve all data layers in the mesh, like UV maps
                                            and vertex groups. By default Blender only computes
                                            the subset of data layers needed for viewport display
                                            and rendering for better performance.
        """
        mesh_uid = self.mesh_uid
        if not mesh_uid:
            return

        raise NotImplementedError('TODO!')

        try:
            # ...
            pass
        except Exception as e:
            error('Could not send mesh', e)


def update_metaballs(depsgraph):
    """Update the transform and mesh for the Metaball singleton

    Args:
        depsgraph (bpy.types.Depsgraph)
    """
    global __meta

    # If our metaball is invalid, find a new root
    if not __meta or not __meta.valid:
        for obj in bpy.data.objects:
            if obj.type == 'META':
                __meta = api.add_component(obj, Metaball)
                break
        else:
            __meta = None

    if __meta:
        __meta.on_update_transform()
        __meta.on_update_mesh(depsgraph)

def on_depsgraph_update(scene, depsgraph):
    """Update the Metaball singleton on any changes to META objects

    Args:
        scene (bpy.types.Scene)
        depsgraph (bpy.types.Depsgraph)
    """
    # If *any* metaballs change, trigger a change on the global metaball.
    for update in depsgraph.updates:
        if type(update.id) == bpy.types.Object and update.id.type == 'META':
            update_metaballs(depsgraph)
            return

def register():
    api.register_component(Metaballs)
    depsgraph_update_post.append(on_depsgraph_update)

def unregister():
    if on_depsgraph_update in depsgraph_update_post:
        depsgraph_update_post.remove(on_depsgraph_update)

    api.unregister_component(Metaballs)
