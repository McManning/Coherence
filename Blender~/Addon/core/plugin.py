
from . import runtime
from . import scene

from .utils import (
    error,
    PluginMessageHandler
)

class BaseObjectPlugin(PluginMessageHandler):
    """Base class for a third party Object Plugin"""

    # TODO: Add from SceneObject: name, kind (?), uid, bpy_name
    # Since we're splitting these out, it's many-to-one ObjectPlugin -> SceneObject

    @property
    def name(self) -> str:
        """str: Name of the associated :class:`bpy.types.Object`"""
        raise NotImplementedError

    @property
    def scene_obj(self):
        """:class:`.SceneObject`: Get the SceneObject associated with this instance.
        """
        raise NotImplementedError

    @property
    def bpy_obj(self):
        """:class:`bpy.types.Object`: Get the Blender object associated with this instance.

        Avoid holding onto a reference to this value long term, as it
        will invalidate out from under you like other StructRNA references.
        """
        raise NotImplementedError
        # TODO: Proxy to the SceneObject

    @property
    def plugin(self):
        """:class:`.GlobalPlugin`: The plugin that instantiated this object"""
        return self._plugin

    def on_create(self):
        """
        Executes after the object has been created through :meth:`.GlobalPlugin.instantiate()`
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

    def add_vertex_data_stream(self, id: str, size: int, callback):
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

    def remove_vertex_data_stream(self, id: str):
        """Remove a previously registered vertex data stream

        Note:
            Not implemented

        Args:
            id (str):
        """
        raise NotImplementedError


class BaseGlobalPlugin(PluginMessageHandler):
    """ Base class for a third party Global Plugin"""
    _objects: scene.SceneObjectCollection

    def __init__(self):
        self._objects = scene.SceneObjectCollection()

    @property
    def objects(self):
        """All currently valid scene objects created through :meth:`instantiate()`

        Returns:
            dict_values[:class:`SceneObject`]
        """
        return self._objects.values()

    def enable(self):
        self.enabled = True
        self.on_enable()

    def disable(self):
        self.enabled = False
        self.destroy_all_objects()
        self.on_disable()

    def registered(self):
        self.on_registered()

    def unregistered(self):
        self.on_unregistered()

    def destroy_all_objects(self):
        """Destroy all :class:`.ObjectPlugin` instantiated by this plugin.

        Access through :attr:`objects` will no longer be possible and any
        other references will point to an invalidated object.
        """
        for obj in self.objects:
            obj.destroy()

        self._objects.clear()

    def instantiate(self, obj_type, name: str, bpy_obj = None):
        """Add a new scene object to be synced.

        If `bpy_obj` is provided then the object will automatically sync mesh
        and transformation data where possible.

        Otherwise - the object will not have a scene presence and will be treated
        as an arbitrary data stream (e.g. just for `send_message`
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
        if not self.enabled:
            raise Exception('Cannot instantiate objects while a plugin is disabled')

        instance = obj_type(name, bpy_obj, self)
        self._objects.append(instance)
        runtime.instance.add_object(instance)
        instance.on_create()
        return instance

    def on_registered(self):
        """Perform any setup that needs to be done after loading this plugin"""
        pass

    def on_unregistered(self):
        """Perform any cleanup that needs to be done before unloading this plugin"""
        pass

    def on_connected(self):
        """Perform any additional work after Coherence establishes a connection"""
        pass

    def on_disconnected(self):
        """Perform any cleanup after Coherence disconnects from the host."""
        pass

    def on_enable(self):
        """Called when the Coherence connection has been enabled.

        This will be followed by :meth:`on_connected()` once a connection can be made.
        """
        pass

    def on_disable(self):
        """Called when the Coherence connection is disabled or the plugin is unregistered.

        This will be preceded by an :meth:`on_disconnected()` if previously connected.

        Before this method is called all objects associated with this plugin will
        have been destroyed through :meth:`destroy_all_objects()`.
        """
        pass

    def on_add_bpy_object(self, bpy_obj):
        """Called when a new :class:`bpy.types.Object` is tracked for changes.

        An object that's tracked may exist in the current scene or is
        referenced from another scene. This may also fire when an object
        is renamed - as Blender typically treats renamed objects as new
        objects altogether.

        Args:
            bpy_obj (bpy.types.Object)
        """
        pass

    def on_remove_bpy_object(self, name):
        """Called when a :class:`bpy.types.Object` is removed from the scene.

        This may fire when an object is renamed as Blender treats
        renamed objects as new objects altogether.

        Args:
            name (str): Object name that has been removed.
        """
        pass

    def on_depsgraph_update(self, scene, depsgraph):
        """

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        pass

