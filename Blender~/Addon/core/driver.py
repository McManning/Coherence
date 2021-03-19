
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
    get_string_buffer
)

from .interop import *

class BridgeDriver:
    """Respond to scene object changes and handle messaging through the Unity bridge"""

    # Location of the Coherence DLL - relative to addon root
    DLL_PATH = 'lib/LibCoherence.dll';

    METABALLS_OBJECT_NAME = get_string_buffer("__Metaballs")

    MAX_TEXTURE_SLOTS = 64
    UNASSIGNED_TEXTURE_SLOT_NAME = '-- Unassigned --'

    running = False
    lib = None
    connection_name: str = None
    blender_version: str = None
    has_metaballs: bool = False

    image_editor_handle = None # <capsule object RNA_HANDLE>
    image_buffer = None # np.ndarray

    # Mapping between viewport IDs and RenderEngine instances.
    # Weakref is used so that we don't hold onto RenderEngine references
    # since Blender uses __del__ to release them after use
    viewports = WeakValueDictionary()

    # Tracked object names already synced to the DLL
    objects = set()

    def __init__(self):
        path = Path(__file__).parent.parent.joinpath(self.DLL_PATH).absolute()
        log('Loading DLL from {}'.format(path))
        self.lib = cdll.LoadLibrary(str(path))

        # Typehint all the API calls we actually need to typehint
        self.lib.Connect.restype = c_int
        self.lib.Disconnect.restype = c_int
        self.lib.Clear.restype = c_int
        self.lib.SetViewportCamera.argtypes = (c_int, InteropCamera)


        #self.lib.GetTextureSlots.argtypes = (
        #    POINTER(InteropString64),   # Target buffer
        #    c_int                       # size
        #)
        self.lib.GetTextureSlots.restype = c_int

        self.lib.UpdateTexturePixels.argtypes = (
            c_void_p,   # name
            c_int,      # width
            c_int,      # height
            c_void_p    # pixels
        )
        self.lib.UpdateTexturePixels.restype = c_int

        self.lib.CopyMeshDataNative.argtypes = (
            c_void_p,   # name
            c_void_p,   # loops
            c_uint,     # loopSize
            c_void_p,   # loopTris
            c_uint,     # loopTrisSize
            c_void_p,   # verts
            c_uint,     # verticesSize
            c_void_p,   # loopCols
            c_void_p,   # loopUVs
            c_void_p,   # loopUV2s
            c_void_p,   # loopUV3s
            c_void_p,   # loopUV4s
        )
        self.lib.CopyMeshDataNative.restype = c_int

        self.lib.GetRenderTexture.argtypes = (c_uint, )
        self.lib.GetRenderTexture.restype = RenderTextureData

        self.lib.ReleaseRenderTextureLock.argtypes = (c_uint, )
        self.lib.ReleaseRenderTextureLock.restype = c_int

        self.lib.AddObjectToScene.argtypes = (
            c_void_p,           # name
            c_uint,             # SceneObjectType
            InteropTransform,   # transform
        )
        self.lib.AddObjectToScene.restype = c_int

        self.lib.SetObjectTransform.argtypes = (
            c_void_p,           # name
            InteropTransform,   # transform
        )
        self.lib.SetObjectTransform.restype = c_int

        # bpy.types.SpaceView3D.draw_handler_add(post_view_draw, (), 'WINDOW', 'POST_PIXEL')


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

        # Register listeners for Blender events
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

        # Sync the current scene state into the bridge
        self.sync_tracked_objects(
            bpy.context.scene,
            bpy.context.evaluated_depsgraph_get()
        )

        self.tag_redraw_viewports()

    def stop(self):
        """Disconnect from Unity and cleanup synced objects"""
        if not self.is_running():
            return

        log('DCC teardown')
        self.lib.Disconnect()
        self.lib.Clear()

        # Clear local tracking
        self.objects = set()
        self.has_metaballs = False

        # Turning off `running` will also destroy the `on_tick` timer.
        self.running = False

        if self.on_depsgraph_update in depsgraph_update_post:
            depsgraph_update_post.remove(self.on_depsgraph_update)

        if self.on_load_pre in load_pre:
            load_pre.remove(self.on_load_pre)

        if self.image_editor_handle:
            bpy.types.SpaceImageEditor.draw_handler_remove(self.image_editor_handle, 'WINDOW')
            self.image_editor_handle = None

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
        log('***REMOVE VIEWPORT {}'.format(uid))

        del self.viewports[uid]
        self.lib.RemoveViewport(uid)

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
        pass

    def on_connected_to_shared_memory(self):
        debug('on_connected_to_shared_memory')
        pass

    def on_disconnected_from_unity(self):
        debug('on_disconnected_from_unity')
        self.tag_redraw_viewports()
        pass

    def tag_redraw_viewports(self):
        """Tag all active render engines for a redraw"""
        for v in self.viewports.items():
            try:
                v[1].on_update()
            except e:
                error(sys.exc_info()[0])

    def sync_tracked_objects(self, scene, depsgraph):
        """Add/remove objects from the bridge to match the scene.

        Objects may be added/removed in case of undo, rename, etc.

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        current = set() # Set of tracked object names
        found_metaballs = False

        # Check for added objects
        for obj in scene.objects:
            if is_supported_object(obj):
                if obj.name not in self.objects:
                    self.on_add_object(obj, depsgraph)

                current.add(obj.name)
            elif obj.type == 'META':
                found_metaballs = True
                if not self.has_metaballs:
                    self.on_add_metaballs(obj, depsgraph)

        # Check for removed objects
        removed = self.objects - current
        for name in removed:
            self.on_remove_object(name)

        if not found_metaballs and self.has_metaballs:
            self.on_remove_metaballs()

        # Update current tracked list
        self.objects = current

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

        # Only update metaballs as a whole once per tick
        has_metaball_updates = False
        geometry_updates = {}

        self.sync_tracked_objects(scene, depsgraph)

        # Check for updates to objects (geometry changes, transform changes, etc)
        for update in depsgraph.updates:
            if type(update.id) == bpy.types.Material:
                self.on_update_material(update.id)
            elif type(update.id) == bpy.types.Object:
                # Get the real object, not the copy in the update
                obj = bpy.data.objects.get(update.id.name)
                if obj.type == 'META':
                    has_metaball_updates = True
                elif update.id.name in self.objects:
                    # If it's a tracked object - update transform/geo/etc where appropriate
                    if update.is_updated_transform:
                        self.on_update_transform(obj)

                    if update.is_updated_geometry:
                        # Aggregate *unique* meshes that need to be updated this
                        # frame. This de-duplicates any instanced meshes that all
                        # fired the same is_updated_geometry update.
                        geometry_updates[get_mesh_uid(obj)] = obj

                    # Push any other updates we may be tracking for this object
                    self.on_update_properties(obj)

        # Handle all geometry updates
        for uid, obj in geometry_updates.items():
            debug('GEO UPDATE uid={}, obj={}'.format(uid, obj.name))
            self.on_update_geometry(obj, depsgraph)

        # A change to any metaball will trigger a re-evaluation of them all as one object
        if has_metaball_updates:
            self.on_update_metaballs(scene, depsgraph)

    def on_add_object(self, obj, depsgraph):
        """Notify the bridge that the object has been added to the scene

        Args:
            obj (bpy.types.Object):             The object that was added to the scene
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        mat_name = get_material_uid(obj.active_material)

        debug('on_add_object - name={}, mat_name={}'.format(obj.name, mat_name))

        # TODO: Other object types

        parent_name = ''
        if obj.parent_type == 'OBJECT' and obj.parent is not None:
            parent_name = obj.parent.name

        self.lib.AddObjectToScene(
            get_string_buffer(obj.name),
            to_interop_type(obj),
            to_interop_transform(obj)
        )

        # When an object is renamed - it's treated as an add. But the rename
        # doesn't propagate any change events to children, so we need to manually
        # trigger a transform update for everything parented to this object
        # so they can all update their parent name to match.
        for child in obj.children:
            if child.name in self.objects:
                self.on_update_transform(child)

        # Send up initial state and geometry
        self.on_update_properties(obj)
        self.on_update_geometry(obj, depsgraph)

    def on_remove_object(self, name):
        """Notify the bridge that the object has been removed from the scene

        Args:
            name (str): Unique object name shared with the Bridge
        """
        debug('on_remove_object - name={}'.format(name))

        self.lib.RemoveObjectFromScene(
            get_string_buffer(name)
        )

    def on_add_metaballs(self, obj, depsgraph):
        """Update our sync state when metaballs have been first added to the scene.

        We treat all metaballs as a single entity synced with Unity.

        Args:
            obj (bpy.types.Object): The first metaball object in the scene
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        self.has_metaballs = True
        debug('on_add_metaballs')

        mat_name = get_material_uid(obj.active_material)

        transform = to_interop_transform(obj)
        self.lib.AddObjectToScene(
            self.METABALLS_OBJECT_NAME,
            2, # SceneObject.Metaball - TODO: Don't hardcode this
            transform
        )

        # Send an initial set of geometry - already done in update I think?
        # self.on_update_metaballs(obj, depsgraph)

    def on_remove_metaballs(self):
        """Update our sync state when all metaballs have been removed from the scene"""
        self.has_metaballs = False
        debug('on_remove_metaballs')

        self.lib.RemoveObjectFromScene(self.METABALLS_OBJECT_NAME)

    def on_update_transform(self, obj):
        """Notify the bridge that the object has been transformed in the scene.

        Args:
            obj (bpy.types.Object): The object that was updated
        """
        debug('on_update_transform - name={}'.format(obj.name))

        transform = to_interop_transform(obj)
        self.lib.SetObjectTransform(
            get_string_buffer(obj.name),
            transform
        )

    def on_update_properties(self, obj):
        """Notify Unity that object props may have changed

        Args:
            obj (bpy.types.Object): The object that was updated
        """
        mesh_uid = get_mesh_uid(obj)
        mat_uid = get_material_uid(obj.active_material)

        debug('on_update_properties - name={}, mesh={}, mat={}'.format(
            obj.name,
            mesh_uid,
            mat_uid
        ))

        self.lib.UpdateObjectProperties(
            get_string_buffer(obj.name),
            int(obj.coherence.display_mode),
            get_string_buffer(mesh_uid),
            get_string_buffer(mat_uid)
        )

    def on_update_geometry(self, obj, depsgraph):
        """Notify Unity that mesh geometry may have changed

        An object is provided instead of the bpy.types.Mesh
        to evaluate modifiers that need to be applied before
        transferring mesh data to Unity.

        Args:
            obj (bpy.types.Object):             The object that received the update
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        mesh_uid = get_mesh_uid(obj)

        debug('on_update_geometry - name={}, mesh={}'.format(obj.name, mesh_uid))

        # We need to do both evaluated_get() and preserve_all_data_layers=True to ensure
        # that - if the mesh is instanced - modifiers are applied to the correct instances.
        eval_obj = obj.evaluated_get(depsgraph)
        mesh = eval_obj.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)

        # TODO: preserve_all_data_layers is only necessary if instanced and modifier
        # stacks change per instance. Might be cheaper to turn this off if a mesh is used only once.

        # Ensure triangulated faces are available
        mesh.calc_loop_triangles()

        # A single (optional) vertex color layer can be passed through
        cols_ptr = None
        if len(mesh.vertex_colors) > 0 and len(mesh.vertex_colors[0].data) > 0:
            cols_ptr = mesh.vertex_colors[0].data[0].as_pointer()

        # Up to 4 (optional) UV layers can be passed through
        uv_ptr = [None] * 4
        for layer in range(len(mesh.uv_layers)):
            if len(mesh.uv_layers[layer].data) > 0:
                uv_ptr[layer] = mesh.uv_layers[layer].data[0].as_pointer()

        self.lib.CopyMeshDataNative(
            get_string_buffer(mesh_uid),
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.loop_triangles[0].as_pointer(),
            len(mesh.loop_triangles),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices),
            cols_ptr,
            uv_ptr[0],
            uv_ptr[1],
            uv_ptr[2],
            uv_ptr[3]
        )

        eval_obj.to_mesh_clear()

    def on_update_metaballs(self, scene, depsgraph):
        """Rebuild geometry from metaballs in the scene and send to Unity

        Args:
            scene (bpy.types.Scene):
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        # We use the first found in the scene as the root
        obj = None
        for obj in scene.objects:
            if obj.type == 'META':
                break

        debug('on_update_metaballs obj={}'.format(obj))

        # Get the evaluated post-modifiers mesh
        eval_obj = obj.evaluated_get(depsgraph)
        mesh = eval_obj.to_mesh()

        # Ensure triangulated faces are available
        mesh.calc_loop_triangles()

        # TODO: Don't do this repeately. Only if the root changes transform.
        # seems to be lagging out the interop.
        transform = to_interop_transform(obj)
        self.lib.SetObjectTransform(
            self.METABALLS_OBJECT_NAME,
            transform
        )

        self.lib.CopyMeshDataNative(
            self.METABALLS_OBJECT_NAME,
            self.METABALLS_OBJECT_NAME,
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.loop_triangles[0].as_pointer(),
            len(mesh.loop_triangles),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices),
            # Metaballs don't have UV/Vertex Color information,
            # So we skip all that on upload
            None,   # loopCols
            None,   # uv
            None,   # uv2
            None,   # uv3
            None    # uv4
        )

        eval_obj.to_mesh_clear()

    def on_update_material(self, mat):
        """
        Args:
            mat (bpy.types.Material)
        """
        debug('on_update_material - name={}'.format(mat.name))

        # Fire off an update for all objects that are using it
        for obj in bpy.context.scene.objects:
            if obj.active_material == mat and obj.name in self.objects:
                self.on_update_properties(obj)

__singleton = BridgeDriver()

def bridge_driver() -> BridgeDriver:
    """Retrieve the active driver singleton

    Returns:
        BridgeDriver
    """
    return __singleton
