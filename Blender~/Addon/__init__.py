
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
    importlib.reload(plugins)
    importlib.reload(api)
else:
    import bpy
    from . import util
    from . import core
    from . import plugins
    from . import api

from util.registry import Registry


def register():
    bpy.types.VIEW3D_HT_header.append(core.panels.draw_view3d_header)
    bpy.types.RENDER_PT_context.append(core.panels.draw_render_header)

    # Register everything tagged with @autoregister
    Registry.register()

    # Register builtin plugins
    api.register_plugin(plugins.mesh.MeshPlugin)
    api.register_plugin(plugins.metaballs.MetaballsPlugin)

def unregister():
    # Unregister *all* plugins, including 3rd party
    core.runtime.instance.unregister_all_plugins()

    bpy.types.VIEW3D_HT_header.remove(core.panels.draw_view3d_header)
    bpy.types.RENDER_PT_context.remove(core.panels.draw_render_header)
    Registry.unregister()

if __name__ == '__main__':
    register()
