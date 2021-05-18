from .core import component, runtime, scene_objects

class Component(component.BaseComponent):
    pass

# Not exposed yet to third parties

# def register_plugin(cls):
#     """Register a third party plugin with the Coherence API.

#     Args:
#         cls (inherited class of :class:`.Plugin`)
#     """
#     runtime.instance.register_plugin(cls)

# def unregister_plugin(cls):
#     """Unregister a third party plugin from the Coherence API.

#     Args:
#         cls (inherited class of :class:`.Plugin`)
#     """
#     runtime.instance.unregister_plugin(cls)

def register_component(component):
    """Register a third party component with the Coherence API.

    :meth:`.Component.on_registered()` will be executed once successfully registered.

    Args:
        component (Type[Component])
    """
    plugin = runtime.instance.get_plugin(scene_objects.SceneObjects)
    plugin.register_component(component)

def unregister_component(component):
    """Unregister a third party component from the Coherence API.

    The following event chain is called on the component when unregistered:

    1. :meth:`.Component.on_disable()` - for all instances, if currently enabled
    2. :meth:`.Component.on_destroy()` - for all instances
    3. :meth:`.Component.on_unregistered()`

    Args:
        component (Type[Component])
    """
    plugin = runtime.instance.get_plugin(scene_objects.SceneObjects)
    if plugin:
        plugin.unregister_component(component)

def add_component(obj, component):
    """Add a component to an existing object

    Args:
        obj (bpy.types.Object)
        component (Type[Component])
    """
    plugin = runtime.instance.get_plugin(scene_objects.SceneObjects)
    plugin.add_component(obj, component)

def destroy_component(obj, component):
    """Remove a component from an existing object

    The following event chain is called on the component when destroyed:

    1. :meth:`.Component.on_disable()` - if currently enabled
    2. :meth:`.Component.on_destroy()`

    If there is a linked Unity component that
    will also be destroyed through Unity's API.

    Args:
        obj (bpy.types.Object)
        component (Type[Component])
    """
    plugin = runtime.instance.get_plugin(scene_objects.SceneObjects)
    plugin.destroy_component(obj, component)

def is_connected_to_unity() -> bool:
    """

    Returns:
        bool
    """
    return runtime.instance.is_connected()
