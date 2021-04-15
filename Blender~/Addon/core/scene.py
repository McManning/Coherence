
import bpy

from . import runtime

from .utils import (
    get_mesh_uid,
    get_material_uid,
    get_string_buffer,
    debug,
    error
)

from .interop import (
    to_interop_transform
)

class SceneObject:
    """
    An object synced through Coherence that contains a transform and optional mesh.
    """
    def __init__(self, name, bpy_obj, plugin):
        """
        Args:
            name (str):                         Unique object name
            bpy_obj (bpy.types.Object|None):    Associated Blender object
            plugin (Plugin):                    Instantiating plugin
        """
        self._name = name
        self._bpy_name = bpy_obj.name if bpy_obj else None
        self._plugin = plugin
        self._valid = True

    @property
    def kind(self) -> str:
        """Get what type of object this is

        Returns:
            str: One of MESH|METABALL|CUSTOM
        """
        return self.__name__

    @property
    def name(self) -> str:
        return self._name

    @property
    def uid(self) -> str:
        """Get a unique identifier for this SceneObject

        Returns:
            str
        """
        # TODO: Cache and make more unique
        # (multiple objects could have the same name across different plugins)
        return self.name

    @property
    def bpy_name(self) -> str:
        """
        Name of the associated `bpy.types.Object` or `None` if this is
        independent of any Blender scene object.

        Returns:
            str|None
        """
        return self._bpy_name

    @property
    def bpy_obj(self):
        """Get the Blender object associated with this instance.

        Avoid holding onto a reference to this value long term, as it
        will invalidate out from under you like other StructRNA references.

        Returns:
            bpy.types.Object|None
        """
        if not self._bpy_name:
            return None

        # Fetch a fresh reference to the object
        return bpy.data.objects[self.bpy_name]

    @property
    def mesh_uid(self) -> str:
        """Retrieve a unique identifier for the mesh attached to the object

        If the object has modifiers applied - this will be unique for
        that object. Otherwise - this may be a common mesh name that
        is instanced between multiple objects in the scene.

        Returns:
            string|None
        """
        bpy_obj = self.bpy_obj
        if not bpy_obj:
            return None

        # Text/Curves/etc all have evaluated meshes unique to that instance.
        # TODO: These *can* be instanced as well, but I don't know of a
        # consistent way of handling that at the moment.
        if bpy_obj.type != 'MESH':
            return bpy_obj.name

        has_modifiers = len(bpy_obj.modifiers) > 0

        # If there are no modifiers - return the unique mesh name.
        # This mesh may be instanced between multiple objects.
        if not has_modifiers:
            return bpy_obj.data.name

        # If there are modifiers - we need to generate a unique
        # name for this object + mesh combination.

        # TODO: Better convention here. If this is > 63 characters
        # it won't transfer. And this can still have a collision:
        # Mesh named `foo__bar` can collide with a `foo` object
        # with a `bar` mesh + modifiers.
        return '{}__{}'.format(self.name, bpy_obj.data.name)

    @property
    def mat_uid(self) -> str:
        """Retrieve a unique identifier for the object's Material, if set

        Returns:
            str|None
        """
        bpy_obj = self.bpy_obj
        if not bpy_obj:
            return None

        # TODO: Cache
        return get_material_uid(bpy_obj.active_material)

    @property
    def plugin(self):
        """
        Returns:
            Plugin
        """
        return self._plugin

    @property
    def valid(self) -> bool:
        """Returns true if this object is still valid in the synced scene.

        Objects will be invalidated when .destroy is called on them.
        An object must be recreated through `Plugin.instantiate()` once invalidated.

        Returns:
            bool
        """
        return self._valid

    def on_create(self):
        """Executes after the object has been created and synced to Coherence."""
        pass

    def on_destroy(self):
        """
        Executes when this object has been destroyed, either through
        calling `.destroy()` or by a desync within Coherence.
        """
        pass

    def on_message(self, id: str, callback):
        """
        Add an event handler for when Unity sends a custom
        message for this object (e.g. through a Unity-side plugin
        that handles these specific custom objects)

        `def callback(id: str, data: ctypes.Structure)`

        Args:
            id (str):   Unique message ID
            callback:   Callback to execute when receiving the message
        """
        pass

    def send_message(self, id: str, data):
        """
        Send an arbitrary block of memory to Unity

        Args:
            id (str):                   Unique message ID
            data (ctypes.Structure):    CTypes structure to copy to Unity

        Returns:
            int: non-zero on failure
        """
        pass

    def update_mesh(self, depsgraph, preserve_all_data_layers: bool = True):
        """
        Attempt to evaluate a mesh from the associated `bpy.types.Object`
        using the provided depsgraph and send to Unity

        Note that if this object's mesh is instanced, only *one* instance will
        execute this update method if it's determined to be non-unique
        (e.g. same modifiers as another instance)
        """
        mesh_uid = self.mesh_uid
        if not mesh_uid:
            return
            # raise AttributeError('SceneObject does not contain a bpy.types.Mesh instance')
            # TODO: Maybe eval_obj failure check instead? Idk.
            # TODO: Warning instead if there's no detectable mesh? There's object types
            # that *wouldn't* have a mesh to update but this'll be called anyway.
            # Maybe a .has_mesh() test first?

        try:
            # We need to do both evaluated_get() and preserve_all_data_layers=True to ensure
            # that - if the mesh is instanced - modifiers are applied to the correct instances.
            eval_obj = self.bpy_obj.evaluated_get(depsgraph)
            mesh = eval_obj.to_mesh(
                preserve_all_data_layers=preserve_all_data_layers,
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

            runtime.lib.CopyMeshDataNative(
                get_string_buffer(mesh_uid),
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

            # TODO: This would also then aggregate custom vertex data streams
            # from ALL plugins into a structure that can get uploaded alongside
            # the rest of the data above.
            # These vertex streams can then be executed from C# per vertex and
            # added as part of the dataset sent to Unity.

            # Release the temporary mesh
            eval_obj.to_mesh_clear()
        except Exception as e:
            error('Could not update mesh', e)


    def update_transform(self):
        """Trigger a sync of this object's transform to Unity.

        This typically happens automatically during depsgraph updates but
        this may also be triggered manually using this method.
        """
        if not self.bpy_obj:
            return

        transform = to_interop_transform(self.bpy_obj)
        runtime.lib.SetObjectTransform(
            get_string_buffer(self.uid),
            transform
        )

    def update_properties(self):
        """Notify Unity that object props may have changed
        """
        if not self.bpy_obj:
            return

        debug('update_properties - name={}'.format(self.name))

        runtime.lib.UpdateObjectProperties(
            get_string_buffer(self.uid),
            int(self.bpy_obj.coherence.display_mode),
            get_string_buffer(self.mesh_uid),
            get_string_buffer(self.mat_uid)
        )

    def remove_on_message(self, callback):
        """Remove a callback previously added with `on_message()`
        """
        pass

    def destroy(self):
        """Destroy this SceneObject

        This will call `on_destroy()` to perform any additional
        cleanup needed after it's been removed.
        """
        self._valid = False
        runtime.instance.invalidated_objects.add(self)
        runtime.lib.RemoveObjectFromScene(get_string_buffer(self.uid))

        self.on_destroy()

class SceneObjectCollection:
    """Collection of SceneObjects with fast lookups by different properties"""

    # Dict<str, SceneObject> where key is a unique object name
    _objects: dict

    # Dict<str, SceneObject> where key is a bpy.types.Object.name
    # and value is the SceneObject referencing the bpy.types.Object.
    _objects_by_bpy_name: dict

    def __init__(self):
        self._objects = {}
        self._objects_by_bpy_name = {}

    def find_by_bpy_name(self, bpy_name: str):
        """

        Args:
            bpy_name (str):

        Returns:
            SceneObject|None
        """
        return self._objects_by_bpy_name.get(bpy_name)

    def append(self, obj):
        """
        Args:
            obj (SceneObject):
        """
        name = obj.name
        if name in self._objects:
            raise Exception('Object named [{}] is already registered'.format(
                name
            ))

        bpy_name = obj.bpy_name
        if bpy_name and bpy_name in self._objects_by_bpy_name:
            raise Exception('Blender object [{}] already has an attached SceneObject'.format(
                bpy_name
            ))

        self._objects[name] = obj
        if bpy_name:
            self._objects_by_bpy_name[bpy_name] = obj

    def remove(self, obj):
        if obj.name in self._objects:
            del self._objects[obj.name]

        bpy_name = obj.bpy_name
        if bpy_name and bpy_name in self._objects_by_bpy_name:
            del self._objects_by_bpy_name[bpy_name]

    def clear(self):
        self._objects = {}
        self._objects_by_bpy_name = {}

    def values(self):
        """
        Returns:
            dict_values[SceneObject]
        """
        return self._objects.values()
