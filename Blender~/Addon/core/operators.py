
from bpy.types import (
    Operator
)

from bpy.props import (
    StringProperty,
    EnumProperty
)

from . import runtime
from . import scene_objects
from util.registry import autoregister

@autoregister
class COHERENCE_OT_Start(Operator):
    """Start the Coherence connection with Unity"""
    bl_idname = 'coherence.start'
    bl_label = 'Start Coherence'

    @classmethod
    def poll(cls, context):
        return not runtime.instance.is_running()

    def execute(self, context):
        runtime.instance.start()

        context.scene.render.engine = 'COHERENCE'

        try:
            context.space_data.shading.type = 'RENDERED'
        except:
            pass

        return { 'FINISHED' }

@autoregister
class COHERENCE_OT_Stop(Operator):
    """Close the Coherence connection with Unity"""
    bl_idname = 'coherence.stop'
    bl_label = 'Stop Coherence'

    @classmethod
    def poll(cls, context):
        return runtime.instance.is_running()

    def execute(self, context):
        runtime.instance.stop()
        return { 'FINISHED' }

@autoregister
class COHERENCE_LIST_OT_AddComponent(Operator):
    bl_idname = 'coherence.add_component'
    bl_label = 'Add Component'
    bl_description = 'Add a new component to an object'
    bl_options = {'INTERNAL'}

    def get_available_components(self, context):
        plugin = runtime.instance.get_plugin(scene_objects.SceneObjects)
        components = plugin.get_available_components(context.object)

        enum_items = []
        for component in components:
            name = component.name()
            enum_items.append((name, name, 'TODO: Docs here'))

        return enum_items

    available_components: EnumProperty(
        name='Available Components',
        items=get_available_components
    )

    def execute(self, context):
        plugin = runtime.instance.get_plugin(scene_objects.SceneObjects)

        name = (e for e in self.get_available_components(context) if e[0] == self.available_components).__next__()[1]

        plugin.add_component_by_name(context.object, name)
        return {'FINISHED'}

@autoregister
class COHERENCE_OT_DestroyComponent(Operator):
    bl_idname = 'coherence.destroy_component'
    bl_label = 'Destroy Component'
    bl_description = 'This will also destroy the linked Unity component'
    bl_options = {'INTERNAL'}

    component_name: StringProperty()

    def execute(self, context):
        plugin = runtime.instance.get_plugin(scene_objects.SceneObjects)
        obj = context.object

        plugin.destroy_component_by_name(obj, self.component_name)
        return {'FINISHED'}
