
import bpy
from bpy.props import PointerProperty
from bpy.types import PropertyGroup

from . import runtime
from . import scene

from .utils import (
    PluginMessageHandler
)

class ComponentMap:
    #: dict[str, dict[str, :class:`.Component`]]: Where key is a
    # :attr:`bpy.types.Object.name` and the value dictionary is
    # keyed by the component name
    _objects: dict

    def __init__(self):
        self._objects = dict()

    def add_component(self):
        pass

    def objects(self):
        """Generator to return objects with components

        Returns:
            :class:`bpy.types.Object` generator
        """
        for name in self._objects.keys():
            yield bpy.data.objects[name]

    def get_component(self, obj, name):
        """
        Args:
            obj (:class:`bpy.types.Object`):
            name (str):
        """
        pass

class BaseComponent(PropertyGroup):
    """Base class for a third party component"""

    #: dict[str, set]: Message ID mapped to a set of callback methods
    _handlers: dict

    def __init__(self, bpy_obj):
        self._name = bpy_obj.name
        self._has_mesh = False
        self._enabled = False
        self._handlers = dict()

    # No longer declared - we now use hasattr to determine if the component is autobind.
    # @classmethod
    # def poll(cls, bpy_obj):
    #    """Return true if this component should auto-mount to the object when added to the scene
    #
    #    Args:
    #        bpy_obj (bpy.types.Object): New object to test for support
    #    """
    #    return False

    def get_supported_properties(self):
        """PropertyGroup property names can be synced with Coherence

        Returns:
            list[str]
        """

        if not hasattr(self, '__annotations__'):
            return []

        # TODO: Filter out to only bool, int, string, etc.
        # OR (since this might be faster) just scan the component
        # while registering and throw an error if any are defined
        # that aren't supported.
        return self.__annotations__.keys()

    @classmethod
    def get_property_group_name(cls):
        return 'coherence_' + cls.__name__.lower()

    @classmethod
    def get_instance(cls, object):
        """
        Returns:
            Component|None
        """
        name = cls.get_property_group_name()
        if not hasattr(object, name):
            return None

        return getattr(object, name)

    @classmethod
    def register(cls):
        """Register with Blender as a PropertyGroup on the :class:`bpy.types.Object`"""
        name = cls.get_property_group_name()
        setattr(bpy.types.Object, name, PointerProperty(type=cls))

    @classmethod
    def unregister(cls):
        """Unregister from Blender as a PropertyGroup on the :class:`bpy.types.Object`"""
        name = cls.get_property_group_name()
        delattr(bpy.types.Object, name)

    @property
    def is_autobind(self) -> bool:
        """bool: Returns true if a ``poll`` method is defined on this component.

        Autobind components cannot be added and removed via the Blender UI
        """
        return hasattr(self, 'poll')

    @property
    def name(self) -> str:
        """str: Name of the associated :class:`bpy.types.Object`"""
        # Replaces .bpy_name and .uid
        return self._name

    @property
    def bpy_obj(self):
        """:class:`bpy.types.Object`: Get the Blender object this component is attached onto

        Avoid holding onto a reference to this value long term, as it
        will invalidate out from under you like other StructRNA references.
        """
        return bpy.data.objects[self._name]

    @property
    def scene_obj(self):
        """:class:`.SceneObject`: Get the SceneObject associated with this instance.
        """
        raise NotImplementedError

    @property
    def mesh_id(self) -> str:
        """Union[str, None]: Unique identifier for the mesh attached to the object

        If the object has modifiers applied - this will be unique for
        that object. Otherwise - this may be a common mesh name that
        is instanced between multiple objects in the scene.
        """
        return None

    @property
    def material_id(self) -> str:
        """Union[str, None]: Unique identifier for the material attached to the object

        If there is no bpy_obj or no active material, this returns None.
        """
        obj = self.bpy_obj
        if not obj or not obj.active_material:
            return None

        return obj.active_material.name

    @property
    def enabled(self):
        return self._enabled

    @enabled.setter
    def enabled(self, val):
        if not self._enabled and val:
            self._enabled = True
            self.on_enable()
        elif self._enabled and not val:
            self._enabled = False
            self.on_disable()

    @property
    def mesh_uid(self):
        """Unique identifier for the mesh associated with this object.

        If a mesh is instanced between different objects and should only be
        evaluated once, then return the same UID between objects.

        If this returns a non-None value, then :meth:`on_update_mesh` must be
        implemented to handle the request for handling mesh data updates
        when the depsgraph is modified.

        Returns:
            Union[str, None]
        """
        return None

    @property
    def has_mesh(self):
        return self._has_mesh

    @has_mesh.setter
    def has_mesh(self, val):
        self._has_mesh = val
        # TODO: Notify runtime that this is provides mesh changes

    # TODO: Replacement (I think just global methods)

    # @classmethod
    # def register(self):
    #     self.on_registered()

    # @classmethod
    # def unregister(self):
    #     self.on_unregistered()

    def add_handler(self, id: str, callback):
        """
        Add an event handler for when Unity sends a custom message
        for this object (e.g. through a associated Unity plugin)

        Args:
            id (str):   Unique message ID
            callback:   Callback to execute when receiving the message
        """
        if not self._handlers:
            self._handlers = {}

        handlers = self._handlers.get(id, set())
        self._handlers.add(callback)
        self._handlers[id] = handlers

    def remove_handler(self, id: str, callback):
        """Remove a callback previously added with :meth:`add_handler()`

        Args:
            id (str):   Unique message ID
            callback:   Callback to execute when receiving the message

        Raises:
            KeyError:   If the handler was not registered
        """
        self._handlers[id].remove(callback)

    def remove_all_handlers(self):
        """Remove all callbacks for inbound messages"""
        self._handlers = {}

    def send_event(self, id: str, size: int, data):
        """Send an arbitrary block of data to Unity.

        Data sent will be associated with this object on the Unity side.

        Args:
            id (str):           Unique message ID
            size (int):         Size of the payload to send
            data (c_void_p):    Payload to send

        Returns:
            int: non-zero on failure
        """
        raise NotImplementedError

    def _dispatch(self, message):
        """Dispatch a message to all listeners

        Args:
            message (:class:`.InteropPluginMessage`)

        Raises:
            KeyError: If no handler is registered for inbound message ID
        """
        self._handlers[message.id].dispatch(
            message.id,
            message.size,
            message.data
        )

    # TODO: Document global method for destroying components

    def destroy(self):
        """Remove this component from the :attr:`bpy_obj`."""
        raise NotImplementedError

    @classmethod
    def on_registered(cls):
        """Perform any setup that needs to be done after loading this plugin"""
        pass

    @classmethod
    def on_unregistered(cls):
        """Perform any cleanup that needs to be done before unloading this plugin"""
        pass

    def on_create(self):
        """
        Executes after the component has been created and synced with Coherence.
        """
        pass

    def on_destroy(self):
        """
        Executes when the :class:`bpy.types.Object` has been removed from the scene
        or this component has been removed from the object.
        """
        pass

    def on_disable(self):
        """Perform any cleanup that needs to be done when disabling this instance"""
        pass

    def on_enable(self):
        """Perform any cleanup that needs to be done when enabling this instance"""
        pass

    def on_coherence_connected(self):
        """Perform any additional work after Coherence establishes a connection"""
        pass

    def on_coherence_disconnected(self):
        """Perform any cleanup after Coherence disconnects from the host."""
        pass

    def on_coherence_enabled(self):
        """Called when the Coherence connection has been enabled.

        This will be followed by :meth:`on_connected()` once a connection can be made.
        """
        pass

    def on_coherence_disabled(self):
        """Called when the Coherence connection is disabled or the plugin is unregistered.

        This will be preceded by an :meth:`on_disconnected()` if previously connected.

        Before this method is called all objects associated with this plugin will
        have been destroyed through :meth:`destroy_all_objects()`.
        """
        pass

    def on_update_mesh(self, depsgraph):
        """Handle mesh update events from the underlying :class:`bpy.types.Object`

        If the mesh is instanced across multiple objects, only one one of
        objects will receive this event.

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
        """
        raise NotImplementedError('This must be implemented if there is a mesh_uid')

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

