
from bpy.types import (
    Panel
)

from .driver import (
    bridge_driver
)

from util.registry import autoregister

def draw_view3d_header(self, context):
    """Draw a run toggle button in the header of Blender's View3D"""
    layout = self.layout

    # Hide if the user doesn't want to see the button
    settings = context.scene.coherence
    if not settings.show_view3d_controls:
        return

    if bridge_driver().is_running():
        layout.operator('coherence.stop', icon='X')
    else:
        layout.operator('coherence.start', icon='PLAY')

def draw_render_header(self, context):
    """Draw a toggle button below engine selection in render settings"""
    layout = self.layout
    layout.use_property_split = True
    layout.use_property_decorate = False

    row = layout.row(align=True)
    row.alignment = 'RIGHT'

    if context.engine == 'COHERENCE':
        if bridge_driver().is_running():
            row.operator('coherence.stop', icon='X')
        else:
            row.operator('coherence.start', icon='PLAY')

class BasePanel(Panel):
    bl_space_type = 'PROPERTIES'
    bl_region_type = 'WINDOW'
    bl_context = 'render'
    COMPAT_ENGINES = 'COHERENCE'

    @classmethod
    def poll(cls, context):
        return context.engine in cls.COMPAT_ENGINES

@autoregister
class COHERENCE_IMAGEPAINT_PT_texture_sync(Panel):
    """Creates a Panel in the scene context of the properties editor"""
    bl_label = 'Coherence Texture Sync'

    bl_space_type = 'IMAGE_EDITOR'
    bl_region_type = 'UI'
    bl_category = 'Image' # gets jammed into a 'Misc' tab if not set

    #bl_space_type = 'VIEW_3D'
    #bl_region_type = 'UI'
    #bl_category = 'Tool'

    # Tool contexts ref: https://blender.stackexchange.com/a/73154
    # bl_context = 'imagepaint'

    def draw(self, context):
        layout = self.layout

        print(context)
        image = context.space_data.image

        if not image:
            layout.label(text='Select an image', icon='ERROR')
            return

        settings = image.coherence

        if settings.error:
            layout.label(text=settings.error, icon='ERROR')

        if not bridge_driver().is_connected():
            layout.label(
                text='Not connected to Unity.',
                icon='ERROR'
            )
            return

        layout.prop(image.coherence, 'texture_slot')

@autoregister
class COHERENCE_RENDER_PT_settings(BasePanel):
    """Panel for the main renderer settings"""
    bl_label = 'Coherence Settings'

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        settings = context.scene.coherence

        layout.prop(settings, 'show_view3d_controls')

@autoregister
class COHERENCE_RENDER_PT_settings_advanced(BasePanel):
    bl_label = 'Advanced Settings'
    bl_parent_id = 'COHERENCE_RENDER_PT_settings'
    bl_options = { 'DEFAULT_CLOSED' }

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        settings = context.scene.coherence

        connected = bridge_driver().is_connected()

        if connected:
            layout.label(text='The below settings cannot be modified while Coherence is running', icon='ERROR')

        layout.enabled = not connected

        # col = layout.column(align=True)
        layout.prop(settings, 'connection_name')
        layout.prop(settings, 'texture_slot_update_frequency')

@autoregister
class COHERENCE_LIGHT_PT_light(BasePanel):
    """Custom per-light settings editor for this render engine"""
    bl_label = 'Light'
    bl_context = 'data'

    @classmethod
    def poll(cls, context):
        return context.light and BasePanel.poll(context)

    def draw(self, context):
        layout = self.layout
        self.layout.label(text='Not supported. Use lights within Unity.')

@autoregister
class COHERENCE_OBJECT_PT_settings(BasePanel):
    bl_label = 'Coherence Settings'
    bl_context = 'object'

    @classmethod
    def poll(cls, context):
        #if context.active_object and context.active_object.type == 'GPENCIL':
        #    return False

        return context.object and BasePanel.poll(context)

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        settings = context.object.coherence

        col = layout.column(align=True)
        col.prop(settings, 'display_mode')

@autoregister
class COHERENCE_MATERIAL_PT_settings(BasePanel):
    bl_label = 'Coherence Settings'
    bl_context = 'material'

    @classmethod
    def poll(cls, context):
        return context.material and BasePanel.poll(context)

    def draw(self, context):
        mat = context.material
        settings = mat.coherence

        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        col = layout.column()

        # col.prop(settings, 'use_override_name')
        # if settings.use_override_name:
        #    col.prop(settings, 'override_name')

@autoregister
class COHERENCE_PT_context_material(BasePanel):
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
