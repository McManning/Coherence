
from .driver import (
    bridge_driver
)

from .utils import  (
    get_mesh_uid,
    get_material_uid,
    get_string_buffer,
    debug
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
    def uid(self):
        """Get a unique identifier for this SceneObject

        Returns:
            PyCArrayType
        """
        # TODO: Cache and make more unique
        # (multiple objects could have the same name across different plugins)
        return get_string_buffer(self.name)

    @property
    def bpy_name(self):
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
    def mesh_uid(self):
        """Retrieve a unique identifier for the object's Mesh, if set

        Returns:
            PyCArrayType|None
        """
        bpy_obj = self.bpy_obj
        if not bpy_obj:
            return None

        # TODO: Cache
        return get_string_buffer(get_mesh_uid(bpy_obj))

    @property
    def mat_uid(self):
        """Retrieve a unique identifier for the object's Material, if set

        Returns:
            PyCArrayType|None
        """
        bpy_obj = self.bpy_obj
        if not bpy_obj:
            return None

        # TODO: Cache
        return get_string_buffer(get_material_uid(bpy_obj.active_material))

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

        Objects may be invalidated when the connection is lost
        or reset with Unity. If the object has an association with a
        `bpy.types.Object` upon creation - the invalidation of the source
        object (i.e. by removal from the scene) will cause this
        scene object to also become invalid.

        An object must be recreated through `Plugin.instantiate()` once invalidated.
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
            raise AttributeError('SceneObject does not contain a bpy.types.Mesh instance')
            # TODO: Maybe eval_obj failure check instead? Idk.

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

        bridge_driver().lib.CopyMeshDataNative(
            mesh_uid,
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

        # Release the temporary mesh
        eval_obj.to_mesh_clear()

    def update_transform(self):
        """Trigger a sync of this object's transform to Unity.

        This typically happens automatically during depsgraph updates but
        this may also be triggered manually using this method.
        """
        if not self.bpy_obj:
            return

        transform = to_interop_transform(self.bpy_obj)
        bridge_driver().lib.SetObjectTransform(self.uid, transform)

    def update_properties(self):
        """Notify Unity that object props may have changed
        """
        if not self.bpy_obj:
            return

        debug('update_properties - name={}'.format(self.name))

        bridge_driver().lib.UpdateObjectProperties(
            self.uid,
            int(self.bpy_obj.coherence.display_mode),
            self.mesh_uid,
            self.mat_uid
        )

    def remove_on_message(self, callback):
        """Remove a callback previously added with `on_message()`
        """
        pass

    def destroy(self):
        """Destroy this SceneObject and remove from Coherence.

        This will call `on_destroy()` to perform any additional
        cleanup needed after it's been removed.
        """
        bridge_driver().remove_object(self)
        self._valid = False
        self.on_destroy()

    def _destroy_silent(self):
        """Destroy this object without removing from Coherence.

        This method will still call `on_destroy()` of the SceneObject
        for any additional cleanup work that's needed.
        """
        self._valid = False
        self.on_destroy()

