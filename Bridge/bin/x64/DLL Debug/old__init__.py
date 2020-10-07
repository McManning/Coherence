
bl_info = {
    'name': 'Unity Viewport Renderer',
    'description': 'Use the Unity Engine as a viewport renderer',
    'author': 'Chase McManning',
    'version': (0, 1, 0),
    'blender': (2, 82, 0),
    'category': 'Render'
}

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

class InteropMatrix4x4(Structure):
    _fields_ = [
        ('m00', c_float),
        ('m33', c_float),
        ('m23', c_float),
        ('m13', c_float),
        ('m03', c_float),
        ('m32', c_float),
        ('m22', c_float),
        ('m02', c_float),
        ('m12', c_float),
        ('m21', c_float),
        ('m11', c_float),
        ('m01', c_float),
        ('m30', c_float),
        ('m20', c_float),
        ('m10', c_float),
        ('m31', c_float),
    ]

class InteropVector2(Structure):
    _fields_ = [
        ('x', c_float),
        ('y', c_float),
    ]

class InteropVector3(Structure):
    _fields_ = [
        ('x', c_float),
        ('y', c_float),
        ('z', c_float),
    ]

class InteropCamera(Structure):
    _fields_ = [
        ('width', c_int),
        ('height', c_int),
        ('lens', c_float),
        ('position', InteropVector3),
        ('forward', InteropVector3),
        ('up', InteropVector3),
    ]

class RenderTextureData(Structure):
    _fields_ = [
        ('viewportId', c_int),
        ('width', c_int),
        ('height', c_int),
        ('frame', c_int),
        ('pixels', POINTER(c_ubyte))
    ]

def identity():
    mat = InteropMatrix4x4()
    mat.m00 = 1
    mat.m11 = 1
    mat.m22 = 1
    mat.m33 = 1
    return mat

def to_interop_matrix4x4(mat):
    """Convert the input matrix to an InteropMatrix4x4

    Parameters:
        mat (float[]):  float multi-dimensional array of 4 * 4 items in [-inf, inf]. E.g.
                        ((1.0, 0.0, 0.0, 0.0), (0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0))
    Returns:
        InteropMatrix4x4
    """
    result = InteropMatrix4x4()
    result.m00 = mat[0][0]
    result.m01 = mat[0][1]
    result.m02 = mat[0][2]
    result.m03 = mat[0][3]

    result.m10 = mat[1][0]
    result.m11 = mat[1][1]
    result.m12 = mat[1][2]
    result.m13 = mat[1][3]

    result.m20 = mat[2][0]
    result.m21 = mat[2][1]
    result.m22 = mat[2][2]
    result.m23 = mat[2][3]

    result.m30 = mat[3][0]
    result.m31 = mat[3][1]
    result.m32 = mat[3][2]
    result.m33 = mat[3][3]

    return result

def to_interop_vector3(vec):
    """Convert a Blender Vector to an interop type for C#
    
    Parameters:
        vec (mathutils.Vector)

    Returns:
    """
    result = InteropVector3()
    result.x = vec[0]
    result.y = vec[1]
    result.z = vec[2]

    return result

def to_interop_vector2(vec):
    """Convert a Blender Vector to an interop type for C#
    
    Parameters:
        vec (mathutils.Vector)

    Returns:
    """
    result = InteropVector2()
    result.x = vec[0]
    result.y = vec[1]

    return result

def to_interop_int_array(arr):
    """Convert the array of ints to an interop type for C# int[]
    
    Parameters:
        arr (int[])
    
    Returns: 
        c_int*
    """
    result = (c_int*len(arr))()
    for i in range(len(arr)):
        result[i] = arr[i]

    return result

def generate_unique_id():
    """Create a unique Uint32 bridge ID for the object"""
    # TODO: collision handling and all that.
    #id = time.time() * 1000000 - random.random()
    id = int.from_bytes(os.urandom(2), byteorder='big')
    return id
      
def is_supported_object(obj):
    """Test if the given object can be sent to the bridge
    
    Parameters:
        obj (bpy.types.Object)
    
    Returns:
        boolean: True if supported
    """
    
    if obj.type == 'MESH':
        return True
    
    return False

def is_renamed(obj):
    """Test if the given object has been renamed at some point.
    The first call to this (or apply_rename) will always be False.

    This will constantly return true until apply_rename() 
    is called on the object.
    
    Parameters:
        obj (bpy.types.Object)

    Returns:
        boolean: True if it has been renamed.
    """
    # It's difficult to identify renamed vs copied in Blender,
    # so we instead track both the memory address + name together
    # to check for a change. If the address changes alongside the name,
    # then it was probably copied. If it has the same address and a new 
    # name, then it was renamed.
    try:
        return obj['prev_name'] != obj.name and obj['prev_ptr'] == obj.as_pointer()
    except KeyError:
        # Haven't queried yet, so assume false?
        # This is awful.
        apply_rename(obj)
        return False

def apply_rename(obj): 
    """Apply a rename to an object so that is_renamed() no longer returns true.

    Parameters:
        obj (bpy.types.Object)
    """
    obj['prev_name'] = obj.name 
    obj['prev_ptr'] = obj.as_pointer()

def get_objects_with_material(mat):
    """Aggregation for objects with a material reference

    Parameters:
        mat (bpy.types.Material)

    Returns:
        set(bpy.types.Object)
    """
    results = set()
    for obj in bpy.context.scene.objects: # bpy.data.scenes[0]
        for slot in obj.material_slots:
            if slot.material == mat:
                results.add(obj)

    return results 

def bridge_driver():
    """Retrieve the active driver singleton
    
    Returns:
        BridgeDriver
    """
    if 'FOO' not in bpy.app.driver_namespace:
        bpy.app.driver_namespace['FOO'] = BridgeDriver()

    return bpy.app.driver_namespace['FOO']
    
def log(msg):
    print(msg, flush = True)
    
def debug(msg):
    print(msg, flush = True)
    pass 

def error(msg):
    print('ERROR: ' + msg, flush = True)

def warning(msg):
    print('WARNING: ' + msg, flush = True)

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
        path = os.path.dirname(os.path.abspath(__file__)) + os.path.sep + 'Bridge.dll'
        log('Loading DLL from {}'.format(path))

        self.lib = cdll.LoadLibrary(path)

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
        log('__del__ on bridge')

        # bpy.types.SpaceView3D.draw_handler_remove(post_view_draw, 'WINDOW')

    def setup(self, scene):
        """
        Parameters:
            scene (bpy.types.Scene)
        """
        log('Starting the DCC')
        self.lib.Start()

        self.add_all_from_scene(scene)

        bpy.app.handlers.depsgraph_update_post.append(self.on_depsgraph_update)
        
        self.active = True
        bpy.app.timers.register(self.on_tick)

    def add_all_from_scene(self, scene):
        """Add all objects in the scene to the bridge
        
        Parameters:
            scene (bpy.types.Scene)
        """

        # Send add events for all objects currently in the scene
        for obj in scene.objects:
            if is_supported_object(obj):
                self.on_add_object(obj)
                self.object_ids.add(get_object_uid(obj))

    def teardown(self):
        log('DCC teardown')
        self.lib.Shutdown()

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

    def remove_viewport(self, id):
        """Remove a RenderEngine instance as a tracked viewport

        Parameters:
            render_engine (FooRenderEngine)
        """
        log('***REMOVE VIEWPORT {}'.format(id))

        del self.viewports[id]
        self.lib.RemoveViewport(id)

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

        # Tag all active render engines for a redraw
        for v in self.viewports.items():
            try:
                v[1].on_update()
            except e:
                error(sys.exc_info()[0])

        self.lib.Update()
        self.lib.ConsumeRenderTextures()

        return 0.0001
        #return 0.06 # 60 FPS update rate

        # return 1.0
    
    def on_depsgraph_update(self, scene, depsgraph):
        """Sync the bridge with the scene's dependency graph on each update
        
        Parameters:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        debug('on depsgraph update')
        current_ids = set()
        
        # Check for added objects
        for obj in scene.objects:
            if is_supported_object(obj):
                uid = get_object_uid(obj)
                if uid not in self.object_ids:
                    self.on_add_object(obj)
                
                current_ids.add(uid)
                
        # Check for removed objects
        removed_ids = self.object_ids - current_ids
        for uid in removed_ids:
            self.on_remove_object(uid)
        
        log('Prev: {}'.format(self.object_ids))
        log('Current: {}'.format(current_ids))
        log('Removed: {}'.format(removed_ids))
        
        self.object_ids = current_ids

        # Check for updates to objects (geometry changes, transform changes, etc)
        for update in depsgraph.updates:
            if type(update.id) == bpy.types.Material:
                self.on_update_material(update)
            elif type(update.id) == bpy.types.Object:
                # Get the real object, not the copy in the update
                # obj = bpy.data.objects.get(update.name)

                if update.is_updated_transform:
                    self.on_update_transform(update)
                elif update.is_updated_geometry:
                    self.on_update_geometry(update, depsgraph)
                else:
                    print('Other update for {}'.format(update.id))
                
                # A material name can change if ..
                # TODO: didn't even check if a rename of a mat will depsgraph the object.
                
                self.on_update_object_material(update)


    def on_add_object(self, obj):
        """Notify the bridge that the object has been added to the scene
        
        Parameters:
            obj (bpy.types.Object)
        """
        uid = get_object_uid(obj)

        log('BRIDGE: Add object uid={}, obj={}'.format(uid, obj))
        
        self.lib.AddMeshObjectToScene(
            uid, 
            create_string_buffer(obj.name.encode()), 
            to_interop_matrix4x4(obj.matrix_world)
        )

        mesh = obj.data

        # Ensure triangulated faces are available
        mesh.calc_loop_triangles()

        # Need both vertices and loops. Vertices store normal information per-vertex
        # while loops align with loop_triangles
        debug('{} loops starting at {}'.format(len(mesh.loops), mesh.loops[0].as_pointer()))
        self.lib.CopyVertices(
            uid,
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices)
        )

        debug('{} loop_triangles starting at {}'.format(len(mesh.loop_triangles), mesh.loop_triangles[0].as_pointer()))
        self.lib.CopyLoopTriangles(
            uid,
            mesh.loop_triangles[0].as_pointer(),
            len(mesh.loop_triangles)
        )
    
    def on_remove_object(self, id):
        """Notify the bridge that the object has been removed from the scene
        
        Parameters:
            id (int): Unique Object ID shared with the Bridge
        """
        if id < 0: return
    
        log('BRIDGE: Remove Object {}'.format(id))
        self.lib.RemoveObjectFromScene(id)

    def on_update_transform(self, update):
        """Notify the bridge that the object has been transformed in the scene
        
        Parameters:
            update (bpy.types.DepsgraphUpdate): Information about ID that was updated
        """
        obj = bpy.data.objects.get(update.id.name)
        uid = get_object_uid(obj)
        debug('BRIDGE: Transform Object uid={}, obj={}'.format(uid, obj))

        for slot in obj.material_slots:
            if slot.material == mat:
                results.add(obj)

        self.lib.SetObjectTransform(
            uid,
            to_interop_matrix4x4(obj.matrix_world)
        )

    def on_update_object_material(self, update):
        """Pass an object's material reference to the bridge
        
        Parameters:
            update (bpy.types.DepsgraphUpdate): Information about ID that was updated
        """
        obj = bpy.data.objects.get(update.id.name)
        uid = get_object_uid(obj)

        try:
            idx = obj.active_material_index
            mat = obj.material_slots[idx].material

            name = mat.name
            if mat.use_override_name:
                name = mat.override_name

            self.lib.SetObjectMaterial(uid, name)
        except:
            self.lib.SetObjectMaterial(uid, "Default")

    def on_update_geometry(self, update, depsgraph):
        """Notify the bridge that object geometry has changed
        
        Parameters:
            update (bpy.types.DepsgraphUpdate): Information about ID that was updated
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        obj = bpy.data.objects.get(update.id.name)

        uid = get_object_uid(obj)
        debug('BRIDGE: Update geometry uid={}, obj={}'.format(uid, obj))
        
        eval_obj = obj.evaluated_get(depsgraph)
        mesh = eval_obj.to_mesh()

        # Ensure triangulated faces are available
        mesh.calc_loop_triangles()

        # Need both vertices and loops. Vertices store normal information per-vertex
        # while loops align with loop_triangles
        debug('{} loops starting at {}'.format(len(mesh.loops), mesh.loops[0].as_pointer()))
        self.lib.CopyVertices(
            uid,
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices)
        )

        debug('{} loop_triangles starting at {}'.format(len(mesh.loop_triangles), mesh.loop_triangles[0].as_pointer()))
        self.lib.CopyLoopTriangles(
            uid,
            mesh.loop_triangles[0].as_pointer(),
            len(mesh.loop_triangles)
        )

        eval_obj.to_mesh_clear()
    
    def on_update_material(self, update):
        """
        Parameters:
            update (bpy.types.DepsgraphUpdate): Information about ID that was updated
        """
        # if not hasattr(mat, 'bridge_uid'):
        #    mat['bridge_uid'] = os.urandom(2)
        
        # Get the source mat in bpy.data
        mat = bpy.data.materials.get(update.id.name)
        print('id={}, as_pointer={}'.format(id(mat), mat.as_pointer()))

        try:
            uid = mat['bridge_uid']
        except KeyError as e:
            print('Creating new key on {} because of {}'.format(id(mat), e))
            uid = os.urandom(2)
            mat['bridge_uid'] = uid

        print('DEPSGRAPH Update id={}, type={}, uid={}'.format(mat.name, type(mat), uid))


def get_object_uid(obj) -> int:
    """Retrieve a unique identifier that exists throughout the lifetime of an object
    
    Parameters:
        obj (bpy.types.Object)
    """
    # TODO: Improve on this. I can't guarantee that Blender
    # won't reallocate an instance to somewhere else. But we can't
    # store a UID on an IntProperty or the object dictionary because
    # it'll be copied with the object. Nor can we use the name,
    # because a renamed object will just be a new object.
    return obj.as_pointer() & 0xffffffff


def get_material_uid(mat) -> int:
    """Retrieve a unique identifier that exists throughout the lifetime of a material
    
    Parameters:
        mat (bpy.types.Material)
    """
    # TODO: Same as above
    return mat.as_pointer() & 0xffffffff


class FooRenderEngine(bpy.types.RenderEngine):
    bl_idname = "foo_renderer"
    bl_label = "Unity Renderer"
    bl_use_preview = False

    viewport_id: int

    # GLSL texture bind code
    bindcode: int 

    def __init__(self):
        """Called when a new render engine instance is created. 

        Note that multiple instances can exist @ once, e.g. a viewport and final render
        """
        log('Init RenderEngine at {}'.format(id(self)))

        self.visible_ids = []
        
        self.shader = gpu.shader.from_builtin('2D_IMAGE')
        self.batch = batch_for_shader(self.shader, 'TRI_FAN', {
            'pos': ((0, 0), (100, 0), (100, 100), (0, 100)),
            'texCoord': ((0, 0), (1, 0), (1, 1), (0, 1)),
        })

        self.lock = threading.Lock()

        self.viewport_id = int.from_bytes(os.urandom(2), byteorder='big')

        self.bindcode = -1
        self.texture_width = 0
        self.texture_height = 0
        self.texture_frame = -1
        
        self.camera = InteropCamera()
        self.camera.width = 100
        self.camera.height = 100
        
        bridge_driver().add_viewport(self)
 
    def __del__(self):
        log('Shutdown RenderEngine at {}'.format(id(self)))

        """Notify the bridge that this viewport is going away"""
        try:
            # TODO: Release GL texture?
            bridge_driver().remove_viewport(self.viewport_id)
        except:
            pass

    def render(self, depsgraph):
        """Handle final render (F12) and material preview window renders"""
        pass
    
    def view_update(self, context, depsgraph):
        """Called when a scene or 3D viewport changes"""
        # Update our list of objects visible to this viewport
        visible_ids = []
        for obj in depsgraph.scene.objects:
            if not obj.visible_get():
                continue
            
            uid = get_object_uid(obj)
            visible_ids.append(uid)
        
        # Only notify for a change if the list was modified
        visible_ids.sort()
        if visible_ids != self.visible_ids:
            self.on_changed_visible_ids(visible_ids)
        
    def on_update(self):
        """
            Update method called from the main driver on_tick.
            Performs all sync work with the bridge
        """
        lib = bridge_driver().lib
        lib.SetViewportCamera(self.viewport_id, self.camera)
        
        # # Poll for a new render texture image and upload
        # # to the GPU once we acquire a lock on the texture buffer
        # rt = lib.GetRenderTexture(self.viewport_id)

        # # Lock shared data access with the render thread (camera and texture data)
        # with self.lock:
        #     debug('[MAIN] Get lock')
        #     # Send updated camera data
        #     lib.SetViewportCamera(self.viewport_id, self.camera)
            
        #     # Upload RT to the GPU if it's valid
        #     self.update_render_texture(rt)
        #     debug('[MAIN] Done working, release lock')

        # Redraw the viewport with the new render texture
        # TODO: Only if we've updated (but we should be getting one at 60 FPS+)
        self.tag_redraw()

    def dump_rt_to_image(self, rt):
        # https://blender.stackexchange.com/a/652
        image = bpy.data.images.new("Results", width=rt.width, height=rt.height)

        pixels = [None] * rt.width * rt.height * 4
        for i in range(rt.width * rt.height):
            src = i * 3
            dst = i * 4
            if i < 50:
                debug('{}, {}, {}'.format(rt.pixels[src], rt.pixels[src+1], rt.pixels[src+2]))
            pixels[dst] = rt.pixels[src] / 255.0
            pixels[dst+1] = rt.pixels[src+1] / 255.0
            pixels[dst+2] = rt.pixels[src+2] / 255.0
            pixels[dst+3] = 0.0

        image.pixels = pixels

        # alternatively, dump into numpy?

    def rebuild_texture(self, frame, width, height, pixels):
        # Invalid texture data. Skip
        if width == 0 or height == 0:
            debug('** Skip RT - Zero size for {}'.format(self.viewport_id))
            return

        # Same frame ID (thus same pixels). Skip
        if frame == self.texture_frame:
            debug('** Skip RT - Same Frame')
            return

        create_new = False
        
        # If we haven't created a resource ID yet, do so now.
        if self.bindcode == -1:
            buf = Buffer(GL_INT, 1)
            glGenTextures(1, buf)
            self.bindcode = buf[0]
            create_new = True
            log('Create viewport texture bind code {}'.format(self.bindcode))
            # self.dump_rt_to_image(rt)

        # If the render texture resizes, we'll need to reallocate
        if width != self.texture_width or height != self.texture_height:
            create_new = True
                
        glActiveTexture(GL_TEXTURE0) # TODO: Needed?
        glBindTexture(GL_TEXTURE_2D, self.bindcode)

        # pixels = np.empty(width * height * 3) # rt.pixels
        # p_pixels = Buffer(GL_UNSIGNED_BYTE, width * height * 3, pixels)
        # p_pixels = cast(rt.pixels, c_void_p).value

        if create_new:
            log('** NEW Texture {} x {}'.format(width, height))
            
            # TODO: Would be nice if I didn't have to match pixel resolution here.
            # Use an alternate shader that doesn't need this?
            self.batch = batch_for_shader(self.shader, 'TRI_FAN', {
                'pos': ((0, 0), (width, 0), (width, height), (0, height)),
                'texCoord': ((0, 0), (1, 0), (1, 1), (0, 1)),
            })

            debug('glTexImage2D using {} x {} at {}'.format(width, height, pixels))

            # GL_BGRA is preferred on Windows according to https://www.khronos.org/opengl/wiki/Common_Mistakes#Slow_pixel_transfer_performance
            # Additionally - we're doing 24 BPP to avoid transferring the alpha channel (upper bound 2 MB per frame)
            # but that'll also probably be a slowdown when uploading. Need to benchmark both solutions. And maybe 
            # eventually pack multiple viewport outputs to the same render texture.
            glPixelStorei(GL_UNPACK_ALIGNMENT, 1)
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB8, width, height, 0, GL_RGB, GL_UNSIGNED_BYTE, pixels)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST)
            
            # self.dump_rt_to_image(rt)
        else: # We can just write pixels into the existing space
            debug('glTexSubImage2D using {} x {} at {}'.format(width, height, pixels))
            glPixelStorei(GL_UNPACK_ALIGNMENT, 1)
            glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, width, height, GL_RGB, GL_UNSIGNED_BYTE, pixels)

        debug('Written. Setting self.texture_*')

        # Track to compare to the next read
        self.texture_width = width 
        self.texture_height = height 
        self.texture_frame = frame

        debug('Done')

    def on_changed_visible_ids(self, visible_ids):
        """Notify the bridge that the visibility list has changed
        
        Parameters:
            visible_ids (List[int])
        """
        debug('BRIDGE: Update Visible ID List {}'.format(visible_ids))
        self.visible_ids = visible_ids
        
        visible_ids_ptr = (c_int * len(visible_ids))(*visible_ids)
        bridge_driver().lib.SetVisibleObjects(
            self.viewport_id, 
            visible_ids_ptr,
            len(visible_ids)
        )

    def update_viewport_camera(self, context):
        """Update the current InteropCamera instance from the render thread context

        Parameters:
            context (bpy.types.Context): Render thread context
        """
        space = context.space_data
        region3d = space.region_3d # or just context.region_3d?
        region = context.region

        self.camera.lens = space.lens
        self.camera.position = to_interop_vector3(region3d.view_matrix.inverted().translation)
        self.camera.forward = to_interop_vector3(region3d.view_rotation @ Vector((0.0, 0.0, -1.0)))
        self.camera.up = to_interop_vector3(region3d.view_rotation @ Vector((0.0, 1.0, 0.0)))

        self.camera.width = region.width 
        self.camera.height = region.height

        # debug('lens', space.lens)
        # debug('sensor_width', space.camera.data.sensor_width)
        # debug('sensor_height', space.camera.data.sensor_height)
        # debug('view_perspective', region.view_perspective)
        # debug('view_distance', region.view_distance)
        # debug('view_camera_zoom', region.view_camera_zoom)
        # debug('view_camera_offset', region.view_camera_offset)
        # debug('is_perspective', region.is_perspective)
        # debug('position', position)
        # debug('forward', forward)
        # debug('up', up)
        # debug('view_matrix', region.view_matrix)

    def stress_test_texture(self):
        # TEST: Works
        width = self.camera.width 
        height = self.camera.height

        # the fact that this isn't showing (1, 1, 1) means I have another issue.
        pixels = np.ones(width * height * 3) # rt.pixels
        p_pixels = pixels.ctypes.data_as(POINTER(c_float))
        vp_pixels = cast(p_pixels, c_void_p).value

        self.rebuild_texture(
            self.texture_frame + 1,
            width,
            height,
            vp_pixels
        )

    def update_render_texture(self):
        lib = bridge_driver().lib
        rt = lib.GetRenderTexture(self.viewport_id)

        # pixels = Buffer(GL_UNSIGNED_BYTE, rt.width * rt.height * 3, rt.pixels)
        p_pixels = cast(rt.pixels, c_void_p).value

        # Replace. Don't upload that memory - upload our own temp.
        # pixels = np.ones(rt.width * rt.height * 3) # rt.pixels
        # p_pixels = cast(pixels.ctypes.data_as(POINTER(c_float)), c_void_p).value

        self.rebuild_texture(
            rt.frame,
            rt.width,
            rt.height,
            p_pixels
        )

        # TODO: Dealloc the replacement memory manually?

        # Not calling this does nothing. So what the actual fuck?
        # Is calling the DLL from a separate thread somehow breaking it?
        lib.ReleaseRenderTextureLock(self.viewport_id)

    def view_draw(self, context, depsgraph):
        """Called whenever Blender redraws the 3D viewport"""
        scene = depsgraph.scene

        # debug('[RENDER] start')
        self.update_viewport_camera(context)

        self.update_render_texture()
        
        self.bind_display_space_shader(scene)
        self.shader.bind()

        # glEnable(GL_DEPTH_TEST)
        
        clear_color = scene.foo.clear_color
        glClearColor(clear_color[0], clear_color[1], clear_color[2], 1.0)
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT)

        if self.bindcode != -1:
            glActiveTexture(GL_TEXTURE0)
            glBindTexture(GL_TEXTURE_2D, self.bindcode)

        self.shader.uniform_int('image', 0)
        self.batch.draw(self.shader)
        
        # # We grab the lock to be able to write the current camera
        # # state as well as avoid reading the render texture while
        # # it's in the process of being uploaded in the main thread
        # with self.lock:
        #     debug('[RENDER] get lock')
        #     # Camera updates need to happen in the render thread since we 
        #     # don't get notified of changes through view_update()
            # self.update_viewport_camera(context) 
        
        #     # if self.bindcode != -1:
        #     #     glActiveTexture(GL_TEXTURE0)
        #     #     glBindTexture(GL_TEXTURE_2D, self.bindcode)

        #     # self.shader.uniform_int('image', 0)
        #     # self.batch.draw(self.shader)

            # debug('[RENDER] release lock')
        
        self.unbind_display_space_shader()

        # glDisable(GL_BLEND)
        # debug('[RENDER] end')


#region Settings

class FooSceneSettings(PropertyGroup):
    """Collection of user configurable settings for the renderer"""

    clear_color: FloatVectorProperty(  
        name='Clear Color',
        subtype='COLOR',
        default=(0.15, 0.15, 0.15),
        min=0.0, max=1.0,
        description='Background color of the scene'
    )
    
    @classmethod
    def register(cls):
        bpy.types.Scene.foo = PointerProperty(
            name='Foo Renderer Settings',
            description='',
            type=cls
        )
        
    @classmethod
    def unregister(cls):
        del bpy.types.Scene.foo

class FooObjectSettings(PropertyGroup):
    # bridge_id: IntProperty(
    #     name='Bridge ID',
    #     default=-1,
    #     description='Unique ID used by the bridge DLL'
    # )
    
    @classmethod
    def register(cls):
        bpy.types.Object.foo = PointerProperty(
            name='Foo Object Settings',
            description='',
            type=cls
        )
        
    @classmethod
    def unregister(cls):
        del bpy.types.Object.foo

def change_sync_texture(self, context):
    """
    Parameters:
        self (FooMaterialSettings)
        context (bpy.types.Context)
    """
    img = self.sync_texture # bpy.types.Image

    print('CHANGE Texture2D Sync image={}'.format(img))


def update_sync_material_name(self, context):
    mat = context.material 
    uid = get_material_uid(mat)

    if self.use_override_name:
        name = self.override_name
    else:
        name = mat.name

    print('UPDATE Material sync for uid={}, name={}'.format(uid, name))


def update_sync_texture_settings(self, context):
    mat = context.material 
    uid = get_material_uid(mat)

    if self.use_override_name:
        name = self.override_name
    else:
        name = mat.name

    if self.use_sync_texture:
        print('UPDATE Texture2D sync for uid={}, name={}'.format(uid, name))
    else:
        print('DISABLE/SKIP Texture2D sync for uid={}, name={}'.format(uid, name))


class FooMaterialSettings(PropertyGroup):
    use_override_name: BoolProperty(
        name='Use custom Unity Material name',
        update=update_sync_material_name
    )

    override_name: StringProperty(
        name='Unity Material Name',
        update=update_sync_material_name
    )

    use_sync_texture: BoolProperty(
        name='Use Texture Syncing',
        description='Sync a Texture2D channel in the Unity Material with an editable Blender Image',
        update=update_sync_texture_settings
    )

    sync_texture: PointerProperty(
        name='Sync Texture',
        description='Blah blah',
        type=bpy.types.Image,
        update=change_sync_texture
        # TODO: This type reference may not work when loading the plugin.
        # bpy.types isn't available when instantiating plugins, so this'll
        # most likely fail. Need some sort of late-load behavior here. 
    )

    sync_texture_map: EnumProperty(
        name='Target Map',
        items=[
            # Default for custom material shaders
            ('_MainTex', 'Main Tex', '', 1),
            
            #  URP property names
            ('_BaseMap', 'Base Map', '', 2),
            ('_BumpMap', 'Bump Map', '', 3),
            ('_EmissionMap', 'Emission Map', '', 4),
            ('_MetallicGlossMap', 'Metallic/Gloss Map', '', 5),
            ('_OcclusionMap', 'Occlusion Map', '', 6),
            ('_SpecGlossMap', 'Specular/Gloss Map', '', 7),

            # Other standard property names
            ('_ParallaxMap', 'Parallax Map', '', 8),
            ('_DetailMask', 'Detail Mask', '', 9),
            ('_DetailNormalMap', 'Detail Normal Map', '', 10),
            ('_DetailAlbedoMap', 'Detail Albedo Map', '', 11),

            # Custom (read from custom_sync_texture_map)
            ('CUSTOM', 'Custom Property Name', '', 100)
        ],
        update=update_sync_texture_settings
    )

    custom_sync_texture_map: StringProperty(
        name='Property Name',
        description='Use a property name matching the Unity shader (e.g. _MainTex)',
        update=update_sync_texture_settings
    )

    @classmethod
    def register(cls):
        bpy.types.Material.foo = PointerProperty(
            name='Foo Material Settings',
            description='',
            type=cls
        )
    
    @classmethod
    def unregister(cls):
        del bpy.types.Material.foo

#endregion Settings

#region Operators

class SetupBridgeOperator(bpy.types.Operator):
    """Tooltip"""
    bl_idname = 'scene.setup_bridge'
    bl_label = 'Start Unity Bridge'
 
    @classmethod
    def poll(cls, context):
        return context.active_object is not None
 
    def execute(self, context):
        bridge = bridge_driver()

        if not bridge.is_ready():
            bridge.setup(context.scene)
            self.__class__.bl_label = 'Stop Unity Bridge'
        else:
            bridge.teardown()
            self.__class__.bl_label = 'Start Unity Bridge'

        return { 'FINISHED' }


class ForceResizeOperator(bpy.types.Operator):
    """Tooltip"""
    bl_idname = 'scene.force_resize'
    bl_label = 'Force Viewport Resize'
 
    @classmethod
    def poll(cls, context):
        return context.active_object is not None
 
    def execute(self, context):
        bridge = bridge_driver()
        region = context.region 

        for v in bridge.viewports.items():
            v[1].on_change_dimensions(region.width, region.height)

        return { 'FINISHED' }


# class BridgeTimerOperator(bpy.types.Operator):
#     """
#         Wonky way of performing a timer that also 
#         can also flag the viewport as dirty every execution
#     """
#     bl_idname = "wm.bridge_timer_operator"
#     bl_label = "Bridge Timer Operator"

#     _timer = None

#     def modal(self, context, event):
#         context.area.tag_redraw()

#         bridge = bridge_driver()
#         bridge.on_tick()

#         if event.type in {'RIGHTMOUSE', 'ESC'}:
#             self.cancel(context)
#             return {'CANCELLED'}

#         if event.type == 'TIMER':
#             debug('TIMER')

#         return {'PASS_THROUGH'}

#     def execute(self, context):
#         wm = context.window_manager
#         self._timer = wm.event_timer_add(0.1, window=context.window)
#         wm.modal_handler_add(self)
#         return {'RUNNING_MODAL'}

#     def cancel(self, context):
#         wm = context.window_manager
#         wm.event_timer_remove(self._timer)


#endregion Operators

#region Panels

class BasePanel(Panel):
    bl_space_type = 'PROPERTIES'
    bl_region_type = 'WINDOW'
    bl_context = 'render'
    COMPAT_ENGINES = {FooRenderEngine.bl_idname}

    @classmethod
    def poll(cls, context):
        return context.engine in cls.COMPAT_ENGINES


class FOO_RENDER_PT_settings(BasePanel):
    """Parent panel for renderer settings"""
    bl_label = 'Foo Renderer Settings'

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False
        
        settings = context.scene.foo
        
        bridge = bridge_driver()

        if bridge.is_ready():
            layout.label(text='Running Bridge v{}'.format(bridge.version))

        layout.operator(
            SetupBridgeOperator.bl_idname, 
            text=SetupBridgeOperator.bl_label
        )

        if not bridge.is_connected():
            layout.label(text='Not connected')
        else:
            layout.label(text='Connected to Unity')


class FOO_RENDER_PT_settings_viewport(BasePanel):
    """Global viewport configurations"""
    bl_label = 'Viewport'
    bl_parent_id = 'FOO_RENDER_PT_settings'

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False
        
        settings = context.scene.foo

        col = layout.column(align=True)
        col.prop(settings, 'clear_color')


class FOO_LIGHT_PT_light(BasePanel):
    """Custom per-light settings editor for this render engine"""
    bl_label = 'Light'
    bl_context = 'data'
    
    @classmethod
    def poll(cls, context):
        return context.light and BasePanel.poll(context)

    def draw(self, context):
        layout = self.layout
        self.layout.label(text='Not supported. Use lights within Unity.')

class FOO_MATERIAL_PT_settings_sync(BasePanel):
    bl_label = 'Texture Sync'
    bl_parent_id = 'FOO_MATERIAL_PT_settings'
    
    def draw_header(self,context):
        self.layout.prop(context.material.foo, 'use_sync_texture', text="", toggle=False)

    def draw(self, context):
        mat = context.material 
        settings = mat.foo

        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        col = layout.column()

        # TODO: If enabled, no other material can have this enabled.
        # ... in theory. That is. There's not *really* a reason we can't
        # have multiple materials enable sync except for a painful
        # initial load. 
        if settings.use_sync_texture:
            col.label(text='')

            col.prop(settings, 'sync_texture_map')
            map_name = settings.sync_texture_map
            
            if map_name == 'CUSTOM':
                col.prop(settings, 'custom_sync_texture_map')
                map_name = settings.custom_sync_texture_map

            col.template_ID(
                settings, 
                'sync_texture', 
                new='image.new', 
                open='image.open', 
                text='Image'
            )

            if settings.use_override_name:
                name = settings.override_name
            else:
                name = mat.name
    
            col.label(text='Syncing to: {}.{}'.format(name, map_name))

class FOO_MATERIAL_PT_settings(BasePanel):
    bl_label = 'Unity Material Settings'
    bl_context = 'material'
    
    @classmethod 
    def poll(cls, context):   
        return context.material and BasePanel.poll(context)

    def draw(self, context):
        mat = context.material 
        settings = mat.foo

        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False
        
        col = layout.column()

        col.prop(settings, 'use_override_name')
        if settings.use_override_name:
            col.prop(settings, 'override_name')
        

class FOO_PT_context_material(BasePanel):
    """This is based on CYCLES_PT_context_material to provide the same material selector menu"""
    bl_label = ''
    bl_context = 'material'
    bl_options = {'HIDE_HEADER'}

    @classmethod
    def poll(cls, context):
        if context.active_object and context.active_object.type == 'GPENCIL':
            return False
       
        return (context.material or context.object) and BasePanel.poll(context)

    def draw(self, context):
        layout = self.layout

        mat = context.material
        ob = context.object
        slot = context.material_slot
        space = context.space_data

        if ob:
            is_sortable = len(ob.material_slots) > 1
            rows = 1
            if (is_sortable):
                rows = 4

            row = layout.row()

            row.template_list("MATERIAL_UL_matslots", "", ob, "material_slots", ob, "active_material_index", rows=rows)

            col = row.column(align=True)
            col.operator("object.material_slot_add", icon='ADD', text="")
            col.operator("object.material_slot_remove", icon='REMOVE', text="")

            col.menu("MATERIAL_MT_context_menu", icon='DOWNARROW_HLT', text="")

            if is_sortable:
                col.separator()

                col.operator("object.material_slot_move", icon='TRIA_UP', text="").direction = 'UP'
                col.operator("object.material_slot_move", icon='TRIA_DOWN', text="").direction = 'DOWN'

            if ob.mode == 'EDIT':
                row = layout.row(align=True)
                row.operator("object.material_slot_assign", text="Assign")
                row.operator("object.material_slot_select", text="Select")
                row.operator("object.material_slot_deselect", text="Deselect")

        split = layout.split(factor=0.65)

        if ob:
            split.template_ID(ob, "active_material", new="material.new")
            row = split.row()

            if slot:
                row.prop(slot, "link", text="")
            else:
                row.label()
        elif mat:
            split.template_ID(space, "pin_id")
            split.separator()

#endregion Panels

#region Plugin Registration

# Classes to (un)register as part of this addon
CLASSLIST = (
    FooRenderEngine,
    
    # Operators
    SetupBridgeOperator,
    # BridgeTimerOperator,
    ForceResizeOperator,
    
    # Settings
    FooSceneSettings,
    FooObjectSettings,
    FooMaterialSettings,
    
    # Renderer panels
    FOO_RENDER_PT_settings,
    FOO_RENDER_PT_settings_viewport,

    # Light panels
    FOO_LIGHT_PT_light,

    # Material panels
    FOO_MATERIAL_PT_settings,
    FOO_MATERIAL_PT_settings_sync,
    FOO_PT_context_material,
)

def get_panels():
    # Panels we *don't* register the RenderEngine with
    exclude_panels = {
        'VIEWLAYER_PT_filter',
        'VIEWLAYER_PT_layer_passes',
        'RENDER_PT_freestyle',
        'RENDER_PT_simplify',
        # TODO: Remove Color Management panel - done via Unity
    }

    panels = []
    for panel in bpy.types.Panel.__subclasses__():
        if hasattr(panel, 'COMPAT_ENGINES') and 'BLENDER_RENDER' in panel.COMPAT_ENGINES:
            if panel.__name__ not in exclude_panels:
                panels.append(panel)

    return panels

def register():
    """Register panels, operators, and the render engine itself"""
    for cls in CLASSLIST:
        bpy.utils.register_class(cls)

    for panel in get_panels():
        panel.COMPAT_ENGINES.add(FooRenderEngine.bl_idname)

    debug('Registering driver')
    if 'FOO' in bpy.app.driver_namespace:
        bpy.app.driver_namespace['FOO'].teardown()
        del bpy.app.driver_namespace['FOO']

def unregister():
    """Unload everything previously registered"""
    debug('Unregistering driver')

    for cls in CLASSLIST:
        bpy.utils.unregister_class(cls)

    for panel in get_panels():
        if FooRenderEngine.bl_idname in panel.COMPAT_ENGINES:
            panel.COMPAT_ENGINES.remove(FooRenderEngine.bl_idname)
       
    if 'FOO' in bpy.app.driver_namespace:
        bpy.app.driver_namespace['FOO'].teardown()
        del bpy.app.driver_namespace['FOO']
        
if __name__ == "__main__":
    register()

#endregion Plugin Registration
