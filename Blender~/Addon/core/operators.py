
import bpy

from .driver import (
    bridge_driver
)

from util.registry import autoregister

@autoregister
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

@autoregister
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

