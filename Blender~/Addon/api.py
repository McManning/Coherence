from .core import plugin, runtime

class ObjectPlugin(plugin.BaseObjectPlugin):
    pass

class GlobalPlugin(plugin.BaseGlobalPlugin):
    pass

class Component(plugin.BaseComponent):
    pass

def register_plugin(cls):
    """Register a third party plugin with the Coherence API.

    This will call the following event chain for the plugin:

    1. :meth:`.GlobalPlugin.on_registered()`
    2. :meth:`.GlobalPlugin.on_enable()` - if Coherence is currently running
    3. :meth:`.GlobalPlugin.on_connected()` - if Coherence is currently connected to Unity

    Args:
        cls (inherited class of :class:`.GlobalPlugin`)
    """
    runtime.instance.register_plugin(cls)

def unregister_plugin(cls):
    """Unregister a third party plugin from the Coherence API.

    The following event chain is called on the plugin when unregistered:

    1. :meth:`.GlobalPlugin.on_disconnected()` - if Coherence is currently connected to Unity
    2. :meth:`.GlobalPlugin.on_disable()` - if Coherence is currently running.

        This will also execute :meth:`ObjectPlugin.on_destroy()` for all
        objects associated with this plugin.

    3. :meth:`.GlobalPlugin.on_unregistered()`

    Args:
        cls (inherited class of :class:`.GlobalPlugin`)
    """
    runtime.instance.unregister_plugin(cls)

def register_component(cls):
    """Register a third party component with the Coherence API.

    :meth:`.Component.on_registered()` will be executed once successfully registered.

    Args:
        cls (inherited class of :class:`.Component`)
    """
    raise NotImplementedError

def unregister_component(cls):
    """Unregister a third party component from the Coherence API.

    The following event chain is called on the component when unregistered:

    1. :meth:`.Component.on_disable()` - for all instances, if currently enabled
    2. :meth:`.Component.on_destroy()` - for all instances
    3. :meth:`.Component.on_unregistered()`

    Args:
        cls (inherited class of :class:`.Component`)
    """
    raise NotImplementedError

def add_component(obj, cls):
    """Add a component to an existing object

    Args:
        obj (bpy.types.Object):
        cls (inherited class of :class:`.Component`)
    """
    raise NotImplementedError

def destroy_component(obj, cls):
    """Remove a component from an existing object

    The following event chain is called on the component when destroyed:

    1. :meth:`.Component.on_disable()` - if currently enabled
    2. :meth:`.Component.on_destroy()`

    If there is a linked Unity component that
    will also be destroyed through Unity's API.

    Args:
        obj (bpy.types.Object):
        cls (inherited class of :class:`.Component`)
    """
    # Delegates off to .destroy of the component instance for the heavy lifting.
    instance = cls.get_instance(obj)
    instance.destroy()


def is_connected_to_unity() -> bool:
    """

    Returns:
        bool
    """
    return runtime.instance.is_connected()
