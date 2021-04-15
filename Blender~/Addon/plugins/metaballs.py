import bpy
from ..core.plugin import Plugin
from ..core.scene import SceneObject

class Metaball(SceneObject):
    """Single metaball mesh object that represents all metaballs in the Blender scene"""
    @property
    def mesh_uid(self) -> str:
        return '__METABALLS'

    def on_destroy(self):
        """If the root metaball ends up destroyed - find a new root"""
        print('DESTROY META INSTANCE')

# what if, instead, each metaball is a transform with some properties
# but we only actually track the mesh on one of them (a root one)

class MetaballsPlugin(Plugin):
    """
    Plugin to generate a single Metaball mesh object that contains
    the geometry of all metaballs in the scene.
    """
    meta: Metaball = None
    dirty: bool = False

    def on_add_bpy_object(self, bpy_obj):
        """
        Args:
            bpy_obj (bpy.types.Object)
        """
        if bpy_obj.type == 'META':
            self.dirty = True

    def on_remove_bpy_object(self, name):
        self.dirty = True

    def on_depsgraph_update(self, scene, depsgraph):
        """Handle changes to metaballs in the scene

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        meta = None
        for update in depsgraph.updates:
            if type(update.id) == bpy.types.Object and update.id.type == 'META':
                self.dirty = True
                break

        if self.dirty:
            self.update_metaballs(depsgraph)
            self.dirty = False

    def update_metaballs(self, depsgraph):
        """
        Args:
            depsgraph (bpy.types.Depsgraph):    Depsgraph used for evaluating metaballs
        """
        # If our metaball object invalidated, find a new root
        if not self.meta or not self.meta.valid:
            for bpy_obj in bpy.data.objects:
                if bpy_obj.type == 'META':
                    self.meta = self.instantiate(Metaball, bpy_obj.name, bpy_obj)
                    break
            else:
                self.meta = None

        if self.meta:
            self.meta.update_transform()
            self.meta.update_mesh(depsgraph)
