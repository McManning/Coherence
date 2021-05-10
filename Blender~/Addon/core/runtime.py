
import bpy
import numpy as np
from bgl import *
from weakref import WeakValueDictionary

from bpy.app.handlers import (
    depsgraph_update_post,
    load_pre
)

from .utils import (
    log,
    debug,
    warning,
    error,
    get_string_buffer
)

from .scene import SceneObjectCollection

from .interop import *

# Location of the Coherence DLL - relative to addon root
DLL_PATH = 'lib/LibCoherence.dll'

lib = load_library(DLL_PATH)

class Runtime:
    """Runtime bridge between Python and LibCoherence

    This contains the main event handlers, pumping messages to
    and from LibCoherence, plugin management, viewport and
    scene object tracking, etc.
    """
    MAX_TEXTURE_SLOTS = 64
    UNASSIGNED_TEXTURE_SLOT_NAME = '-- Unassigned --'

    #: bool: True if Coherence is currently running
    running: bool = False

    connection_name = None
    blender_version = None

    #: Draw handler for :class:`bpy.types.SpaceImageEditor`
    image_editor_handle = None

    # Numpy array referencing pixel data for the
    # active bpy.types.Image to sync
    image_buffer = None # np.ndarray

    #: WeakValueDictionary[int, :class:`.CoherenceRenderEngine`]: Mapping viewport ID -> Render Engine
    viewports = WeakValueDictionary()

    #: All tracked SceneObject instances synced (or to be synced) with the host
    objects = SceneObjectCollection()

    #: dict[str, Plugin]: Currently registered plugins
    plugins = {}

    #: set(str): :class:`bpy.types.Object` names currently tracked as being in the scene.
    current_names = set()

    #: set(SceneObject): Objects invalidated during the last update
    invalidated_objects = set()

    #: Union[:class:`bpy.types.Despgraph`, None]: Depsgraph we're currently executing within
    current_depsgraph = None

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
        bpy.app.timers.register(self.check_texture_sync)

        # Monitor updates in SpaceImageEditor for texture syncing
        self.image_editor_handle = bpy.types.SpaceImageEditor.draw_handler_add(
            self.on_image_editor_update, (bpy.context,),
            'WINDOW', 'POST_PIXEL'
        )

        # Notify plugins
        for plugin in self.plugins.values():
            plugin.enable()

        # Sync the current scene state
        self.sync_tracked_objects(
            bpy.context.scene,
            bpy.context.evaluated_depsgraph_get()
        )

        # Notify viewports
        self.tag_redraw_viewports()

    def stop(self):
        """Disconnect from the host and cleanup synced objects"""
        if not self.is_running():
            return

        # Safely cleanup if connected
        if self.is_connected():
            self.on_disconnected_from_unity()

        # Disconnect the active connection
        log('DCC teardown')
        lib.Disconnect()

        # Notify plugins
        for plugin in self.plugins.values():
            plugin.disable()

        # Clear local tracking
        self.cleanup()

        # Turning off `running` will also destroy the `on_tick` timer.
        self.running = False

        # Remove Blender event handlers
        if self.on_depsgraph_update in depsgraph_update_post:
            depsgraph_update_post.remove(self.on_depsgraph_update)

        if self.on_load_pre in load_pre:
            load_pre.remove(self.on_load_pre)

        if self.image_editor_handle:
            bpy.types.SpaceImageEditor.draw_handler_remove(self.image_editor_handle, 'WINDOW')
            self.image_editor_handle = None

        # Notify viewports
        self.tag_redraw_viewports()

    def get_texture_slots(self) -> list:
        """Return all sync-able texture slot names exposed by the host

        Returns:
            list[str]
        """
        if not self.is_connected():
            return []

        buffer = (InteropString64 * self.MAX_TEXTURE_SLOTS)()
        size = lib.GetTextureSlots(buffer, len(buffer))

        # Convert byte arrays to a list of strings.
        return [self.UNASSIGNED_TEXTURE_SLOT_NAME] + [buffer[i].buffer.decode('utf-8') for i in range(size)]


    def sync_texture(self, image):
        """Send updated pixel data for a texture to the host

        Args:
            image (bpy.types.Image): The image to sync
        """
        settings = image.coherence
        if settings.error or settings.texture_slot == self.UNASSIGNED_TEXTURE_SLOT_NAME:
            return

        # TODO: Optimize further (e.g. don't allocate
        # the numpy buffer each time, etc etc)
        w, h = image.size

        if self.image_buffer is None:
            self.image_buffer = np.empty(w * h * 4, dtype=np.float32)
        else:
            self.image_buffer.resize(w * h * 4, refcheck=False)

        image.pixels.foreach_get(self.image_buffer)
        pixels_ptr = self.image_buffer.ctypes.data

        lib.UpdateTexturePixels(
            get_string_buffer(settings.texture_slot),
            image.size[0],
            image.size[1],
            pixels_ptr
        )

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

    def register_plugin(self, plugin):
        """Register a third party plugin with the Coherence API.

        This will call the following event chain for the plugin:

        1. :meth:`.Plugin.on_registered()`
        2. :meth:`.Plugin.on_enable()` - if Coherence is currently running
        3. :meth:`.Plugin.on_connected()` - if Coherence is currently connected to Unity

        Args:
            plugin (inherited class of :class:`.Plugin`)
        """
        log('*** REGISTER PLUGIN {}'.format(plugin))

        instance = plugin()
        self.plugins[plugin] = instance
        instance.registered()

        if self.is_running():
            instance.enable()

        if self.is_connected():
            instance.on_connected()

    def unregister_plugin(self, cls):
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
        try:
            instance = self.plugins[cls]

            if self.is_connected():
                instance.on_disconnected()

            if self.is_running():
                instance.disable()

            instance.unregistered()

            del self.plugins[cls]
        except KeyError:
            warning('Plugin {} is not installed'.format(cls))
        except Exception as e:
            error('Exception ignored in Plugin [{}] while unregistering'.format(cls))
            print(e)

    def unregister_all_plugins(self):
        """Remove all third party plugins currently registered.

        This will call the same event chain as :meth:`unregister_plugin()` for each one.
        """
        for plugin in list(self.plugins.values()):
            self.unregister_plugin(plugin)

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
                self.on_disconnected_from_unity()

            #return 0.0001
            #return 0.016 # 60 FPS update rate
            return 0.008 # 120 FPS

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
            self.on_connected_to_unity()

        return 0.05

    def on_message(self, message):
        """Forward inbound messages to plugins

        Args:
            message (InteropMessage)
        """
        if message.invalid:
            return

        plugin_msg = message.as_plugin_message()
        if not plugin_msg:
            return

        try:
            # Determine if it's a message to a SceneObject or Plugin
            if not plugin_msg.target.empty:
                target = self.objects.find(plugin_msg.target.value)
            else:
                target = self.plugins[message.target]

            target._dispatch(plugin_msg)
        except KeyError as e:
            error('Error while routing message to plugin [{}]'.format(message.target, e))

    def check_texture_sync(self) -> float:
        """
        Push image updates to the host if we're actively drawing
        on an image bound to one of the synced texture slots

        Returns:
            float: Milliseconds until the next check
        """
        delay = bpy.context.scene.coherence.texture_slot_update_frequency

        # Don't do anything if we're still not connected
        if bpy.context.mode != 'PAINT_TEXTURE' or not self.is_connected():
            return delay

        image = bpy.context.tool_settings.image_paint.canvas

        # Tool is active but we don't have an image assigned
        if image is None:
            return delay

        self.sync_texture(image)

        return delay

    def on_connected_to_unity(self):
        """Notify plugins that we've connected to an instance of Unity"""
        debug('on_connected_to_unity')
        self.tag_redraw_viewports()

        for plugin in self.plugins.values():
            plugin.on_connected_to_unity()

    def on_connected_to_shared_memory(self):
        debug('on_connected_to_shared_memory')
        # TODO: Needed anymore?

    def on_disconnected_from_unity(self):
        """Notify plugins that we've disconnected from an instance of Unity"""
        debug('on_disconnected_from_unity')
        self.tag_redraw_viewports()

        for plugin in self.plugins.values():
            plugin.on_disconnected_from_unity()

    def tag_redraw_viewports(self):
        """Tag all active RenderEngines for a redraw"""
        for v in self.viewports.items():
            try:
                v[1].on_update()
            except e:
                error(sys.exc_info()[0])

    def sync_tracked_objects(self, scene, depsgraph):
        """Track add/remove of scene objects.

        Objects may be added/removed in case of undo, rename, etc.

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        current = set()

        # TODO: Drill down into cross-scene objects to track.
        # (E.g. something that's referencing a bunch of meshes
        # via a collection in this scene into another scene)

        # Check for added objects
        for bpy_obj in scene.objects:
            current.add(bpy_obj.name)
            if bpy_obj.name not in self.current_names:
                self.on_add_bpy_object(bpy_obj)

        # Check for removed objects
        removed = self.current_names - current
        for name in removed:
            self.on_remove_bpy_object(name)

        # Update current tracked list
        self.current_names = current

    def on_add_bpy_object(self, bpy_obj):
        """Notify plugins that an object has been added to the scene

        Args:
            bpy_obj (bpy.types.Object)
        """
        for plugin in self.plugins.values():
            plugin.on_add_bpy_object(bpy_obj)

    def on_remove_bpy_object(self, name):
        """Destroy an associated SceneObject and notify plugins of removal

        Args:
            name (str): Object name that has been removed.
        """
        obj = self.objects.find_by_bpy_name(name)
        if obj: obj.destroy()

        for plugin in self.plugins.values():
            plugin.on_remove_bpy_object(name)

    def on_image_editor_update(self, context):
        """
        Args:
            context (:mod:`bpy.context`): Image Editor area context
        """
        space = context.space_data

        # Only try to sync updates if we're actively painting
        # on an image. Any other action (masking, viewing) are ignored.
        if space.mode == 'PAINT' and space.image:
            self.sync_texture(space.image)

    def on_load_pre(self, *args, **kwargs):
        """Stop Coherence when our Blender file changes.

        This is to prevent Coherence from entering an invalid state where
        synced objects/viewports no longer exist in the Blender sync.
        """
        self.stop()

    def on_depsgraph_update(self, scene, depsgraph):
        """Sync the bridge with the scene's dependency graph on each update

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        debug('on depsgraph update')

        geometry_updates = {}

        self.current_depsgraph = depsgraph
        self.sync_tracked_objects(scene, depsgraph)

        # Check for updates to objects (geometry changes, transform changes, etc)
        for update in depsgraph.updates:
            if type(update.id) == bpy.types.Material:
                self.on_update_material(update.id)

            elif type(update.id) == bpy.types.Object:
                # If it's a tracked object - update transform/geo/etc where appropriate
                obj = self.objects.find_by_bpy_name(update.id.name)
                if obj:
                    if update.is_updated_transform:
                        obj.update_transform()

                    # Aggregate *unique* meshes that need to be updated this
                    # frame. This de-duplicates any instanced meshes that all
                    # fired the same is_updated_geometry update.
                    if update.is_updated_geometry:
                        debug('Geo update event for obj={} uid={}'.format(obj, obj.mesh_uid))
                        geometry_updates[obj.mesh_uid] = obj

                    # Push any other updates we may be tracking for this object
                    obj.update_properties()

        # Handle all geometry updates
        for uid, obj in geometry_updates.items():
            debug('GEO UPDATE uid={}, obj={}, type={}'.format(uid, obj.name, obj))
            obj.update_mesh(depsgraph)

        # Update all plugins
        for plugin in self.plugins.values():
            plugin.on_depsgraph_update(scene, depsgraph)

        self.current_depsgraph = None

        # Clear everything that may have invalidated this update
        self.remove_all_invalid_objects()

    def add_object(self, obj):
        """Add the given object to the tracked list and lib.

        This does *not* fire any events for the added SceneObject.

        Args:
            obj (SceneObject)
        """
        debug('add_object - name={}'.format(obj.name))
        self.objects.append(obj)

        bpy_obj = obj.bpy_obj
        transform = to_interop_transform(bpy_obj) if bpy_obj else InteropTransform()

        lib.AddObjectToScene(
            get_string_buffer(obj.uid),
            get_string_buffer(obj.kind),
            transform
        )

        # When an object is renamed - it's treated as an add. But the rename
        # doesn't propagate any change events to children, so we need to manually
        # trigger a transform update for everything parented to this object
        # so they can all update their parent name to match.
        if bpy_obj:
            for child in bpy_obj.children:
                child_obj = self.objects.find_by_bpy_name(child.name)
                if child_obj: child_obj.update_transform()

        if not self.current_depsgraph:
            warning('No current_depsgraph during add_object')

        # Send up initial state and geometry
        obj.update_properties()
        obj.update_mesh(
            # TODO: Depsgraph isn't available because this may not get executed
            # within an depsgraph update. So we use the context depsgraph. Is this fine?
            self.current_depsgraph or bpy.context.evaluated_depsgraph_get()
        )

    def remove_all_invalid_objects(self):
        """Remove references to SceneObjects that were previously invalidated"""
        for obj in self.invalidated_objects:
            obj.plugin._objects.remove(obj)
            self.objects.remove(obj)

        self.invalidated_objects.clear()

    def cleanup(self):
        """Clear all object tracking"""
        self.current_names.clear()
        self.objects.clear()
        self.invalidated_objects.clear()
        lib.Clear()

    def on_update_material(self, mat):
        """Trigger a call to :meth:`.SceneObject.update_properties` for all objects using this material

        Args:
            mat (bpy.types.Material)
        """
        debug('on_update_material - name={}'.format(mat.name))

        # Fire off an update for all objects that are using this material
        for bpy_obj in bpy.context.scene.objects:
            if bpy_obj.active_material == mat:
                obj = self.objects.find_by_bpy_name(bpy_obj.name)
                if obj: obj.update_properties()


instance = Runtime()
