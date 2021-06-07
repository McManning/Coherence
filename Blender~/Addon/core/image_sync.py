
import bpy
import numpy as np

from .plugin import Plugin
from . import (runtime, interop)

MAX_TEXTURE_SLOTS = 64
UNASSIGNED_TEXTURE_SLOT_NAME = '-- Unassigned --'

def get_texture_slots() -> list:
    """Return all sync-able texture slot names exposed by the host

    Returns:
        list[str]
    """
    # if not runtime.instance.is_connected():
    #     return []

    buffer = (interop.InteropString64 * MAX_TEXTURE_SLOTS)()
    size = interop.lib.GetTextureSlots(buffer, len(buffer))

    # Convert byte arrays to a list of strings.
    return [UNASSIGNED_TEXTURE_SLOT_NAME] + [buffer[i].buffer.decode('utf-8') for i in range(size)]

class ImageSync(Plugin):
    """Sync Blender Images with Unity Render Textures"""

    #: Draw handler for :class:`bpy.types.SpaceImageEditor`
    image_editor_handle = None

    #: np.ndarray: Numpy array with pixel data for the active :class:`bpy.types.Image`
    image_buffer = None # np.ndarray

    def on_start(self):
        bpy.app.timers.register(self.check_texture_sync)

        # Monitor updates in SpaceImageEditor for texture syncing
        self.image_editor_handle = bpy.types.SpaceImageEditor.draw_handler_add(
            self.on_image_editor_update, (bpy.context,),
            'WINDOW', 'POST_PIXEL'
        )

    def on_stop(self):
        if self.image_editor_handle:
            bpy.types.SpaceImageEditor.draw_handler_remove(self.image_editor_handle, 'WINDOW')
            self.image_editor_handle = None

    def sync_texture(self, image):
        """Send updated pixel data for a texture to the host

        Args:
            image (bpy.types.Image): The image to sync
        """
        settings = image.coherence
        if settings.error or settings.texture_slot == UNASSIGNED_TEXTURE_SLOT_NAME:
            return

        # TODO: Optimize further (e.g. don't allocate the numpy buffer each time, etc)
        w, h = image.size

        if self.image_buffer is None:
            self.image_buffer = np.empty(w * h * 4, dtype=np.float32)
        else:
            self.image_buffer.resize(w * h * 4, refcheck=False)

        image.pixels.foreach_get(self.image_buffer)
        pixels_ptr = self.image_buffer.ctypes.data

        interop.lib.UpdateTexturePixels(
            interop.get_string_buffer(settings.texture_slot),
            image.size[0],
            image.size[1],
            pixels_ptr
        )

    def check_texture_sync(self) -> float:
        """
        Push image updates to the host if we're actively drawing
        on an image bound to one of the synced texture slots

        Returns:
            float: Milliseconds until the next check
        """
        delay = bpy.context.scene.coherence.texture_slot_update_frequency

        # Don't do anything if we're still not connected
        if bpy.context.mode != 'PAINT_TEXTURE' or not runtime.instance.is_connected():
            return delay

        image = bpy.context.tool_settings.image_paint.canvas

        # Tool is active but we don't have an image assigned
        if image is None:
            return delay

        self.sync_texture(image)

        return delay

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
