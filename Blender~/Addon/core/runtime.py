
import sys
import bpy
from bgl import *
from weakref import WeakValueDictionary

from bpy.app.handlers import (
    depsgraph_update_post,
    load_pre
)

from .utils import (
    log,
    debug,
    error
)

from .interop import *

class Runtime:
    """Runtime bridge between Python and LibCoherence

    This contains the main event handlers, pumping messages to
    and from LibCoherence, plugin and viewport management, etc
    """
    #: bool: True if Coherence is currently running
    running: bool = False

    connection_name = None
    blender_version = None

    #: WeakValueDictionary[int, :class:`.CoherenceRenderEngine`]: Mapping viewport ID -> Render Engine
    viewports = WeakValueDictionary()

    #: Plugin: Registered Coherence plugins that listen to events
    plugins = set()

    def register_plugin(self, plugin):
        """
        Args:
            plugin (Type[Plugin])
        """
        instance = plugin()
        self.plugins.add(instance)
        instance.on_registered()

    def unregister_plugin(self, plugin):
        """
        Args:
            plugin (Type[Plugin])

        Raises:
            KeyError: If the plugin is not registered
        """
        for instance in self.plugins:
            if instance.__class__ == plugin:
                self.plugins.remove(instance)
                instance.on_unregistered()
                break
        else:
            raise KeyError('Plugin [{}] is not registered'.format(plugin))

    def unregister_all_plugins(self):
        """Unregister all previously registered plugins"""
        for instance in self.plugins:
            instance.on_unregistered()

        self.plugins.clear()

    def get_plugin(self, plugin):
        """
        Args:
            plugin (Type[Plugin])

        Returns:
            Union[Plugin, None]
        """
        return next((x for x in self.plugins if x.__class__ == plugin), None)

    def start(self):
        """Start trying to connect to the host"""
        if self.is_running():
            return

        log('Starting the DCC')

        # TODO: Pull connection name from scene's coherence.connection_name
        self.connection_name = create_string_buffer("Coherence".encode())
        self.blender_version = create_string_buffer(bpy.app.version_string.encode())
        self.running = True

        # Register active viewports
        for render_engine in self.viewports.values():
            self.add_viewport(render_engine)

        # Register handlers for Blender events
        depsgraph_update_post.append(self.on_depsgraph_update)
        load_pre.append(self.on_load_pre)

        # Register timers for frequent updates
        bpy.app.timers.register(self.on_tick)

        # Notify plugins
        for plugin in self.plugins:
            plugin.on_start()

        # Notify viewports
        self.tag_redraw_viewports()

    def stop(self):
        """Disconnect from the host and notify plugins"""
        if not self.is_running():
            return

        # Safely cleanup if connected
        if self.is_connected():
            self.on_disconnected()

        # Close the active connection if there is one
        lib.Disconnect()

        # Notify plugins
        for plugin in self.plugins:
            plugin.on_stop()

        # Turning off `running` will also destroy the `on_tick` timer.
        self.running = False

        # Remove Blender event handlers
        if self.on_depsgraph_update in depsgraph_update_post:
            depsgraph_update_post.remove(self.on_depsgraph_update)

        if self.on_load_pre in load_pre:
            load_pre.remove(self.on_load_pre)

        # Notify viewports
        self.tag_redraw_viewports()

    def add_viewport(self, render_engine):
        """Add a RenderEngine instance as a tracked viewport

        Args:
            render_engine (CoherenceRenderEngine)
        """
        log('Create Viewport {} from {}'.format(
            render_engine.viewport_id,
            id(render_engine)
        ))

        self.viewports[render_engine.viewport_id] = render_engine
        lib.AddViewport(render_engine.viewport_id)

    def remove_viewport(self, uid):
        """Remove a RenderEngine instance as a tracked viewport

        Args:
            uid (int): Unique identifier for the viewport RenderEngine
        """
        log('*** REMOVE VIEWPORT {}'.format(uid))

        del self.viewports[uid]
        lib.RemoveViewport(uid)

    def is_connected(self) -> bool:
        """Is the bridge currently connected to a host

        Returns:
            bool
        """
        return self.running and lib.IsConnectedToUnity()

    def is_running(self) -> bool:
        """Is the driver actively trying to / is connected

        Returns:
            bool
        """
        return self.running

    def on_tick(self):
        """
        Timer registered through bpy.app.timers to handle
        connecting/reconnecting to the host and processing messages

        Returns:
            Union[float, None]: Next time to run the timer or None to destroy it
        """
        if not self.running:
            log('Deactivating on_tick timer')
            return None

        # While actively connected send typical IO,
        # get viewport renders, and run as fast as possible
        if self.is_connected():

            msg = lib.Update()
            self.on_message(msg)

            lib.ConsumeRenderTextures()

            self.tag_redraw_viewports()

            # If we lost connection while polling - flag a disconnect
            if not self.is_connected():
                self.on_disconnected()

            return 0.016 # 60 FPS update rate

            # 120 seems to hang on Windows when minimized. I think it's the processing time of
            # lib.Update() + the tiny delay that prevents Blender from being able to restore?
            # return 0.008 # 120 FPS

        # Attempt to connect to shared memory if not already
        if not lib.IsConnectedToSharedMemory():
            response = lib.Connect(
                self.connection_name,
                self.blender_version
            )

            if response == 1:
                self.on_connected_to_shared_memory()
            elif response == -1:
                print('UNKNOWN ERROR WOO!')
                exit()
            # else the space doesn't exist.

        # Poll for updates until we get one.
        msg = lib.Update()
        self.on_message(msg)

        # TODO: Could replace this with a handshake message from Unity.
        if self.is_connected():
            self.on_connected()

        return 0.05

    def on_message(self, message):
        """Forward inbound messages to plugins

        Args:
            message (InteropMessage)
        """
        if message.invalid:
            return

        for plugin in self.plugins:
            plugin.on_message(message)

    def on_connected(self):
        """Notify plugins and viewports that we've connected"""
        self.tag_redraw_viewports()

        # Notify plugins
        for plugin in self.plugins:
            plugin.on_connected()

    def on_connected_to_shared_memory(self):
        debug('on_connected_to_shared_memory')
        # TODO: Needed anymore?

    def on_disconnected(self):
        """Notify plugins and viewports that we've disconnected"""
        self.tag_redraw_viewports()

        # Notify plugins
        for plugin in self.plugins:
            plugin.on_disconnected()

    def tag_redraw_viewports(self):
        """Tag all active RenderEngines for a redraw"""
        for v in self.viewports.items():
            try:
                v[1].on_update()
            except:
                error(sys.exc_info()[0])

    def on_load_pre(self, *args, **kwargs):
        """Stop Coherence when our Blender file changes.

        This is to prevent Coherence from entering an invalid state where
        synced objects/viewports no longer exist in the Blender sync.
        """
        self.stop()

    def on_depsgraph_update(self, scene, depsgraph):
        """Notify plugins on a depsgraph update

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        for plugin in self.plugins:
            plugin.on_depsgraph_update(scene, depsgraph)

instance = Runtime()
