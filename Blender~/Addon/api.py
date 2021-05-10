from . import core

class SceneObject(core.scene.SceneObject):
    pass

class Plugin(core.plugin.Plugin):
    pass

def register_plugin(cls):
    """Register a third party plugin with the Coherence API.

    This will call the following event chain for the plugin:

    1. :meth:`.Plugin.on_registered()`
    2. :meth:`.Plugin.on_enable()` - if Coherence is currently running
    3. :meth:`.Plugin.on_connected()` - if Coherence is currently connected to Unity

    Args:
        cls (inherited class of :class:`.Plugin`)
    """
    core.runtime.instance.register_plugin(cls)

def unregister_plugin(cls):
    """Unregister a third party plugin from the Coherence API.

    The following event chain is called on the plugin when unregistered:

    1. :meth:`.Plugin.on_disconnected()` - if Coherence is currently connected to Unity
    2. :meth:`.Plugin.on_disable()` - if Coherence is currently running.

        This will also execute :meth:`.SceneObject.on_destroy()` for all
        objects associated with this plugin.

    3. :meth:`.Plugin.on_unregistered()`

    Args:
        cls (inherited class of :class:`.Plugin`)
    """
    core.runtime.instance.unregister_plugin(cls)

def is_connected_to_unity() -> bool:
    """

    Returns:
        bool
    """
    return core.runtime.instance.is_connected()
