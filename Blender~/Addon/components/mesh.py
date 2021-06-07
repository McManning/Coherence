import bpy
from bpy.props import (EnumProperty, BoolProperty, StringProperty)

from .. import api
from ..core import (utils, interop, utils)

class Mesh(api.Component):
    """
    Component attached to anything that can be represented as a Mesh in Unity.
    """

    #: Set[str]: Shared set of mesh UIDs that have updated within a depsgraph update event.
    # This is shared between all Mesh components to support mesh instancing.
    geometry_updates = set()

    #: enum, default 0: Technique used to render this object within Unity
    display_mode: EnumProperty(
        name='Display Mode',
        description='Technique used to render this object within Unity',
        items=[
            # Enum items match ObjectDisplayMode in C#
            # First value will be cast to an int when passed to the bridge
            ('Material', 'Material', '', 0),
            ('Normals', 'Normals', 'Show vertex normals in Unity', 1),
            ('VertexColors', 'Vertex Colors', 'Show vertex colors in Unity', 2),

            ('UV', 'UV Checker', 'Show UV values in Unity', 10),
            ('UV2', 'UV2 Checker', 'Show UV2 values in Unity', 11),
            ('UV3', 'UV3 Checker', 'Show UV3 values in Unity', 12),
            ('UV4', 'UV4 Checker', 'Show UV4 values in Unity', 13),

            ('Hidden', 'Hidden', 'Do not render this object in Unity', 100),
        ]
    )

    #: bool, default True: Optimize (compress) vertex data prior to sending to Unity.
    optimize: BoolProperty(
        name='Optimize',
        description='Optimize (compress) vertex data prior to sending to Unity. ' +
                    'With this option turned off - full loops will be transmitted which may negatively ' +
                    'impact performance or lookdev in Unity when compared with an export of the same mesh',
        default=True
    )

    #: string: Mesh ID attached to this object
    mesh_id: StringProperty(options={'HIDDEN'})

    #: string: Active material for the mesh
    material_name: StringProperty(options={'HIDDEN'})

    @classmethod
    def poll(cls, obj):
        return obj.type in {'MESH', 'CURVE', 'SURFACE', 'FONT'}

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

    def get_mesh_id(self) -> str:
        """Unique identifier for the mesh attached to the object

        If the object has modifiers applied - this will be unique for
        that object. Otherwise - this may be a common mesh name that
        is instanced between multiple objects in the scene.

        Returns:
            Union[str, None]
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
        # name for this object + mesh combination to avoid overwriting
        # another instance of the same mesh with different modifiers.
        # This ends up something like `Cube.001_0x1d4d765ca40`
        return '{}_{}'.format(self._name[:40], hex(id(self)))

    def on_update(self, depsgraph, update):
        """Handle a depsgraph update for the linked `bpy.types.Object`

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
            update (bpy.types.DepsgraphUpdate): Update for the linked object
        """
        print('Depsgraph update for {} - is_updated_geometry={}'.format(self.bpy_obj, update.is_updated_geometry))
        if not update.is_updated_geometry:
            return

        mesh_id = self.get_mesh_id()

        # If this was the first component to receive a geometry update
        # on the given mesh, push geometry data to LibCoherence.
        if mesh_id not in self.geometry_updates:
            self.geometry_updates.add(mesh_id)
            self.send_evaluated_mesh(depsgraph, update)

        # Reassign mesh ID if it was changed
        if mesh_id != self.property_group.mesh_id:
            self.property_group.mesh_id = mesh_id

    def on_after_depsgraph_updates(self, depsgraph):
        """Executed after all depsgraph updates have been processed

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
        """
        # Clear the geometry list that was updated this frame.
        self.geometry_updates.clear()

        # Reassign material ID if it was changed.
        # We do this after all depsgraph updates because a material could
        # be renamed in Blender but our associated bpy obj won't get a
        # depsgraph update event for it, despite using it.
        material_name = self.get_material_name()
        if material_name != self.property_group.material_name:
            self.property_group.material_name = material_name


    def send_evaluated_mesh(self, depsgraph, update):
        """
        Attempt to evaluate a mesh from the linked `bpy.types.Object`
        using the provided depsgraph and send to Unity

        Args:
            depsgraph (bpy.types.Depsgraph):    Evaluated dependency graph
            update (bpy.types.DepsgraphUpdate): Information about the ID that was updated
        """
        mesh_id = self.get_mesh_id()
        if not mesh_id:
            return

        try:
            # We need to do both evaluated_get() and preserve_all_data_layers=True to ensure
            # that - if the mesh is instanced - modifiers are applied to the correct instances.
            # eval_obj = self.bpy_obj.evaluated_get(depsgraph)
            mesh = update.id.to_mesh(
                # preserve_all_data_layers=True,
                # depsgraph=depsgraph

                # ^ These two do NOT work if we're not in render preview mode.
                # The mesh pointer will change but the underlying vertex data will not.

                # For instancing, Blender's own behaviour is to disable modifiers for all
                # instances while modifying one of them, and then re-enable modifiers once
                # we exit edit mode. This *could* be done in Unity by replacing all instances
                # with the base version while one is in edit. And then re-evaluating them all
                # with modifiers once the edits are locked in - but we have no way of currently
                # identifying what "locked in" means here.

                # If the mesh isn't in edit focus - then don't apply modifiers?
                # but then what about animation / etc. ...
            )

            # preserve and depsgraph are both required.

            # to_mesh preserve=True and no depsgraph
            #   = mesh is null
            # to_mesh

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

            co = mesh.vertices[0].co
            print('SEND EVAL vertices={} x={}, y={}, z={}'.format(mesh.vertices[0].as_pointer(), co[0], co[1], co[2]))

            # pointer changes, but the coordinates stay the same...
            # UNLESS I'm in render view... has to be evaluated_get related... but
            # I swear I dealt with this

            interop.lib.CopyMeshDataNative(
                interop.get_string_buffer(mesh_id),
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
            # eval_obj.to_mesh_clear()
            update.id.to_mesh_clear()
        except Exception as e:
            utils.error('Could not update mesh', e)

    def draw(self, layout):
        layout.label(text='Example override')
        super().draw(layout)

def register():
    api.register_component(Mesh)

def unregister():
    api.unregister_component(Mesh)
