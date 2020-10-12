import bpy 
from .debug import debug

class Registry:
    """Manager for a set of classes to (un)register with bpy"""
    classes = []

    def __call__(self, cls):
        Registry.add(cls)

    @classmethod
    def add(cls, instance):
        if instance not in cls.classes:
            cls.classes.append(instance)
    
    @classmethod
    def register(cls):
        for c in cls.classes:
            debug('[Autoregister] Register {}'.format(c))
            bpy.utils.register_class(c)
            
            # If registering a RenderEngine, notify panels
            count = 0
            if issubclass(c, bpy.types.RenderEngine):
                name = c.bl_idname
                for panel in get_panels(c.exclude_panels):
                    # debug('[Autoregister] Register {} to Panel {}'.format(
                    #     name,
                    #     panel
                    # ))
                    panel.COMPAT_ENGINES.add(name)
                    count = count + 1

                debug('[Autoregister] Register {} to {} Panels '.format(name, count))

    @classmethod 
    def unregister(cls):
        render_engine_id = None

        for c in reversed(cls.classes):
            debug('[Autoregister] Unregister {}'.format(c))

            try:
                bpy.utils.unregister_class(c)
            except Exception as e:
                # May not be registered - that's okay
                debug('[Autoregister] ERROR for {} : {}'.format(c, e))

            # If unregistering a RenderEngine, notify panels
            count = 0
            if issubclass(c, bpy.types.RenderEngine):
                name = c.bl_idname
                for panel in get_panels([]):
                    if name in panel.COMPAT_ENGINES:
                        # debug('[Autoregister] Unregister {} from Panel {}'.format(
                        #     name,
                        #     panel
                        # ))
                        panel.COMPAT_ENGINES.remove(name)
                        count = count + 1

                debug('[Autoregister] Unregister {} from {} Panels '.format(name, count))

    @classmethod
    def clear(cls):
        cls.classes = []

def autoregister(cls):
    """Decorator to automatically add a class to the registry when imported"""
    Registry.add(cls)
    return cls

def get_panels(exclude_panels):
    """UI Panels that the RenderEngine is compatible with
    
    Properties:
        exclude_panels (list[str]): Panel names to not register the RenderEngine with
    """
    # Ref: https://docs.blender.org/api/current/bpy.types.RenderEngine.html
    panels = []
    for panel in bpy.types.Panel.__subclasses__():
        if hasattr(panel, 'COMPAT_ENGINES') and 'BLENDER_RENDER' in panel.COMPAT_ENGINES:
            if panel.__name__ not in exclude_panels:
                panels.append(panel)

    return panels

