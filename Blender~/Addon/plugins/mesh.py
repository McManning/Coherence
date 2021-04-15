import bpy
from ..core.plugin import Plugin
from ..core.scene import SceneObject

class Mesh(SceneObject):
    pass

class MeshPlugin(Plugin):
    """
    Plugin to handle anything that can be represented as a Mesh
    with default behavior of syncing geometry with Coherence.
    """
    # def on_enable(self):
    #     Coherence.events.on_add_bpy_object_to_scene.append(self.on_add_bpy_object_to_scene)

    #   ^ Alternative API for uncommon events

    def on_add_bpy_object(self, bpy_obj):
        if bpy_obj.type in {'MESH', 'CURVE', 'SURFACE', 'FONT'}:
            self.instantiate(
                obj_type=Mesh,
                name=bpy_obj.name,
                bpy_obj=bpy_obj
            )

        # TODO: If Coherence is already running when this plugin loads
        # this event doesn't fire. Is that fine? Or should it get blasted
        # with the full list of objects in the scene currently so it can
        # register correctly?
        # If it doesn't get the full list - I could see some developers
        # wanting to hack around it to get the object list themselves, then
        # miss things like objects from other scenes referenced by the active one.
        # e.g. See MetaballsPlugin:recalculate_root()
