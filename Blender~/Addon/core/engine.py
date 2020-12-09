
import os
from ctypes import *
from ctypes import wintypes
import bpy
import numpy as np
import gpu
import blf
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

from .driver import (
    bridge_driver
)

from .utils import (
    log,
    debug,
    warning,
    error,
    get_object_uid
)

from .interop import *

from util.registry import autoregister

@autoregister
class CoherenceRenderEngine(bpy.types.RenderEngine):
    bl_idname = "coherence_renderer"
    bl_label = "Unity Renderer"
    bl_use_preview = False

    # Panels that we don't register this engine with
    exclude_panels = {
        'VIEWLAYER_PT_filter',
        'VIEWLAYER_PT_layer_passes',
        'RENDER_PT_freestyle',
        'RENDER_PT_simplify',
        # TODO: Remove Color Management panel - done via Unity
    }

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

        # self.lock = threading.Lock()

        self.viewport_id = int.from_bytes(os.urandom(2), byteorder='big')
        self.connected = False

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
        # Not relevant here - since we're working in viewport/realtime
        pass

    def view_update(self, context, depsgraph):
        """Called when a scene or 3D viewport changes"""
        # Update our list of objects visible to this viewport
        # visible_ids = []
        # for obj in depsgraph.scene.objects:
        #    if not obj.visible_get():
        #        continue
        #
        #   uid = get_object_uid(obj)
        #   visible_ids.append(uid)

        # Track bridge's connection status to determine how this VP renders
        self.connected = bridge_driver().is_connected()

        # Only notify for a change if the list was modified
        #visible_ids.sort()
        #if visible_ids != self.visible_ids:
        #    self.on_changed_visible_ids(visible_ids)

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

    # def dump_rt_to_image(self, rt):
    #     # https://blender.stackexchange.com/a/652
    #     image = bpy.data.images.new("Results", width=rt.width, height=rt.height)

    #     pixels = [None] * rt.width * rt.height * 4
    #     for i in range(rt.width * rt.height):
    #         src = i * 3
    #         dst = i * 4
    #         if i < 50:
    #             debug('{}, {}, {}'.format(rt.pixels[src], rt.pixels[src+1], rt.pixels[src+2]))
    #         pixels[dst] = rt.pixels[src] / 255.0
    #         pixels[dst+1] = rt.pixels[src+1] / 255.0
    #         pixels[dst+2] = rt.pixels[src+2] / 255.0
    #         pixels[dst+3] = 0.0

    #     image.pixels = pixels

    #     # alternatively, dump into numpy?

    def rebuild_texture(self, frame, width, height, pixels):
        # Invalid texture data or we're still on the same frame (thus same pixels). Skip.
        if width == 0 or height == 0 or frame == self.texture_frame:
            return

        create_new = False

        # If we haven't created a resource ID yet, do so now.
        if self.bindcode == -1:
            buf = Buffer(GL_INT, 1)
            glGenTextures(1, buf)
            self.bindcode = buf[0]
            create_new = True
            log('Create viewport texture bind code {}'.format(self.bindcode))

        # If the render texture resizes, we'll need to reallocate
        if width != self.texture_width or height != self.texture_height:
            create_new = True

        glActiveTexture(GL_TEXTURE0) # TODO: Needed?
        glBindTexture(GL_TEXTURE_2D, self.bindcode)

        if create_new:
            debug('glTexImage2D using {} x {} at {}'.format(width, height, pixels))

            # TODO: Would be nice if I didn't have to match pixel resolution here.
            # Use an alternate shader that doesn't need this?
            self.batch = batch_for_shader(self.shader, 'TRI_FAN', {
                'pos': ((0, 0), (width, 0), (width, height), (0, height)),
                'texCoord': ((0, 0), (1, 0), (1, 1), (0, 1)),
            })

            # GL_BGRA is preferred on Windows according to https://www.khronos.org/opengl/wiki/Common_Mistakes#Slow_pixel_transfer_performance
            # Additionally - we're doing 24 BPP to avoid transferring the alpha channel (upper bound 2 MB per frame)
            # but that'll also probably be a slowdown when uploading. Need to benchmark both solutions. And maybe
            # eventually pack multiple viewport outputs to the same render texture.
            glPixelStorei(GL_UNPACK_ALIGNMENT, 1)
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB8, width, height, 0, GL_RGB, GL_UNSIGNED_BYTE, pixels)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST)

        else: # We can just write pixels into the existing space
            # debug('glTexSubImage2D using {} x {} at {}'.format(width, height, pixels))
            glPixelStorei(GL_UNPACK_ALIGNMENT, 1)
            glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, width, height, GL_RGB, GL_UNSIGNED_BYTE, pixels)

        # Track to compare to the next read
        self.texture_width = width
        self.texture_height = height
        self.texture_frame = frame

    def on_changed_visible_ids(self, visible_ids):
        """Notify the bridge that the visibility list has changed

        Parameters:
            visible_ids (List[int])
        """
        debug('BRIDGE: Update Visible ID List {}'.format(visible_ids))

        # TODO: Reimplement using object names

        #self.visible_ids = visible_ids

        #visible_ids_ptr = (c_int * len(visible_ids))(*visible_ids)
        #bridge_driver().lib.SetVisibleObjects(
        #    self.viewport_id,
        #    visible_ids_ptr,
        #    len(visible_ids)
        #)

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

    # def stress_test_texture(self):
    #     # TEST: Works
    #     width = self.camera.width
    #     height = self.camera.height

    #     # the fact that this isn't showing (1, 1, 1) means I have another issue.
    #     pixels = np.ones(width * height * 3) # rt.pixels
    #     p_pixels = pixels.ctypes.data_as(POINTER(c_float))
    #     vp_pixels = cast(p_pixels, c_void_p).value

    #     self.rebuild_texture(
    #         self.texture_frame + 1,
    #         width,
    #         height,
    #         vp_pixels
    #     )

    def update_render_texture(self):
        lib = bridge_driver().lib
        rt = lib.GetRenderTexture(self.viewport_id)

        # pixels = Buffer(GL_UNSIGNED_BYTE, rt.width * rt.height * 3, rt.pixels)
        p_pixels = cast(rt.pixels, c_void_p).value

        self.rebuild_texture(
            rt.frame,
            rt.width,
            rt.height,
            p_pixels
        )

        # TODO: Dealloc the replacement memory manually?

        lib.ReleaseRenderTextureLock(self.viewport_id)

    def view_draw(self, context, depsgraph):
        """Called whenever Blender redraws the 3D viewport"""
        scene = depsgraph.scene

        # debug('[RENDER] start')
        self.update_viewport_camera(context)
        self.update_render_texture()

        # glEnable(GL_DEPTH_TEST)

        if self.connected and self.bindcode != -1:
            self.bind_display_space_shader(scene)
            self.shader.bind()

            glActiveTexture(GL_TEXTURE0)
            glBindTexture(GL_TEXTURE_2D, self.bindcode)

            self.shader.uniform_int('image', 0)
            self.batch.draw(self.shader)

            self.unbind_display_space_shader()
        elif not self.connected:
            glClearColor(0, 0, 0, 1.0)
            glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT)

            blf.position(0, 15, 30, 0)
            blf.size(0, 20, 72)
            blf.color(0, 0.8, 0, 0, 1.0)
            blf.draw(0, 'Not Connected to Unity')

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


        # glDisable(GL_BLEND)
        # debug('[RENDER] end')

