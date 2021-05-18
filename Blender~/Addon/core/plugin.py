
class Plugin:
    """Class that can register with the Coherence runtime and listen to events.

    Plugins are not yet available to third parties (for that, use components)

    Add via :meth:`Runtime.register_plugin` and remove through :meth:`Runtime.unregister_plugin`
    """
    def on_registered(self):
        pass

    def on_unregistered(self):
        pass

    def on_connected(self):
        """Perform any additional work after Coherence establishes a connection"""
        pass

    def on_disconnected(self):
        """Perform any cleanup after Coherence disconnects from the host."""
        pass

    def on_start(self):
        """Called when Coherence is started."""
        pass

    def on_stop(self):
        """Called when Coherence is stopped"""
        pass

    def on_depsgraph_update(self, scene, depsgraph):
        """
        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        pass

    def on_message(self, message):
        """
        Args:
            message (InteropMessage)
        """
        pass