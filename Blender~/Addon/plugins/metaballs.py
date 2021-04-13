
from ..core.plugin import Plugin
from ..core.scene_object import SceneObject

class Metaball(SceneObject):
    def on_destroy(self):
        """If the root metaball ends up destroyed - find a new root"""
        self.plugin.recalculate_root()

class MetaballsPlugin(Plugin):
    """
    Plugin to generate a single Metaball mesh object that contains
    the geometry of all metaballs in the scene.
    """
    OBJECT_NAME = '__METABALLS'

    meta: Metaball

    def on_enable(self):
        self.recalculate_root()

    def on_depsgraph_update(self, scene, depsgraph):
        """Handle changes to metaballs in the scene

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        for update in depsgraph.updates:
            if type(update.id) == bpy.types.Object and update.id.type == 'META':
                self.update_metaballs(update.id, depsgraph)
                return

    def update_metaballs(self, obj, depsgraph):
        """
        Args:
            obj (bpy.types.Object):             META type object that triggered the update
            depsgraph (bpy.types.Depsgraph):    Depsgraph used for evaluating metaballs
        """
        # If we don't have a metaballs scene object, use the input
        if not self.meta:
            self.meta = self.instantiate(Metaball, self.OBJECT_NAME, obj)

        # Mesh can (and will) change when *any* metaballs transform.
        # So we always push a recalculation update.
        self.meta.update_transform()
        self.meta.update_mesh(depsgraph)

    def recalculate_root(self):
        """Find a new "root" metaball in the scene"""
        self.meta = None

        for bpy_obj in bpy.context.scene.objects:
            if bpy_obj.type == 'META':
                self.update_metaballs(
                    bpy_obj,
                    bpy.context.evaluated_depsgraph_get()
                )
                break
