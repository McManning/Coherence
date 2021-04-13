
from .scene_object import SceneObject
from .utils import (
    SceneObjectCollection
)
from .driver import (
    bridge_driver
)

class Plugin:
    """
    Base class for a third party Coherence plugin.

    Features:
    * Event handlers (connect, disconnect, etc)
    * Custom object management for syncing anything not supported by default
    * Custom vertex data streams for injecting additional data during geometry updates
    """

    _objects: SceneObjectCollection

    def __init__(self):
        self._objects = SceneObjectCollection()

    @property
    def objects(self):
        """All currently valid scene objects created through `instantiate()`

        Returns:
            list of SceneObject
        """
        return self._objects

    def destroy_all_objects(self):
        """Destroy all tracked objects"""
        for obj in self._objects.values():
            obj.destroy()

        self._objects.clear()

    def _destroy_all_silent(self):
        """Destroy all tracked objects without updating Coherence"""
        for obj in self._objects.values():
            obj._destroy_silent()

        self._objects.clear()

    # Plugin gets some basic event callbacks.
    # You can register for more complicated ones.

    def on_registered(self):
        """Perform any setup that needs to be done after loading this plugin"""
        pass

    def on_unregistered(self):
        """Perform any cleanup that needs to be done before unloading this plugin"""
        pass

    def on_connected_to_unity(self):
        """Perform any additional work after Coherence establishes a connection with Unity"""
        pass

    def on_disconnected_from_unity(self):
        """Perform any cleanup after Coherence disconnects from Unity."""
        pass

    def on_enable(self):
        """Called when the Coherence connection has been enabled.

        This will be followed by `on_connected_to_unity()` once a connection can be made.
        """
        pass

    def on_disable(self):
        """Called when the Coherence connection is turned off.

        This will be preceded by a `on_disconnected_from_unity()` if previously connected.

        At this point, any scene objects created by this plugin are invalidated.
        """
        pass

    def on_add_bpy_object(self, bpy_obj):
        """Called when a `bpy.types.Object` is tracked for changes.

        An object that's tracked may exist in the current scene or is
        referenced from another scene. This may also fire when an object
        is renamed - as Blender typically treats renamed objects as new
        objects altogether.

        Args:
            bpy_obj (bpy.types.Object)
        """
        pass

    def on_remove_bpy_object(self, name):
        """Called when a `bpy.types.Object` is removed from the scene.

        This may fire when an object is renamed as Blender treats
        renamed objects as new objects altogether.

        Args:
            name (str): Object name of the now invalid StructRNA
        """
        pass

    def on_depsgraph_update(self, scene, depsgraph):
        """

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        pass

    def add_custom_vertex_data_stream(self, id: str, size: int, callback):
        """
        Add a callback to be executed every time vertex data needs to be synced.
        The callback must return a pointer to the custom vertex data, aligned *to loops*
        for the given mesh.

        `def callback(mesh: bpy.types.Mesh) -> ctypes.void_p`

        Args:
            id (str):
            size (int):             Number of bytes in the data stream per loop index
            callback (callable):    Callable that returns a pointer to the data stream
        """
        # Maybe an optional align to loops vs align to unique vertex index option?
        # I can see use cases for both and it wouldn't be too difficult (if aligned
        # to verts we can totally skip the mapping from loops[i].v step)
        pass

    def remove_custom_vertex_data_stream(self, id: str):
        """Remove a previously registered vertex data stream

        Args:
            id (str):
        """
        pass

    def instantiate(self, obj_type, name: str, bpy_obj = None) -> SceneObject:
        """Add a new object to be synced to Unity.

        If `bpy_obj` is provided then the object will automatically sync mesh
        and transformation data with Unity where possible.

        Otherwise - the object will not have a scene presence and will be treated
        as an arbitrary data stream with Unity (e.g. just for `send_message`
        and `on_message` communication)

        Args:
            obj_type (class):           SceneObject class type to instantiate
            name (str):                 Unique object name in the scene
            bpy_obj (bpy.types.Object): Object instance in the scene to treat as a custom object.
                                        Mutually exclusive with `name`.

        Raises:
            AttributeError: When the type has already been registered with Coherence
                            or when the name already exists in the scene as either
                            a built-in or custom object.

        Returns:
            SceneObject: Instance that has been synced to the scene
        """
        instance = obj_type(name, bpy_obj, self)
        self._objects.append(instance)
        bridge_driver().add_object(instance)
        instance.on_create()
        return instance
