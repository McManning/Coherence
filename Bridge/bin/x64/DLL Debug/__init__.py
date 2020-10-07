
bl_info = {
    'name': 'Unity Viewport Renderer',
    'description': 'Use the Unity Engine as a viewport renderer',
    'author': 'Chase McManning',
    'version': (0, 1, 0),
    'blender': (2, 82, 0),
    'doc_url': 'https://github.com/McManning/TBD/wiki',
    'tracker_url': 'https://github.com/McManning/TBD/issues',
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
else:
    import bpy 
    from . import util
    from . import core

import bpy

from util.registry import Registry

def register():
    Registry.register()

def unregister():
    Registry.unregister()

if __name__ == '__main__':
    register()
