
bl_info = {
    'name': 'Unity Coherence',
    'description': 'Use the Unity Engine as a viewport renderer',
    'author': 'Chase McManning',
    'version': (0, 1, 0),
    'blender': (2, 82, 0),
    'doc_url': 'https://github.com/McManning/Coherence/wiki',
    'tracker_url': 'https://github.com/McManning/Coherence/issues',
    'category': 'Render'
}

import os
import sys

# This is done to allow absolute imports from the root of this package.
# This lets us keep the same imports as we would while running through unittest.
path = os.path.dirname(os.path.realpath(__file__))
if path not in sys.path:
    sys.path.append(path)

if 'bpy' in locals():
    import importlib
    importlib.reload(util)
    util.registry.Registry.clear()
    importlib.reload(core)
    importlib.reload(api)
    importlib.reload(components)
else:
    import bpy
    from . import util
    from . import core
    from . import api
    from . import components

from util.registry import Registry

def register():
    bpy.types.VIEW3D_HT_header.append(core.panels.draw_view3d_header)
    bpy.types.RENDER_PT_context.append(core.panels.draw_render_header)

    # Register everything tagged with @autoregister
    Registry.register()

    # Register builtin plugins
    runtime = core.runtime.instance
    runtime.register_plugin(core.scene_objects.SceneObjects)
    runtime.register_plugin(core.image_sync.ImageSync)

    # Register builtin components
    components.mesh.register()
    components.metaballs.register()

def unregister():
    # Unregister builtin components
    components.mesh.unregister()
    components.metaballs.unregister()

    # And then all plugins
    core.runtime.instance.unregister_all_plugins()

    bpy.types.VIEW3D_HT_header.remove(core.panels.draw_view3d_header)
    bpy.types.RENDER_PT_context.remove(core.panels.draw_render_header)
    Registry.unregister()

if __name__ == '__main__':
    register()
