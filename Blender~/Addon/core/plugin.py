
from . import runtime
from . import scene

from .utils import (
    error,
    PluginMessageHandler
)

class Plugin(PluginMessageHandler):
    """
    Base class for a third party Coherence plugin.

    Features:
    * Event handlers (connect, disconnect, etc)
    * Custom object management for syncing custom scene objects
    * Custom vertex data streams for injecting additional data during geometry updates
    """

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
        """Destroy all :class:`.SceneObject` instantiated by this plugin.

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
