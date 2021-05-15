import bpy
from .. import api
from ..util import error

class Mesh(api.Component):
    """
    Component attached to anything that can be represented as a Mesh in Unity
    """
    @classmethod
    def poll(cls, obj):
        return obj.type in {'MESH', 'CURVE', 'SURFACE', 'FONT'}

    @property
    def mesh_uid(self):
        """Union[str, None]: Unique identifier for the mesh attached to the object

        If the object has modifiers applied - this will be unique for
        that object. Otherwise - this may be a common mesh name that
        is instanced between multiple objects in the scene.
        """
        bpy_obj = self.bpy_obj

        # No evaluateable object (could be attached to the scene).
        # Thus no mesh.
        if not bpy_obj:
            return None

        # Text/Curves/etc all have evaluated meshes unique to that instance.
        # TODO: These *can* be instanced as well, but I don't know of a
        # consistent way of handling that at the moment.
        if bpy_obj.type != 'MESH':
            return bpy_obj.name

        # If there are no modifiers - return the unique mesh name.
        # This mesh may be instanced between multiple objects.
        has_modifiers = len(bpy_obj.modifiers) > 0
        if not has_modifiers:
            return bpy_obj.data.name

        # If there are modifiers - we need to generate a unique
        # name for this object + mesh combination.
        # This ends up something like `Cube.001_0x1d4d765ca40`
        return '{}_{}'.format(self.name[:40], hex(id(self)))

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

        try:
            # ...
            pass
        except Exception as e:
            error('Could not send mesh', e)

    def on_update_mesh(self, depsgraph):
        """Callback from the runtime's depsgraph evaluation.

        This method gets executed per *unique* mesh_uid that
        had a geometry update this tick.

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
        """
        self.send_evaluated_mesh(self, depsgraph)
