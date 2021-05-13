# Eating my own dog food with the new API
# metaballs.py

import bpy
from bpy.app.handlers import (
    depsgraph_update_post
)

from .. import api

# Global Metaball instance
__meta = None

class Metaball(api.Component):
    @property
    def mesh_uid(self) -> str:
        # There is no base mesh, only an evaluated copy.
        # So return a custom UID to ensure this metaball
        # will send an updated mesh for on_update_mesh
        return '__METABALLS'

    def on_update_mesh(self, depsgraph):
        self.update_mesh(depsgraph) # Rename to "send_evaluated_mesh" ?


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
        __meta.update_transform()
        __meta.update_mesh(depsgraph)

def on_depsgraph_update(scene, depsgraph):
    """Update the Metaball singleton on any changes to META objects

    Args:
        scene (bpy.types.Scene)
        depsgraph (bpy.types.Depsgraph)
    """
    # If any metaballs change, trigger a change on the global metaball.
    for update in depsgraph.updates:
        if type(update.id) == bpy.types.Object and update.id.type == 'META':
            update_metaballs(depsgraph)
            return

def register():
    api.register_component(Metaball)
    depsgraph_update_post.append(on_depsgraph_update)

def unregister():
    if on_depsgraph_update in depsgraph_update_post:
        depsgraph_update_post.remove(on_depsgraph_update)

    api.unregister_component(Metaball)
