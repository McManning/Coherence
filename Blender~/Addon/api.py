from . import core

class SceneObject(core.scene.SceneObject):
    pass

class Plugin(core.plugin.Plugin):
    pass

def register_plugin(plugin):
    """
    Register a third party plugin with the Coherence API.

    This ensures that all objects synced are scoped to the plugin.

    Args:
        plugin_name (class 'Plugin'): Class of a plugin to register
    """
    core.runtime.instance.register_plugin(plugin)

def unregister_plugin(plugin):
    """
    Unregister a third party plugin from the Coherence API.

    This will automatically flush and invalidate all synced custom objects
    associated with the plugin.

    Args:
        plugin (class 'Plugin')
    """
    core.runtime.instance.unregister_plugin(plugin)

def is_connected_to_unity() -> bool:
    """Returns true if Coherence is currently connected to an instance of Unity

    Returns:
        bool
    """
    return core.runtime.instance.is_connected()