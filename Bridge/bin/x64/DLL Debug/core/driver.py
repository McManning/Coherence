
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
    get_material_unity_name
)

from .interop import * 

def bridge_driver():
    """Retrieve the active driver singleton
    
    Returns:
        BridgeDriver
    """
    if 'FOO' not in bpy.app.driver_namespace:
        bpy.app.driver_namespace['FOO'] = BridgeDriver()

    return bpy.app.driver_namespace['FOO']
    
class BridgeDriver:
    """Respond to scene object changes and handle messaging through the Unity bridge"""

    object_ids = set()
    active: bool = False
    lib = None
    version = None

    # Mapping between viewport IDs and RenderEngine instances.
    # Weakref is used so that we don't hold onto RenderEngine references
    # since Blender uses __del__ to release them after use
    viewports = WeakValueDictionary()

    def __init__(self):
        # TODO: Better path lookup.
        path = os.path.dirname(os.path.dirname(os.path.abspath(__file__))) + os.path.sep + 'Bridge.dll'
        log('Loading DLL from {}'.format(path))

        self.lib = cdll.LoadLibrary(path)
        
        # Typehint all the API calls we actually need to typehint
        self.lib.Start.restype = c_int
        self.lib.SetViewportCamera.argtypes = (c_int, InteropCamera)
        self.lib.CopyVertices.argtypes = (c_int, c_void_p, c_uint, c_void_p, c_uint)
        self.lib.CopyLoopTriangles.argtypes = (c_int, c_void_p, c_uint)
        self.lib.GetRenderTexture.argtypes = (c_uint, )
        self.lib.GetRenderTexture.restype = RenderTextureData
        
        self.lib.ReleaseRenderTextureLock.argtypes = (c_uint, )
        self.lib.ReleaseRenderTextureLock.restype = c_int

        # bpy.types.SpaceView3D.draw_handler_add(post_view_draw, (), 'WINDOW', 'POST_PIXEL')

        self.version = self.lib.Version()

    def __del__(self):
        debug('__del__ on bridge')

        # bpy.types.SpaceView3D.draw_handler_remove(post_view_draw, 'WINDOW')

    def setup(self, scene):
        """
        Parameters:
            scene (bpy.types.Scene)
        """
        log('Starting the DCC')
        self.object_ids = set()

        if self.lib.Start() < 0:
            error('Could not start bridge: Internal error')
            return 

        self.active = True

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

    def teardown(self):
        log('DCC teardown')
        self.lib.Shutdown()

        # Turning off `active` will also destroy the `on_tick` timer.
        self.active = False
        
        if self.on_depsgraph_update in depsgraph_update_post:
            depsgraph_update_post.remove(self.on_depsgraph_update)

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
            render_engine (FooRenderEngine)
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
        # TODO: ask bridge
        return self.lib.IsConnectedToUnity()

    def is_ready(self) -> bool:
        """Is the driver ready to start accepting connections
        
        Returns:
            bool
        """
        return self.active 

    def on_tick(self):
        """Timer registered through bpy.app.timers to push/pull messages through the bridge"""
        if not self.active:
            log('Deactivating on_tick timer')
            return None

        # Push/pull shared message channels with Unity
        self.lib.Update()
        self.lib.ConsumeRenderTextures()

        # Tag all active render engines for a redraw
        for v in self.viewports.items():
            try:
                v[1].on_update()
            except e:
                error(sys.exc_info()[0])

        return 0.0001
        #return 0.06 # 60 FPS update rate

        # return 1.0

    def sync_tracked_objects(self, scene, depsgraph):
        """Add/remove objects from the bridge to match the scene

        Parameters:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        current_ids = set()
        
        # Check for added objects
        for obj in scene.objects:
            if is_supported_object(obj):
                uid = get_object_uid(obj)
                if uid not in self.object_ids:
                    self.on_add_object(obj, depsgraph)
                
                current_ids.add(uid)
                
        # Check for removed objects
        removed_ids = self.object_ids - current_ids
        for uid in removed_ids:
            self.on_remove_object(uid)
        
        # Update current tracked list
        self.object_ids = current_ids

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
        uid = get_object_uid(obj)
        mat_name = get_material_unity_name(obj.active_material)
        
        debug('on_add_object - name={}, uid={}'.format(obj.name, uid))

        self.lib.AddMeshObjectToScene(
            uid, 
            create_string_buffer(obj.name.encode()), 
            to_interop_matrix4x4(obj.matrix_world),
            create_string_buffer(mat_name.encode())
        )
        
        # Send up initial geometry as well
        self.on_update_geometry(obj, depsgraph)
    
    def on_remove_object(self, uid):
        """Notify the bridge that the object has been removed from the scene
        
        Parameters:
            uid (int): Unique Object ID shared with the Bridge
        """
        if uid < 0: return
    
        debug('on_remove_object - uid={}'.format(uid))

        self.lib.RemoveObjectFromScene(uid)

    def on_update_transform(self, obj):
        """Notify the bridge that the object has been transformed in the scene
        
        Parameters:
            obj (bpy.types.Object):             The object that was updated
        """
        uid = get_object_uid(obj)

        debug('on_update_transform - name={}, uid={}'.format(obj.name, uid))

        self.lib.SetObjectTransform(
            uid,
            to_interop_matrix4x4(obj.matrix_world)
        )

    def on_update_object_material(self, obj):
        """Pass an object's material reference to the bridge
        
        Parameters:
            obj (bpy.types.Object):             The object that was updated
        """
        uid = get_object_uid(obj)

        debug('on_update_object_material - name={}, uid={}'.format(obj.name, uid))

        name = get_material_unity_name(obj.active_material)
        name_buf = create_string_buffer(name.encode())

        self.lib.SetObjectMaterial(uid, name_buf)

    def on_update_geometry(self, obj, depsgraph):
        """Notify the bridge that object geometry has changed
        
        Parameters:
            obj (bpy.types.Object):             The object that was updated
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        uid = get_object_uid(obj)

        debug('on_update_geometry - name={}, uid={}'.format(obj.name, uid))

        eval_obj = obj.evaluated_get(depsgraph)
        mesh = eval_obj.to_mesh()

        # Ensure triangulated faces are available
        mesh.calc_loop_triangles()

        # Need both vertices and loops. Vertices store co/no information per-vertex
        # while loops align with loop_triangles
        debug('on_update_geometry - loops_len={}'.format(len(mesh.loops)))
        self.lib.CopyVertices(
            uid,
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices)
        )

        debug('on_update_geometry - loop_triangles_len={} '.format(len(mesh.loop_triangles)))
        self.lib.CopyLoopTriangles(
            uid,
            mesh.loop_triangles[0].as_pointer(),
            len(mesh.loop_triangles)
        )

        eval_obj.to_mesh_clear()
    
    def on_update_material(self, mat):
        """
        Parameters:
            mat (bpy.types.Material)
            update (bpy.types.DepsgraphUpdate): Information about ID that was updated
        """
        debug('on_update_material - name={}'.format(mat.name))

        name = get_material_unity_name(mat)
        name_buf = create_string_buffer(name.encode())

        # TODO: This may pass in objects that aren't tracked yet by the bridge.
        # Is that fine? (Probably safe to ignore errors for these)
        for obj in bpy.context.scene.objects:
            if obj.active_material:
                uid = get_object_uid(obj)
                self.lib.SetObjectMaterial(uid, name_buf)
        