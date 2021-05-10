
import bpy

from . import runtime

from .utils import (
    get_string_buffer,
    debug,
    error,
    PluginMessageHandler,
)

from .interop import (
    to_interop_transform
)

class SceneObject(PluginMessageHandler):
    """
    An object synced through Coherence that contains a transform and optional mesh.
    """
    def __init__(self, name: str, bpy_obj, plugin):
        """
        Warning:
            Do not instantiate directly.
            Instead, call :meth:`.Plugin.instantiate()` from within your plugin.

        Args:
            name (str):                         Unique object name
            bpy_obj (Union[:class:`bpy.types.Object`, None]):    Associated Blender object
            plugin (Plugin):               Instantiating plugin
        """
        # TODO: Hide the above docs from Sphinx somehow
        self._name = name
        self._bpy_name = bpy_obj.name if bpy_obj else None
        self._plugin = plugin
        self._valid = True

    @property
    def kind(self) -> str:
        """str, default ``self.__name__``: The kind of object that is currently being synced"""
        return self.__name__

    @property
    def name(self) -> str:
        """str: Unique name for this object"""
        return self._name

    @property
    def uid(self) -> str:
        """str: Unique identifier"""
        # TODO: Cache and make more unique
        # (multiple objects could have the same name across different plugins)
        return self.name

    @property
    def bpy_name(self) -> str:
        """Union[str, None]: Name of the associated :class:`bpy.types.Object` if one exists"""
        return self._bpy_name

    @property
    def bpy_obj(self):
        """Union[:class:`bpy.types.Object`, None]: Get the Blender object associated with this instance.

        Avoid holding onto a reference to this value long term, as it
        will invalidate out from under you like other StructRNA references.
        """
        if not self._bpy_name:
            return None

        # Fetch a fresh reference to the object
        return bpy.data.objects[self.bpy_name]

    @property
    def mesh_uid(self) -> str:
        """str: Unique identifier for the mesh attached to the object

        If the object has modifiers applied - this will be unique for
        that object. Otherwise - this may be a common mesh name that
        is instanced between multiple objects in the scene.
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
        """Union[str, None]: Retrieve a unique identifier for the object's Material, if applicable"""
        bpy_obj = self.bpy_obj
        if not bpy_obj:
            return None

        if not mat:
            return 'Default'

        return bpy_obj.active_material.name

    @property
    def plugin(self):
        """:class:`.Plugin`: The plugin that instantiated this object"""
        return self._plugin

    @property
    def valid(self) -> bool:
        """Returns true if this object is still valid in the synced scene.

        Objects will be invalidated when :meth:`destroy()` is called on them.
        An object must be recreated through :meth:`.Plugin.instantiate()` once invalidated.

        Returns:
            bool
        """
        return self._valid

    def on_create(self):
        """
        Executes after the object has been created through :meth:`.Plugin.instantiate()`
        and synced to Coherence.
        """
        pass

    def on_destroy(self):
        """
        Executes when this object has been destroyed, either through
        calling :meth:`destroy()`, a desync within Coherence, or
        the associated :attr:`bpy_obj` has been removed from the scene.
        """
        pass

    def add_custom_vertex_data_stream(self, id: str, size: int, callback):
        """
        Add a callback to be executed every time vertex data needs to be synced.

        Note:
            Not yet implemented

        The callback has the following definition::

            def callback(mesh: bpy.types.Mesh) -> Tuple[ctypes.void_p, int]:
                \"""
                Args:
                    mesh (bpy.types.Mesh):      The evaluated mesh instance in the
                                                current Depsgraph.

                Returns:
                    Tuple[ctypes.void_p, int]:  Tuple containing a pointer to the start of the
                                                vertex data array and the number of bytes per
                                                element in that array.
                \"""
                # ... logic here ...

        Data returned by the callback **must be aligned to loops** for the given mesh.
        That is, your element count must equal ``len(mesh.loops)``

        Warning:
            Instancing is disabled for meshes with custom vertex data streams. Each instance
            will be evaluated and sent to Unity as a separate meshes.

        Warning:
            The callback is given a temporary mesh that was created **after** evaluating
            all Blender modifiers through the active Depsgraph. The number of elements
            in your array must match the number of loops after the evaluation.

        Args:
            id (str):
            size (int):             Number of bytes in the data stream per loop index
            callback (callable):    Callable that returns a pointer to the data stream
        """
        # Maybe an optional align to loops vs align to unique vertex index option?
        # I can see use cases for both and it wouldn't be too difficult (if aligned
        # to verts we can totally skip the mapping from loops[i].v step)

        # TODO: Needs to actually return a tuple probably (pointer + size)
        # because I have no idea how big these custom per-vertex data points are.
        raise NotImplementedError

    def remove_custom_vertex_data_stream(self, id: str):
        """Remove a previously registered vertex data stream

        Note:
            Not implemented

        Args:
            id (str):
        """
        raise NotImplementedError

    def update_mesh(self, depsgraph, preserve_all_data_layers: bool = True):
        """
        Attempt to evaluate a mesh from the associated `bpy.types.Object`
        using the provided depsgraph and send to Unity

        Note that if this object's mesh is instanced, only *one* instance will
        execute this update method if it's determined to be non-unique
        (e.g. same modifiers as another instance)

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
            preserve_all_data_layers (bool): Preserve all data layers in the mesh, like UV maps
                                            and vertex groups. By default Blender only computes
                                            the subset of data layers needed for viewport display
                                            and rendering, for better performance.
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

            # TODO: This would also aggregate custom vertex data streams
            # for all SceneObjects that are referencing the same bpy_obj.
            # We'd also need to disable instancing if there's any registered streams
            # since we can't guarantee that data isn't different per instance.
            # Streams need to be converted to some struct containing the stream info
            # (id, size, ptr) and pushed up to C# for diffing and syncing.

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
        """Notify Unity that object properties (transform, display mode, etc) have changed
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

    def destroy(self):
        """Destroy this SceneObject

        This will call :meth:`on_destroy()` to perform any additional
        cleanup needed after it's been removed.
        """
        self._valid = False
        runtime.instance.invalidated_objects.add(self)
        runtime.lib.RemoveObjectFromScene(get_string_buffer(self.uid))

        self.on_destroy()

class SceneObjectCollection:
    """Collection of SceneObjects with fast lookups by different properties"""

    #: dict[str, :class:`.SceneObject`]: Where key is a unique object name
    _objects: dict

    #: dict[str, :class:`.SceneObject`]: Where key is a :attr:`bpy.types.Object.name`
    # and value is the :class:`SceneObject` referencing the `bpy.types.Object`.
    _objects_by_bpy_name: dict

    def __init__(self):
        self._objects = {}
        self._objects_by_bpy_name = {}

    def find_by_bpy_name(self, bpy_name: str):
        """Find an object by the name of the associated :class:`bpy.types.Object`

        Args:
            bpy_name (str):

        Returns:
            Union[:class:`SceneObject`, None]
        """
        return self._objects_by_bpy_name.get(bpy_name)

    def find(self, name: str):
        """Find an object by name

        Args:
            name (str):

        Returns:
            Union[:class:`SceneObject`, None]
        """
        return self._objects.get(name)

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
        """Remove an object from this collection

        Warning:
            This does not execute the object lifecycle method :meth:`.SceneObject.destroy()`

            To safely remove an object as part of a plugin's logic, call destroy on the instance.

        Args:
            obj (SceneObject):
        """
        if obj.name in self._objects:
            del self._objects[obj.name]

        bpy_name = obj.bpy_name
        if bpy_name and bpy_name in self._objects_by_bpy_name:
            del self._objects_by_bpy_name[bpy_name]

    def clear(self):
        """Remove all objects from this collection.

        Warning:
            This does not execute the object lifecycle method :meth:`.SceneObject.destroy()`

            To safely remove all objects as part of a plugin's logic, iterate the collection
            and destroy each object individually.
        """

        self._objects = {}
        self._objects_by_bpy_name = {}

    def values(self):
        """
        Returns:
            dict_values[:class:`SceneObject`]
        """
        return self._objects.values()
