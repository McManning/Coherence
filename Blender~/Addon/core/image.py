
from .driver import bridge_driver

class ImageEditing:
    def __init__(self, context):
        self.handle = bpy.types.SpaceImageEditor.draw_handler_add(
            self.draw_callback, (context,),
            'WINDOW', 'POST_PIXEL'
        )

    def draw_callback(self, context):
        space = context.space_data
        print(space)
        print(space.image)
        print(space.cursor_location)
        print(space.mode)
        print(space.zoom[0], space.zoom[1])

        # This still gets fired for pan/zoom

        if space.mode == 'PAINT' and space.image:
            settings = space.image.coherence
            if settings.texture_slot[0] != '-': # TODO: Better "if defined..."
                bridge_driver().sync_texture(settings.texture_slot, space.image)

    def remove_handle(self):
        bpy.types.SpaceImageEditor.draw_handler_remove(self.handle, 'WINDOW')
