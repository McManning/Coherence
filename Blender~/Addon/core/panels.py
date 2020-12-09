
from bpy.types import (
    Panel
)

from .driver import (
    bridge_driver
)

from util.registry import autoregister

class BasePanel(Panel):
    bl_space_type = 'PROPERTIES'
    bl_region_type = 'WINDOW'
    bl_context = 'render'
    COMPAT_ENGINES = 'coherence_renderer'

    @classmethod
    def poll(cls, context):
        return context.engine in cls.COMPAT_ENGINES

@autoregister
class COHERENCE_RENDER_PT_settings(BasePanel):
    """Parent panel for renderer settings"""
    bl_label = 'Coherence Renderer Settings'

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        settings = context.scene.coherence

        bridge = bridge_driver()

        if bridge.is_running():
            layout.label(text='Running Coherence')
            layout.operator('scene.toggle_coherence', text='Stop Coherence')
        else:
            layout.operator('scene.toggle_coherence', text='Start Coherence')

        if not bridge.is_connected():
            layout.label(text='Not connected')
        else:
            layout.label(text='Connected to Unity')

        col = layout.column(align=True)
        col.prop(settings, 'connection_name')

@autoregister
class COHERENCE_RENDER_PT_settings_viewport(BasePanel):
    """Global viewport configurations"""
    bl_label = 'Viewport'
    bl_parent_id = 'COHERENCE_RENDER_PT_settings'

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        settings = context.scene.coherence

        col = layout.column(align=True)
        col.prop(settings, 'clear_color')

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
class COHERENCE_MATERIAL_PT_settings(BasePanel):
    bl_label = 'Unity Material Settings'
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

        col.prop(settings, 'use_override_name')
        if settings.use_override_name:
            col.prop(settings, 'override_name')

@autoregister
class COHERENCE_MATERIAL_PT_settings_sync(BasePanel):
    bl_label = 'Texture Sync'
    bl_parent_id = 'COHERENCE_MATERIAL_PT_settings'

    def draw_header(self,context):
        self.layout.prop(context.material.coherence, 'use_sync_texture', text="", toggle=False)

    def draw(self, context):
        mat = context.material
        settings = mat.coherence

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
