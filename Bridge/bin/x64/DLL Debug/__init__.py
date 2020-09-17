
bl_info = {
    'name': 'Unity Bridge',
    'description': 'fooo bar',
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
   
def bridge_driver():
    """Retrieve the active driver singleton
    
    Returns:
        BridgeDriver
    """
    if 'FOO' not in bpy.app.driver_namespace:
        bpy.app.driver_namespace['FOO'] = BridgeDriver()

    return bpy.app.driver_namespace['FOO']
    
def log(msg):
    print(msg)
    
def debug(msg):
    # print(msg)
    pass 

def error(msg):
    print('ERROR: ' + msg)

def warning(msg):
    print('WARNING: ' + msg)

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

        self.version = self.lib.Version()

    def __del__(self):
        log('__del__ on bridge')

    def setup(self, scene):
        """
        Parameters:
            scene (bpy.types.Scene)
        """
        log('Starting the DCC')
        self.lib.Start()

        bpy.app.handlers.depsgraph_update_post.append(self.on_depsgraph_update)
        
        self.active = True
        bpy.app.timers.register(self.on_tick)
        
        # Send add events for all objects currently in the scene
        for obj in scene.objects:
            if is_supported_object(obj):
                self.on_add_object(obj)
                self.object_ids.add(obj.foo.bridge_id)

    def teardown(self):
        log('DCC teardown')
        self.lib.Shutdown()

        # # Windows-specific handling for freeing the DLL.
        # # See: https://stackoverflow.com/questions/359498/how-can-i-unload-a-dll-using-ctypes-in-python  
        # handle = self.lib._handle
        # del self.lib
        # self.lib = None

        # kernel32 = WinDLL('kernel32', use_last_error=True)    
        # kernel32.FreeLibrary.argtypes = [wintypes.HMODULE]
        # kernel32.FreeLibrary(handle)

        self.active = False
        
        if self.on_depsgraph_update in depsgraph_update_post:
            depsgraph_update_post.remove(self.on_depsgraph_update)

    def add_viewport(self, render_engine):
        """Add a RenderEngine instance as a tracked viewport

        Parameters:
            render_engine (FooRenderEngine)
        """
        log('Create Viewport {} at {} x {} from {}'.format(
            render_engine.viewport_id, 
            render_engine.width, 
            render_engine.height,
            id(render_engine)
        ))

        self.viewports[render_engine.viewport_id] = render_engine
        self.lib.AddViewport(render_engine.viewport_id, render_engine.width, render_engine.height)

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
        
        # Tag all render engines for a redraw
        # TODO: StructRNA has been removed - axe them as appropriate.
        for v in self.viewports.items():
            try:
                v[1].tag_redraw()
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
                if obj.foo.bridge_id not in self.object_ids:
                    self.on_add_object(obj)
                
                current_ids.add(obj.foo.bridge_id)
                
        # Check for removed objects
        removed_ids = self.object_ids - current_ids
        for id in removed_ids:
            self.on_remove_object(id)
        
        log('Prev: {}'.format(self.object_ids))
        log('Current: {}'.format(current_ids))
        log('Removed: {}'.format(removed_ids))
        
        self.object_ids = current_ids

        # Check for updates to objects (geometry changes, transform changes, etc)
        for update in depsgraph.updates:
            if type(update.id) == bpy.types.Object:
                if update.is_updated_transform:
                    self.on_update_transform(update.id)
                elif update.is_updated_geometry:
                     self.on_update_geometry(update.id, depsgraph)
                    
    def on_add_object(self, obj):
        """Notify the bridge that the object has been added to the scene
        
        Parameters:
            obj (bpy.types.Object)
        """
        obj.foo.bridge_id = generate_unique_id()
        log('BRIDGE: Add object {}'.format(obj.foo.bridge_id))
        
        self.lib.AddMeshObjectToScene(
            obj.foo.bridge_id, 
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
            obj.foo.bridge_id,
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices)
        )

        debug('{} loop_triangles starting at {}'.format(len(mesh.loop_triangles), mesh.loop_triangles[0].as_pointer()))
        self.lib.CopyLoopTriangles(
            obj.foo.bridge_id,
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

    def on_update_transform(self, obj):
        """Notify the bridge that the object has been transformed in the scene
        
        Parameters:
            obj (bpy.types.Object)
        """
        if obj.foo.bridge_id < 0: return
    
        debug('BRIDGE: Transform Object {}'.format(obj.foo.bridge_id))

        self.lib.SetObjectTransform(
            obj.foo.bridge_id,
            to_interop_matrix4x4(obj.matrix_world)
        )

    def on_update_geometry(self, obj, depsgraph):
        """Notify the bridge that object geometry has changed
        
        Parameters:
            obj (bpy.types.Object):             Object containing mesh data to read
            depsgraph (bpy.types.Depsgraph):    Dependency graph to use for generating a final mesh
        """
        if obj.foo.bridge_id < 0: return
        
        debug('BRIDGE: Update geometry {}'.format(obj))
        
        eval_obj = obj.evaluated_get(depsgraph)
        mesh = eval_obj.to_mesh()

        # Ensure triangulated faces are available
        mesh.calc_loop_triangles()

        # Need both vertices and loops. Vertices store normal information per-vertex
        # while loops align with loop_triangles
        debug('{} loops starting at {}'.format(len(mesh.loops), mesh.loops[0].as_pointer()))
        self.lib.CopyVertices(
            obj.foo.bridge_id,
            mesh.loops[0].as_pointer(),
            len(mesh.loops),
            mesh.vertices[0].as_pointer(),
            len(mesh.vertices)
        )

        debug('{} loop_triangles starting at {}'.format(len(mesh.loop_triangles), mesh.loop_triangles[0].as_pointer()))
        self.lib.CopyLoopTriangles(
            obj.foo.bridge_id,
            mesh.loop_triangles[0].as_pointer(),
            len(mesh.loop_triangles)
        )

        eval_obj.to_mesh_clear()
    
    
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
        debug('Created render engine')
        
        self.visible_ids = []
        
        self.viewport_id = -1
        self.width = 0
        self.height = 0
        
        self.created = False
        
        self.shader = gpu.shader.from_builtin('2D_IMAGE')
        self.batch = batch_for_shader(self.shader, 'TRI_FAN', {
            'pos': ((0, 0), (100, 0), (100, 100), (0, 100)),
            'texCoord': ((0, 0), (1, 0), (1, 1), (0, 1)),
        })

        log('Init RenderEngine at {}'.format(id(self)))

        self.bindcode = -1
        self.texture_width = 0
        self.texture_height = 0
        self.texture_frame = -1
        self.lens = None 
        self.view_matrix = None 

        # Mock spin-up test
        # rt = RenderTextureData()
        # rt.viewportId = -1
        # rt.width = 800
        # rt.height = 600
        # rt.pixels = pointer(c_ubyte(self.bindcode))
        # self.update_render_texture(rt)
        
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
        region = context.region
        view3d = context.space_data
        region3d = context.region_data
        scene = depsgraph.scene

        if not self.created:
            self.on_create_viewport(region)
            self.created = True
            
        # debug("vViewMatrix", region3d.view_matrix.transposed())
        # debug("vProjectionMatrix", region3d.window_matrix.transposed())
        # debug('vCameraMatrix', region3d.view_matrix.inverted().transposed())

        # Update our list of objects visible to this viewport
        visible_ids = []
        for obj in scene.objects:
            if not obj.visible_get():
                continue
            
            if obj.foo.bridge_id > 0:
                visible_ids.append(obj.foo.bridge_id)
        
        # Only notify for a change if the list was modified
        visible_ids.sort()
        if visible_ids != self.visible_ids:
            self.on_changed_visible_ids(visible_ids)
        
        self.on_update_render_image()
        
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

    
    def update_render_texture(self, rt):
        """Upload pixels from the provided render texture if modified since last upload.

        Parameters:
            rt (RenderTextureData)
        """
        # Invalid texture data. Skip
        if rt.width == 0 or rt.height == 0:
            debug('** Skip RT - Zero size')
            return

        # Same frame ID (thus same pixels). Skip
        if rt.frame == self.texture_frame:
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
        if rt.width != self.texture_width or rt.height != self.texture_height:
            create_new = True
                
        glActiveTexture(GL_TEXTURE0) # TODO: Needed?
        glBindTexture(GL_TEXTURE_2D, self.bindcode)

        # pixels = Buffer(GL_UNSIGNED_BYTE, rt.width * rt.height * 3, rt.pixels)
        p_pixels = cast(rt.pixels, c_void_p).value

        if create_new:
            log('** NEW Texture {} x {}'.format(rt.width, rt.height))
            
            # TODO: Would be nice if I didn't have to match pixel resolution here.
            # Use an alternate shader that doesn't need this?
            self.batch = batch_for_shader(self.shader, 'TRI_FAN', {
                'pos': ((0, 0), (rt.width, 0), (rt.width, rt.height), (0, rt.height)),
                'texCoord': ((0, 0), (1, 0), (1, 1), (0, 1)),
            })
            
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB, rt.width, rt.height, 0, GL_RGB, GL_UNSIGNED_BYTE, p_pixels)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST)
            
            # self.dump_rt_to_image(rt)
        else: # We can just write pixels into the existing space
            glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, rt.width, rt.height, GL_RGB, GL_UNSIGNED_BYTE, p_pixels)

        # Track to compare to the next read
        self.texture_width = rt.width 
        self.texture_height = rt.height 
        self.texture_frame = rt.frame

    def on_update_render_image(self):
        """Poll a new render texture image from the bridge and update our local texture
        """
        debug('BRIDGE: Read render image for viewport {}'.format(self.viewport_id))

        rt = bridge_driver().lib.GetRenderTexture(self.viewport_id)
        self.update_render_texture(rt)

        
    def on_create_viewport(self, region):
        """Notify the bridge that a new viewport has been added
        
        Parameters:
            region (bpy.types.Region): The viewport region
        """
        self.width = region.width
        self.height = region.height
        
        # TODO: Give this a viewport ID or something and register with the bridge.
        # And pull from some cache if we're just toggling... somehow. Should be able
        # to store that data somewhere right? Like on a SpaceView3D?
        self.viewport_id = int.from_bytes(os.urandom(2), byteorder='big')
        
        print('create viewport {}'.format(id(self)))
        bridge_driver().add_viewport(self)

    def on_change_dimensions(self, width, height):
        """Notify the bridge that our viewport dimensions have changed
        
        Parameters:
            width (int): Viewport width in pixels
            height (int): Viewport height in pixels
        """
        self.width = width
        self.height = height
        
        log('Update Viewport Dimensions to {} x {}'.format(width, height))
        bridge_driver().lib.ResizeViewport(self.viewport_id, self.width, self.height)
    
    def on_change_camera_matrix(self, space):
        """Notify the bridge that our viewport camera has been changed

        Parameters:
            space (bpy.types.SpaceView3D)
        """
        region = space.region_3d

        # If our view changed, push the update to the bridge
        if space.lens != self.lens or region.view_matrix != self.view_matrix:
            self.lens = space.lens
            self.view_matrix = copy(region.view_matrix)

            camera = InteropCamera()
            camera.lens = self.lens
            camera.position = to_interop_vector3(region.view_matrix.inverted().translation)
            camera.forward = to_interop_vector3(region.view_rotation @ Vector((0.0, 0.0, -1.0)))
            camera.up = to_interop_vector3(region.view_rotation @ Vector((0.0, 1.0, 0.0)))

            # TODO: This is sent from the render thread. Is that safe?
            # Would I need to add a lock for this in the DLL?

            bridge_driver().lib.SetViewportCamera(self.viewport_id, camera)
        
            # bridge.lib.SetViewportCamera(
            #     self.viewport_id, 
            #     to_interop_matrix4x4(region.view_matrix)
            # )
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

            # view_distance changes for ortho cameras as we scroll in/out
            # forward Vector * distance = offset position? 


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

    def view_draw(self, context, depsgraph):
        """Called whenever Blender redraws the 3D viewport"""
        scene = depsgraph.scene
        region = context.region
        region3d = context.region_data
        
        if not self.created:
            self.on_create_viewport(region)
            self.created = True
            
        # Check for an updated viewport size
        # TODO: Should happen @ render, not view_update() since resizing the 
        # viewport does render but doesn't fire view_update()
        if self.width != region.width or self.height != region.height:
            self.on_change_dimensions(region.width, region.height)
            
        debug('Redraw viewport {}'.format(self.viewport_id))
        self.on_change_camera_matrix(context.space_data)
        self.on_update_render_image()

        self.bind_display_space_shader(scene)
        
        glEnable(GL_DEPTH_TEST)
        
        clear_color = scene.foo.clear_color
        glClearColor(clear_color[0], clear_color[1], clear_color[2], 1.0)
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT)
        
        if self.bindcode != -1:
            glActiveTexture(GL_TEXTURE0)
            glBindTexture(GL_TEXTURE_2D, self.bindcode)

        self.shader.bind()
        self.shader.uniform_int('image', 0)
        self.batch.draw(self.shader)
        
        self.unbind_display_space_shader()

        glDisable(GL_BLEND)


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
    bridge_id: IntProperty(
        name='Bridge ID',
        default=-1,
        description='Unique ID used by the bridge DLL'
    )
    
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


class BridgeTimerOperator(bpy.types.Operator):
    """
        Wonky way of performing a timer that also 
        can also flag the viewport as dirty every execution
    """
    bl_idname = "wm.bridge_timer_operator"
    bl_label = "Bridge Timer Operator"

    _timer = None

    def modal(self, context, event):
        context.area.tag_redraw()

        bridge = bridge_driver()
        bridge.on_tick()

        if event.type in {'RIGHTMOUSE', 'ESC'}:
            self.cancel(context)
            return {'CANCELLED'}

        if event.type == 'TIMER':
            debug('TIMER')

        return {'PASS_THROUGH'}

    def execute(self, context):
        wm = context.window_manager
        self._timer = wm.event_timer_add(0.1, window=context.window)
        wm.modal_handler_add(self)
        return {'RUNNING_MODAL'}

    def cancel(self, context):
        wm = context.window_manager
        wm.event_timer_remove(self._timer)


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

#endregion Panels

#region Plugin Registration

# Classes to (un)register as part of this addon
CLASSLIST = (
    FooRenderEngine,
    
    # Operators
    SetupBridgeOperator,
    BridgeTimerOperator,
    
    # Settings
    FooSceneSettings,
    FooObjectSettings,
    
    # Renderer panels
    FOO_RENDER_PT_settings,
    FOO_RENDER_PT_settings_viewport,

    # Light panels
    FOO_LIGHT_PT_light
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
