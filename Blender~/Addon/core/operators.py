
from bpy.types import (
    Operator
)
from .driver import (
    bridge_driver
)

from util.registry import autoregister

@autoregister
class StartCoherenceOperator(Operator):
    """Start the Coherence connection with Unity"""
    bl_idname = 'coherence.start'
    bl_label = 'Start Coherence'

    @classmethod
    def poll(cls, context):
        bridge = bridge_driver()
        return not bridge.is_running()

    def execute(self, context):
        bridge_driver().start()

        context.scene.render.engine = 'COHERENCE'

        try:
            context.space_data.shading.type = 'RENDERED'
        except:
            pass

        return { 'FINISHED' }

@autoregister
class StopCoherenceOperator(Operator):
    """Close the Coherence connection with Unity"""
    bl_idname = 'coherence.stop'
    bl_label = 'Stop Coherence'

    @classmethod
    def poll(cls, context):
        bridge = bridge_driver()
        return bridge.is_running()

    def execute(self, context):
        bridge_driver().stop()
        return { 'FINISHED' }
