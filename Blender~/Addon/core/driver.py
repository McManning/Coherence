
import os
from ctypes import *
from ctypes import wintypes
import bpy
import numpy as np
import gpu
from pathlib import Path
from bgl import *
from gpu_extras.batch import batch_for_shader
from mathutils import Vector, Matrix, Quaternion
from math import cos
from copy import copy
from weakref import WeakValueDictionary
import threading

from bpy.props import (
    BoolProperty,
    CollectionProperty,
    EnumProperty,
    FloatProperty,
    FloatVectorProperty,
    IntProperty,
    PointerProperty,
    StringProperty,
)

from bpy.types import (
    PropertyGroup,
    Panel
)

from bpy.app.handlers import (
    depsgraph_update_post,
    load_pre
)

from .utils import (
    log,
    debug,
    warning,
    error,
    is_supported_object,
    get_object_uid,
    get_mesh_uid,
    get_material_uid,
    get_string_buffer,
    SceneObjectCollection
)

from .interop import *

from ..plugins.mesh import (
    MeshPlugin
)

class BridgeDriver:
    """Respond to scene object changes and handle messaging through the Unity bridge"""

    # Location of the Coherence DLL - relative to addon root
    DLL_PATH = 'lib/LibCoherence.dll'

    MAX_TEXTURE_SLOTS = 64
    UNASSIGNED_TEXTURE_SLOT_NAME = '-- Unassigned --'

    running: bool = False
    lib = None # CDLL
    connection_name: str = None
    blender_version: str = None

    # Draw handler for bpy.types.SpaceImageEditor
    image_editor_handle = None # <capsule object RNA_HANDLE>

    # Numpy array referencing pixel data for the
    # active bpy.types.Image to sync to Unity
    image_buffer = None # np.ndarray

    # Dict<int, RenderEngine> where key is a unique viewport ID per RenderEngine instance.
    # Weakref is used so that we don't hold onto RenderEngine references
    # since Blender uses __del__ to release them after use
    viewports = WeakValueDictionary()

    objects = SceneObjectCollection()

    # Dict<str, Plugin> where key is a plugin name
    plugins = {}

    # bpy.types.Object names currently tracked as being in the scene.
    current = set()

    def __init__(self):
        self.lib = load_library(self.DLL_PATH)
        # bpy.types.SpaceView3D.draw_handler_add(post_view_draw, (), 'WINDOW', 'POST_PIXEL')

        # Add default plugins
        self.register_plugin(MeshPlugin)

    def __del__(self):
        debug('__del__ on bridge')
        # bpy.types.SpaceView3D.draw_handler_remove(post_view_draw, 'WINDOW')

    def start(self):
        """Start trying to connect to Unity"""
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
            plugin.on_enable()

        # Sync the current scene state
        self.sync_tracked_objects(
            bpy.context.scene,
            bpy.context.evaluated_depsgraph_get()
        )

        # Notify viewports
        self.tag_redraw_viewports()

    def stop(self):
        """Disconnect from Unity and cleanup synced objects"""
        if not self.is_running():
            return

        # Safely cleanup if connected
        if self.is_connected():
            self.on_disconnected_from_unity()

        # Disconnect the active connection
        log('DCC teardown')
        self.lib.Disconnect()
        self.lib.Clear()

        # Clear local tracking
        self.destroy_all_objects()

        # Notify plugins
        for plugin in self.plugins.values():
            plugin.on_disable()

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

    def free_lib(self): # UNUSED
        pass
        # # Windows-specific handling for freeing the DLL.
        # # See: https://stackoverflow.com/questions/359498/how-can-i-unload-a-dll-using-ctypes-in-python
        # handle = self.lib._handle
        # del self.lib
        # self.lib = None

        # kernel32 = WinDLL('kernel32', use_last_error=True)
        # kernel32.FreeLibrary.argtypes = [wintypes.HMODULE]
        # kernel32.FreeLibrary(handle)

    def get_texture_slots(self) -> list:
        """Return all sync-able texture slot names exposed by Unity

        Returns:
            list[str]
        """
        if not self.is_connected():
            return []

        buffer = (InteropString64 * self.MAX_TEXTURE_SLOTS)()
        size = self.lib.GetTextureSlots(buffer, len(buffer))

        # Convert byte arrays to a list of strings.
        return [self.UNASSIGNED_TEXTURE_SLOT_NAME] + [buffer[i].buffer.decode('utf-8') for i in range(size)]


    def sync_texture(self, image):
        """Send updated pixel data for a texture to Unity

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

        self.lib.UpdateTexturePixels(
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
        self.lib.AddViewport(render_engine.viewport_id)

    def remove_viewport(self, uid):
        """Remove a RenderEngine instance as a tracked viewport

        Args:
            uid (int): Unique identifier for the viewport RenderEngine
        """
        log('*** REMOVE VIEWPORT {}'.format(uid))

        del self.viewports[uid]
        self.lib.RemoveViewport(uid)

    def add_plugin(self, plugin):
        """Add a new plugin to be executed on events

        """
        log('*** ADD PLUGIN {}'.format(plugin))

        instance = plugin()
        self.plugins[plugin.__name__] = instance
        instance.on_registered()

        if self.is_running():
            instance.on_enable()

        if self.is_connected():
            instance.on_connected_to_unity()

    def unregister_plugin(self, plugin):
        try:
            instance = self.plugins[plugin.__name__]

            if self.is_connected():
                instance.on_disconnected_from_unity()

            instance.destroy_all_objects()

            if self.is_running():
                instance.on_disable()

            instance.on_unregistered()

            del self.plugins[plugin.__name__]
        except KeyError:
            warning('Plugin {} is not installed'.format(plugin.__name__))

    def is_connected(self) -> bool:
        """Is the bridge currently connected to an instance of Unity

        Returns:
            bool
        """
        return self.running and self.lib.IsConnectedToUnity()

    def is_running(self) -> bool:
        """Is the driver actively trying to / is connected to Unity

        Returns:
            bool
        """
        return self.running

    def on_tick(self):
        """
            Timer registered through bpy.app.timers to handle
            connecting/reconnecting to Unity and processing messages

        Returns:
            float for next time to run the timer, or None to destroy it
        """
        if not self.running:
            log('Deactivating on_tick timer')
            return None

        # While actively connected to Unity, send typical IO,
        # get viewport renders, and run as fast as possible
        if self.is_connected():
            self.lib.Update()
            self.lib.ConsumeRenderTextures()

            self.tag_redraw_viewports()

            # If we lost connection while polling - flag a disconnect
            if not self.is_connected():
                self.on_disconnected_from_unity()

            #return 0.0001
            #return 0.016 # 60 FPS update rate
            return 0.008 # 120 FPS

        # Attempt to connect to shared memory if not already
        if not self.lib.IsConnectedToSharedMemory():
            response = self.lib.Connect(self.connection_name, self.blender_version)
            if response == 1:
                self.on_connected_to_shared_memory()
            elif response == -1:
                print('UNKNOWN ERROR WOO!')
                exit()
            # else the space doesn't exist.

        # Poll for updates from Unity until we get one.
        self.lib.Update()

        if self.is_connected():
            self.on_connected_to_unity()

        return 0.05

    def check_texture_sync(self) -> float:
        """Push image updates to Unity if we're actively drawing
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
        debug('on_connected_to_unity')
        self.tag_redraw_viewports()

        for plugin in self.plugins.values():
            plugin.on_connected_to_unity()

    def on_connected_to_shared_memory(self):
        debug('on_connected_to_shared_memory')

    def on_disconnected_from_unity(self):
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
            if bpy_obj.name not in self.current:
                self.on_add_bpy_object(obj)

        # Check for removed objects
        removed = self.current - current
        for name in removed:
            self.on_remove_bpy_object(name)

        # Update current tracked list
        self.current = current

    def on_add_bpy_object(self, bpy_obj):
        for plugin in self.plugins.values():
            plugin.on_add_bpy_object(bpy_obj)

    def on_remove_bpy_object(self, name):
        obj = self.objects.find_by_bpy_name(name)
        if obj: obj.destroy()

        for plugin in self.plugins.values():
            plugin.on_remove_bpy_object(name)

    def on_image_editor_update(self, context):
        space = context.space_data

        # Only try to sync updates if we're actively painting
        # on an image. Any other action (masking, viewing) are ignored.
        if space.mode == 'PAINT' and space.image:
            self.sync_texture(space.image)

    def on_load_pre(self, *args, **kwargs):
        """Stop Coherence when our Blender file changes.

        This is to prevent Coherence from entering some invalid state where
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
                        geometry_updates[obj.mesh_uid] = obj

                    # Push any other updates we may be tracking for this object
                    obj.update_properties()

        # Handle all geometry updates
        for uid, obj in geometry_updates.items():
            debug('GEO UPDATE uid={}, obj={}'.format(uid, obj.name))
            obj.update_mesh(depsgraph)

        # Update all plugins
        for plugin in self.plugins.values():
            plugin.on_depsgraph_update(scene, depsgraph)

    def add_object(self, obj):
        """Add the given object to the tracked list and lib.

        This does *not* fire any events for the added SceneObject.

        Args:
            obj (SceneObject):
        """
        debug('add_object - name={}'.format(obj.name))
        self.objects.append(obj)

        bpy_obj = obj.bpy_obj

        parent_name = ''
        if bpy_obj.parent_type == 'OBJECT' and bpy_obj.parent is not None:
            parent_name = bpy_obj.parent.name

        self.lib.AddObjectToScene(
            obj.uid,
            to_interop_type(bpy_obj), # TODO: Use SceneObject.kind instead
            to_interop_transform(bpy_obj)
        )

        # When an object is renamed - it's treated as an add. But the rename
        # doesn't propagate any change events to children, so we need to manually
        # trigger a transform update for everything parented to this object
        # so they can all update their parent name to match.
        for child in bpy_obj.children:
            child_obj = self.objects.find_by_bpy_name(child.name)
            if child_obj: child_obj.update_transform()

        # Send up initial state and geometry
        obj.update_properties()
        obj.update_mesh(depsgraph)

    def remove_object(self, obj):
        """Remove the given object from the tracked list and lib

        This does *not* fire any events for the removed SceneObject.

        Args:
            obj (SceneObject):
        """
        debug('remove_object - name={}'.format(obj.name))

        self.objects.remove(obj)
        self.lib.RemoveObjectFromScene(obj.uid)

    def destroy_all_objects(self):
        """Notify each Plugin to destroy its SceneObjects and clear tracking"""
        for plugin in self.plugins:
            plugin._destroy_all_silent()

        # Clear all tracking
        self.objects.clear()
        self.current = set()

    def on_update_material(self, mat):
        """Trigger a `SceneObject.update_properties()` for all objects using this material

        Args:
            mat (bpy.types.Material)
        """
        debug('on_update_material - name={}'.format(mat.name))

        # Fire off an update for all objects that are using this material
        for bpy_obj in bpy.context.scene.objects:
            if bpy_obj.active_material == mat:
                obj = self.objects.find_by_bpy_name(bpy_obj.name)
                if obj: obj.update_properties()


__singleton = BridgeDriver()

def bridge_driver() -> BridgeDriver:
    """Retrieve the active driver singleton

    Returns:
        BridgeDriver
    """
    return __singleton
