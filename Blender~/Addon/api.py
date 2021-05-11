from .core import plugin, runtime

class ObjectPlugin(plugin.BaseObjectPlugin):
    pass

class GlobalPlugin(plugin.BaseGlobalPlugin):
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

def is_connected_to_unity() -> bool:
    """

    Returns:
        bool
    """
    return runtime.instance.is_connected()
