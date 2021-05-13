import bpy
from .. import api

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
        return '{}_{}'.format(self.name[:40], hex(id(bpy_obj)))
