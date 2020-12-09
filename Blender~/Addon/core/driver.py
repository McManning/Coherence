
import os
from ctypes import *
from ctypes import wintypes
import bpy
import numpy as np
import gpu
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
    depsgraph_update_post
)

from .utils import (
    log,
    debug,
    warning,
    error,
    is_supported_object,
    get_object_uid,
    get_material_unity_name,
    get_string_buffer
)

from .interop import *

class BridgeDriver:
    """Respond to scene object changes and handle messaging through the Unity bridge"""

    running = False
    lib = None
    connection_name: str = None
    blender_version: str = None

    # Mapping between viewport IDs and RenderEngine instances.
    # Weakref is used so that we don't hold onto RenderEngine references
    # since Blender uses __del__ to release them after use
    viewports = WeakValueDictionary()

    # Tracked object names already synced to the DLL
    objects = set()

    def __init__(self):
        # TODO: Better path lookup. I'm using a junction point in Blender
        # to reference the DLL during development - so this needs to be hardcoded
        # since __file__ shows the virtual path

        # path = os.path.dirname(os.path.dirname(os.path.abspath(__file__))) + os.path.sep + 'Bridge.dll'
        path = 'D:\\Unity Projects\\Coherence\\Blender~\\LibCoherence\\bin\\Debug\\LibCoherence.dll'

        log('Loading DLL from {}'.format(path))

        self.lib = cdll.LoadLibrary(path)

        # Typehint all the API calls we actually need to typehint
        self.lib.Connect.restype = c_int
        self.lib.Disconnect.restype = c_int
        self.lib.Clear.restype = c_int
        self.lib.SetViewportCamera.argtypes = (c_int, InteropCamera)
        self.lib.CopyVertices.argtypes = (c_void_p, c_void_p, c_uint, c_void_p, c_uint)
        self.lib.CopyLoopTriangles.argtypes = (c_void_p, c_void_p, c_uint)
        self.lib.GetRenderTexture.argtypes = (c_uint, )
        self.lib.GetRenderTexture.restype = RenderTextureData

        self.lib.ReleaseRenderTextureLock.argtypes = (c_uint, )
        self.lib.ReleaseRenderTextureLock.restype = c_int

        # bpy.types.SpaceView3D.draw_handler_add(post_view_draw, (), 'WINDOW', 'POST_PIXEL')


    def __del__(self):
        debug('__del__ on bridge')

        # bpy.types.SpaceView3D.draw_handler_remove(post_view_draw, 'WINDOW')

    def start(self):
        """Start trying to connect to Unity

        Parameters:
            scene (bpy.types.Scene)
        """
        log('Starting the DCC')
        self.object_ids = set()

        self.connection_name = create_string_buffer("Coherence".encode())
        self.blender_version = create_string_buffer(bpy.app.version_string.encode())
        self.running = True

        bpy.app.handlers.depsgraph_update_post.append(self.on_depsgraph_update)
        bpy.app.timers.register(self.on_tick)

        # Sync the current scene state into the bridge
        self.sync_tracked_objects(
            bpy.context.scene,
            bpy.context.evaluated_depsgraph_get()
        )

    # def add_all_from_scene(self, scene):
    #     """Add all objects in the scene to the bridge

    #     Parameters:
    #         scene (bpy.types.Scene)
    #     """

    #     # Send add events for all objects currently in the scene
    #     for obj in scene.objects:
    #         if is_supported_object(obj):
    #             self.on_add_object(obj)
    #             self.object_ids.add(get_object_uid(obj))

    def stop(self):
        """Disconnect from Unity and cleanup sycned objects"""
        log('DCC teardown')
        self.lib.Disconnect()
        self.lib.Clear()

        # Turning off `running` will also destroy the `on_tick` timer.
        self.running = False

        if self.on_depsgraph_update in depsgraph_update_post:
            depsgraph_update_post.remove(self.on_depsgraph_update)

        # Untrack everything in the scene - we'll have to re-track it all on start() again.
        self.object_ids = set()

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

    def add_viewport(self, render_engine):
        """Add a RenderEngine instance as a tracked viewport

        Parameters:
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

        Parameters:
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
        return self.lib.IsConnectedToUnity()

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
        if self.lib.IsConnectedToUnity():
            self.lib.Update()
            self.lib.ConsumeRenderTextures()

            self.tag_redraw_viewports()

            # During an update we lost connection.
            if not self.lib.IsConnectedToUnity():
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

        if self.lib.IsConnectedToUnity():
            self.on_connected_to_unity()

        return 0.05

    def on_connected_to_unity(self):
        debug('on_connected_to_unity')
        pass

    def on_connected_to_shared_memory(self):
        debug('on_connected_to_shared_memory')
        pass

    def on_disconnected_from_unity(self):
        debug('on_disconnected_from_unity')
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

        Parameters:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        current = set() # Set of tracked object names

        # Check for added objects
        for obj in scene.objects:
            if is_supported_object(obj):
                if obj.name not in self.objects:
                    self.on_add_object(obj, depsgraph)

                current.add(obj.name)

        # Check for removed objects
        removed = self.objects - current
        for name in removed:
            self.on_remove_object(name)

        # Update current tracked list
        self.objects = current

    def on_depsgraph_update(self, scene, depsgraph):
        """Sync the bridge with the scene's dependency graph on each update

        Parameters:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        debug('on depsgraph update')

        self.sync_tracked_objects(scene, depsgraph)

        # Check for updates to objects (geometry changes, transform changes, etc)
        for update in depsgraph.updates:
            if type(update.id) == bpy.types.Material:
                self.on_update_material(update.id)
            elif type(update.id) == bpy.types.Object:
                # Get the real object, not the copy in the update
                obj = bpy.data.objects.get(update.id.name)

                if update.is_updated_transform:
                    self.on_update_transform(obj)
                elif update.is_updated_geometry:
                    self.on_update_geometry(obj, depsgraph)

                # A material association can change - which doesn't
                # trigger a material update but does trigger a
                # depsgraph update on the parent object.
                self.on_update_object_material(obj)

    def on_add_object(self, obj, depsgraph):
        """Notify the bridge that the object has been added to the scene

        Parameters:
            obj (bpy.types.Object):             The object that was added to the scene
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        mat_name = get_material_unity_name(obj.active_material)

        debug('on_add_object - name={}, mat_name={}'.format(obj.name, mat_name))

        # TODO: Other object types

        self.lib.AddMeshObjectToScene(
            get_string_buffer(obj.name),
            to_interop_matrix4x4(obj.matrix_world),
            get_string_buffer(mat_name)
        )

        # Send up initial geometry as well
        self.on_update_geometry(obj, depsgraph)

    def on_remove_object(self, name):
        """Notify the bridge that the object has been removed from the scene

        Parameters:
            name (str): Unique object name shared with the Bridge
        """
        debug('on_remove_object - name={}'.format(name))

        self.lib.RemoveObjectFromScene(
            get_string_buffer(name)
        )

    def on_update_transform(self, obj):
        """Notify the bridge that the object has been transformed in the scene

        Parameters:
            obj (bpy.types.Object): The object that was updated
        """
        debug('on_update_transform - name={}'.format(obj.name))

        self.lib.SetObjectTransform(
            get_string_buffer(obj.name),
            to_interop_matrix4x4(obj.matrix_world)
        )

    def on_update_geometry(self, obj, depsgraph):
        """Notify the bridge that object geometry has changed

        Parameters:
            obj (bpy.types.Object):             The object that was updated
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        debug('on_update_geometry - name={}'.format(obj.name))

        name_buf = get_string_buffer(obj.name)
        eval_obj = obj.evaluated_get(depsgraph)
        mesh = eval_obj.to_mesh()

        # Ensure triangulated faces are available
        mesh.calc_loop_triangles()

        # Need both vertices and loops. Vertices store co/no information per-vertex
        # while loops align with loop_triangles
        debug('on_update_geometry - loops_len={}'.format(len(mesh.loops)))
        self.lib.CopyVertices(
            name_buf,
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices)
        )

        debug('on_update_geometry - loop_triangles_len={} '.format(len(mesh.loop_triangles)))
        self.lib.CopyLoopTriangles(
            name_buf,
            mesh.loop_triangles[0].as_pointer(),
            len(mesh.loop_triangles)
        )

        # TODO: UVs?

        eval_obj.to_mesh_clear()

    def on_update_object_material(self, obj):
        """Pass an object's material reference to the bridge

        Parameters:
            obj (bpy.types.Object):             The object that was updated
        """
        debug('on_update_object_material - name={}'.format(obj.name))

        mat_name_buf = get_string_buffer(
            get_material_unity_name(obj.active_material)
        )

        self.lib.SetObjectMaterial(
            get_string_buffer(obj.name),
            mat_name_buf
        )

    def on_update_material(self, mat):
        """
        Parameters:
            mat (bpy.types.Material)
            update (bpy.types.DepsgraphUpdate): Information about ID that was updated
        """
        debug('on_update_material - name={}'.format(mat.name))

        mat_name_buf = get_string_buffer(
            get_material_unity_name(mat)
        )

        # TODO: This may pass in objects that aren't tracked yet by the bridge.
        # Is that fine? (Probably safe to ignore errors for these)
        # Could also iterate self.objects here and pull each from bpy.data.objects
        for obj in bpy.context.scene.objects:
            if obj.active_material:
                self.lib.SetObjectMaterial(
                    get_string_buffer(obj.name),
                    mat_name_buf
                )


def bridge_driver() -> BridgeDriver:
    """Retrieve the active driver singleton

    Returns:
        BridgeDriver
    """
    if 'COHERENCE' not in bpy.app.driver_namespace:
        bpy.app.driver_namespace['COHERENCE'] = BridgeDriver()

    return bpy.app.driver_namespace['COHERENCE']
