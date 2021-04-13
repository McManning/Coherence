
from .driver import bridge_driver

class API:
    """
    Public API exposed by Coherence for third party addons
    """

    def register(self, plugin):
        """
        Register a third party plugin with the Coherence API.

        This ensures that all objects synced are scoped to the plugin.

        Args:
            plugin_name (class 'Plugin'): Class of a plugin to register
        """
        bridge_driver().register_plugin(plugin)

    def unregister(self, plugin):
        """
        Unregister a third party plugin from the Coherence API.

        This will automatically flush and invalidate all synced custom objects
        associated with the plugin.

        Args:
            plugin (class 'Plugin')
        """
        bridge_driver().unregister_plugin(plugin)

