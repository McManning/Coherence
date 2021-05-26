
from os import name
import bpy
from bpy.types import PropertyGroup
from bpy.props import (
    PointerProperty,
    IntProperty,
    FloatProperty,
    BoolProperty,
    StringProperty,
    EnumProperty,
    FloatVectorProperty
)

from . import interop
from . import runtime
from . import scene_objects

# bpy.types.*Property functions that can transfer as Coherence Properties
ALLOWED_PROPERTY_TYPES = [
    IntProperty,
    FloatProperty,
    BoolProperty,
    StringProperty,
    EnumProperty,
    FloatVectorProperty,
]

def on_property_change(name, prev):
    """
    Args:
        name (str): bpy property name
        prev (Union[Callable, None]): Previous
    """
    plugin = runtime.instance.get_plugin(scene_objects.SceneObjects)
    interop_name = interop.to_interop_property_name(name)

    def wrap(self, context):
        instance = plugin.get_component(self.id_data, self.component)
        if not instance:
            raise Exception(
                'Cannot find component instance associated with property group [{}]'.format(self)
            )

        instance.update_property(interop_name, getattr(self, name))

        if prev:
            prev(self, context)

    return wrap

class BaseComponentPropertyGroup(PropertyGroup):
    # TODO: Magic to create this from the base component itself (copy annotations)
    # and register when the base component registers a first instance.

    # Not necessary for this current round of work.

    @classmethod
    def register(cls):
        """Register with Blender as a PropertyGroup on the :class:`bpy.types.Object`"""
        name = cls.component.get_property_group_name()
        setattr(bpy.types.Object, name, PointerProperty(type=cls))

    @classmethod
    def unregister(cls):
        """Unregister from Blender as a PropertyGroup on the :class:`bpy.types.Object`"""
        name = cls.component.get_property_group_name()
        delattr(bpy.types.Object, name)


class BaseComponent:
    """Base class for a Coherence Component.

    Warning:
        Third party components should extend off of ``Coherence.api.Component``
    """

    #: dict[str, set]: Message ID mapped to a set of callback methods
    _handlers: dict

    def __init__(self, obj_name):
        """
        Args:
            obj_name (str)
        """
        self._name = obj_name
        self._has_mesh = False
        self._enabled = False
        self._handlers = dict()
        self._properties = dict()

    # No longer declared - we now use hasattr to determine if the component is autobind.
    # @classmethod
    # def poll(cls, bpy_obj):
    #    """Return true if this component should auto-mount to the object when added to the scene
    #
    #    Args:
    #        bpy_obj (bpy.types.Object): New object to test for support
    #    """
    #    return False

    @classmethod
    def name(cls) -> str:
        """str: Get common component name"""
        return cls.__name__

    @property
    def property_names(self) -> list:
        """list[str]: PropertyGroup property names that can be synced"""
        if not hasattr(self, '__annotations__'):
            return []

        return self.__annotations__.keys()

    @classmethod
    def get_property_group_name(cls) -> str:
        """:str: Name of the PropertyGroup registered to this component type"""
        return 'coherence_' + cls.__name__.lower()

    @property
    def property_group(self):
        """
        Returns:
            PropertyGroup|None
        """
        name = self.get_property_group_name()
        if not hasattr(self.bpy_obj, name):
            return None

        return getattr(self.bpy_obj, name)

    @property
    def interop(self):
        mesh_uid = self.mesh_uid
        material_id = self.material_id

        component = interop.InteropComponent()
        component.name = interop.InteropString64(self.name().encode())
        component.target = interop.InteropString64(self._name.encode())
        component.enabled = self.enabled

        if mesh_uid:
            component.mesh = interop.InteropString64(mesh_uid.encode())

        if material_id:
            component.material = interop.InteropString64(material_id.encode())

        return component

    @classmethod
    def is_autobind(cls) -> bool:
        """:bool: true if a ``poll`` method is defined on this component.

        Autobind components cannot be added and removed via the Blender UI
        """
        return hasattr(cls, 'poll')

    @property
    def object_name(self) -> str:
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
            message (:class:`.InteropComponentMessage`)

        Raises:
            KeyError: If no handler is registered for inbound message ID
        """
        self._handlers[message.id].dispatch(
            message.id,
            message.size,
            message.data
        )

    def update_property(self, name: str, value):
        """Update a property shared between synced components.

        This is typically called as an event handler on Blender prop changes but can be
        called directly with the caveat that the value may be overridden by the Blender UI.

        Args:
            name (str): Unique property name
            value (mixed): Native property value (bool, int, vec3, etc)

        Raises:
            TypeError: If the value cannot be converted to an interop property type
        """
        print('Update property {} to value {}'.format(name, value))

        prop = self._properties.get(name)
        if not prop:
            prop = interop.InteropProperty(
                name=interop.InteropString64(name.encode())
            )

        prop.set_value_from_bpy_property(value)

        self._properties[name] = prop
        print('CALLING UPDATE PROPERTY FOR {} -> {}'.format(name, prop))
        interop.lib.UpdateComponentProperty(self.interop, prop)

    def update_all_properties(self):
        """Push all associated PropertyGroup properties to Coherence"""
        props = self.property_group
        if not props:
            return

        # Annotations of the generated PropertyGroup should already be
        # filtered down to supported properties. So it's safe to iterate here.
        for name in props.__annotations__:
            self.update_property(
                interop.to_interop_property_name(name),
                getattr(props, name)
            )

    def destroy(self):
        """Remove this component from the :attr:`bpy_obj`."""
        raise NotImplementedError

    def draw(self, layout):
        """Draw the component panel in Blender's UI

        This can be overridden per-component to create a custom panel

        Args:
            layout (bpy.types.UILayout): Parent layout to draw into
        """
        props = self.property_group
        if not props or len(props.__annotations__) < 1:
            layout.active = False
            layout.label(text='No properties available')
            return

        layout.use_property_split = True
        for name in props.__annotations__:
            (func, args) = props.__annotations__[name]

            # Skip hidden props
            if 'options' in args and 'HIDDEN' in args['options']: continue

            layout.prop(props, name)


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

    def on_coherence_start(self):
        """Called when Coherence has been started

        This will be followed by :meth:`on_coherence_connected()` once a connection can be made.
        """
        pass

    def on_coherence_stop(self):
        """Called when Coherence is stopped or the plugin is unregistered.

        This will be preceded by an :meth:`on_coherence_disconnected()` if previously connected.
        """
        pass

    def on_update(self, depsgraph, update):
        """Handle a depsgraph update for the linked `bpy.types.Object`

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
            update (bpy.types.DepsgraphUpdate): Update for the linked object
        """
        pass

    def on_after_depsgraph_updates(self, depsgraph):
        """Executed after all depsgraph updates have been processed

        Args:
            depsgraph (bpy.types.Depsgraph): Evaluated dependency graph
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

    @classmethod
    def register_property_group(cls):
        if not hasattr(cls, '__annotations__'):
            return

        # Filter only the allowed property types that can transfer
        annotations = dict()
        for (key, annotation) in cls.__annotations__.items():
            if type(annotation) is tuple and annotation[0] in ALLOWED_PROPERTY_TYPES:
                props = annotation[1].copy()

                # Create (or wrap) the update function with our own handler
                props['update'] = on_property_change(key, props.get('update'))

                annotations[key] = (annotation[0], props)
            else:
                print('Skipping unsupported property [{}] - {}'.format(key, annotation))

        # Create a dynamic PropertyGroup associated with this component
        cls.property_group_class = type(
            cls.__name__ + 'PropertyGroup',
            (BaseComponentPropertyGroup,),
            { '__annotations__': annotations }
        )

        cls.property_group_class.component = cls

        bpy.utils.register_class(cls.property_group_class)

    @classmethod
    def unregister_property_group(cls):
        bpy.utils.unregister_class(cls.property_group_class)
