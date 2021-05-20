import bpy
from bpy.props import (EnumProperty, BoolProperty)

from .. import api
from ..core import (utils, interop, utils)

def on_update_display_mode(self, context):
    print('display mode updated')

def on_update_optimize(self, context):
    print('optimize mode updated')

class Mesh(api.Component):
    """
    Component attached to anything that can be represented as a Mesh in Unity
    """

    #: enum, default 0: Technique used to render this object within Unity
    display_mode: EnumProperty(
        name='Display Mode',
        description='Technique used to render this object within Unity',
        items=[
            # Enum items match ObjectDisplayMode in C#
            # First value will be cast to an int when passed to the bridge
            ('0', 'Material', '', 0),
            ('1', 'Normals', 'Show vertex normals in Unity', 1),
            ('2', 'Vertex Colors', 'Show vertex colors in Unity', 2),

            ('10', 'UV Checker', 'Show UV values in Unity', 10),
            ('11', 'UV2 Checker', 'Show UV2 values in Unity', 11),
            ('12', 'UV3 Checker', 'Show UV3 values in Unity', 12),
            ('13', 'UV4 Checker', 'Show UV4 values in Unity', 13),

            ('100', 'Hidden', 'Do not render this object in Unity', 100),
        ],
        update=on_update_display_mode
    )

    #: bool, default True: Optimize (compress) vertex data prior to sending to Unity.
    optimize: BoolProperty(
        name='Optimize',
        description='Optimize (compress) vertex data prior to sending to Unity. ' +
                    'With this option turned off - full loops will be transmitted which may negatively ' +
                    'impact performance or lookdev in Unity when compared with an export of the same mesh',
        default=True,
        update=on_update_optimize
    )

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
        return '{}_{}'.format(self._name[:40], hex(id(self)))

    def send_evaluated_mesh(self, depsgraph):
        """
        Attempt to evaluate a mesh from the associated `bpy.types.Object`
        using the provided depsgraph and send to Unity

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
        """
        mesh_uid = self.mesh_uid
        if not mesh_uid:
            return

        try:
            # We need to do both evaluated_get() and preserve_all_data_layers=True to ensure
            # that - if the mesh is instanced - modifiers are applied to the correct instances.
            eval_obj = self.bpy_obj.evaluated_get(depsgraph)
            mesh = eval_obj.to_mesh(
                preserve_all_data_layers=True,
                depsgraph=depsgraph
            )

            # TODO: preserve_all_data_layers is only necessary if instanced and modifier
            # stacks change per instance. Might be cheaper to turn this off automatically
            # if a mesh is used only once.

            # Ensure triangulated faces are available (only tris may be transferred)
            mesh.calc_loop_triangles()

            # A single (optional) vertex color layer can be passed through
            cols_ptr = None
            if len(mesh.vertex_colors) > 0 and len(mesh.vertex_colors[0].data) > 0:
                cols_ptr = mesh.vertex_colors[0].data[0].as_pointer()

            # Up to 4 (optional) UV layers can be passed through
            uv_ptr = [None] * 4
            for layer in range(len(mesh.uv_layers)):
                if len(mesh.uv_layers[layer].data) > 0:
                    uv_ptr[layer] = mesh.uv_layers[layer].data[0].as_pointer()

            interop.lib.CopyMeshDataNative(
                interop.get_string_buffer(mesh_uid),
                mesh.loops[0].as_pointer(),
                len(mesh.loops),
                mesh.loop_triangles[0].as_pointer(),
                len(mesh.loop_triangles),
                mesh.vertices[0].as_pointer(),
                len(mesh.vertices),
                cols_ptr,
                uv_ptr[0],
                uv_ptr[1],
                uv_ptr[2],
                uv_ptr[3]
            )

            # TODO: This would also aggregate custom vertex data streams
            # for all SceneObjects that are referencing the same bpy_obj.
            # We'd also need to disable instancing if there's any registered streams
            # since we can't guarantee that data isn't different per instance.
            # Streams need to be converted to some struct containing the stream info
            # (id, size, ptr) and pushed up to C# for diffing and syncing.

            # Release the temporary mesh
            eval_obj.to_mesh_clear()
        except Exception as e:
            utils.error('Could not update mesh', e)

    def on_update_mesh(self, depsgraph):
        """Callback from the runtime's depsgraph evaluation.

        This method gets executed per *unique* mesh_uid that
        had a geometry update this tick.

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
        """
        # Could do a lib.UpdateComponent here with interop but I don't want to
        # expose that to the end users...
        self.send_evaluated_mesh(depsgraph)

def register():
    api.register_component(Mesh)

def unregister():
    api.unregister_component(Mesh)
